import json
import os
import time
from datetime import datetime, timezone

import pika
import psycopg

RABBITMQ_HOST = os.getenv("RABBITMQ_HOST", "localhost")
RABBITMQ_USER = os.getenv("RABBITMQ_USER", "guest")
RABBITMQ_PASSWORD = os.getenv("RABBITMQ_PASSWORD", "guest")
DATABASE_URL = os.getenv(
    "DATABASE_URL",
    "postgresql://telemetry:telemetry_dev@localhost:5432/telemetry",
)


def connect_database():
    while True:
        try:
            conn = psycopg.connect(DATABASE_URL, autocommit=True)
            with conn.cursor() as cur:
                cur.execute("""
                    CREATE TABLE IF NOT EXISTS telemetry_events (
                        event_id UUID PRIMARY KEY,
                        device_id TEXT NOT NULL,
                        recorded_at TIMESTAMPTZ NOT NULL,
                        temperature_celsius DOUBLE PRECISION NOT NULL,
                        heart_rate_bpm INTEGER NOT NULL,
                        movement DOUBLE PRECISION NOT NULL,
                        received_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                    )
                """)
                cur.execute("""
                    CREATE INDEX IF NOT EXISTS ix_telemetry_device_recorded
                    ON telemetry_events (device_id, recorded_at DESC)
                """)
                cur.execute("""
                    CREATE TABLE IF NOT EXISTS hourly_device_summary (
                        device_id TEXT NOT NULL,
                        hour_utc TIMESTAMPTZ NOT NULL,
                        event_count INTEGER NOT NULL,
                        avg_temperature DOUBLE PRECISION NOT NULL,
                        avg_heart_rate DOUBLE PRECISION NOT NULL,
                        PRIMARY KEY (device_id, hour_utc)
                    )
                """)
            return conn
        except Exception as exc:
            print(f"Database unavailable: {exc}; retrying in 5 seconds", flush=True)
            time.sleep(5)


def process_message(conn, payload):
    event = json.loads(payload)
    recorded_at = datetime.fromisoformat(event["recordedAt"].replace("Z", "+00:00"))
    hour = recorded_at.astimezone(timezone.utc).replace(
        minute=0, second=0, microsecond=0
    )

    # Keep the raw event insert and aggregate update in one transaction.
    # If RabbitMQ redelivers an event, the unique event_id prevents double counting.
    with conn.transaction():
        with conn.cursor() as cur:
            cur.execute("""
                INSERT INTO telemetry_events
                    (event_id, device_id, recorded_at, temperature_celsius, heart_rate_bpm, movement)
                VALUES (%s, %s, %s, %s, %s, %s)
                ON CONFLICT (event_id) DO NOTHING
                RETURNING event_id
            """, (
                event["eventId"],
                event["deviceId"],
                recorded_at,
                event["temperatureCelsius"],
                event["heartRateBpm"],
                event["movement"],
            ))

            inserted = cur.fetchone()
            if inserted is None:
                return

            cur.execute("""
                INSERT INTO hourly_device_summary
                    (device_id, hour_utc, event_count, avg_temperature, avg_heart_rate)
                VALUES (%s, %s, 1, %s, %s)
                ON CONFLICT (device_id, hour_utc) DO UPDATE SET
                    event_count = hourly_device_summary.event_count + 1,
                    avg_temperature =
                        (hourly_device_summary.avg_temperature * hourly_device_summary.event_count
                         + EXCLUDED.avg_temperature)
                        / (hourly_device_summary.event_count + 1),
                    avg_heart_rate =
                        (hourly_device_summary.avg_heart_rate * hourly_device_summary.event_count
                         + EXCLUDED.avg_heart_rate)
                        / (hourly_device_summary.event_count + 1)
            """, (
                event["deviceId"],
                hour,
                event["temperatureCelsius"],
                event["heartRateBpm"],
            ))


def main():
    conn = connect_database()
    credentials = pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASSWORD)
    parameters = pika.ConnectionParameters(
        host=RABBITMQ_HOST,
        credentials=credentials,
        heartbeat=30,
        blocked_connection_timeout=30,
    )

    while True:
        try:
            connection = pika.BlockingConnection(parameters)
            channel = connection.channel()
            channel.queue_declare(queue="telemetry.ingest", durable=True)
            channel.basic_qos(prefetch_count=20)

            def callback(ch, method, properties, body):
                try:
                    process_message(conn, body)
                    ch.basic_ack(delivery_tag=method.delivery_tag)
                except Exception as exc:
                    print(f"Processing failed: {exc}", flush=True)
                    # Add bounded retries and a dead-letter queue before production use.
                    ch.basic_nack(delivery_tag=method.delivery_tag, requeue=True)

            channel.basic_consume(
                queue="telemetry.ingest",
                on_message_callback=callback,
            )
            print("Analytics worker is consuming telemetry.ingest", flush=True)
            channel.start_consuming()
        except Exception as exc:
            print(f"Broker connection lost: {exc}; retrying in 5 seconds", flush=True)
            time.sleep(5)


if __name__ == "__main__":
    main()
