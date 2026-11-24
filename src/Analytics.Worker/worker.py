import os
import time

import pika
import psycopg
from processing import process_message

RABBITMQ_HOST = os.getenv("RABBITMQ_HOST", "localhost")
RABBITMQ_USER = os.getenv("RABBITMQ_USER", "telemetry")
RABBITMQ_PASSWORD = os.getenv("RABBITMQ_PASSWORD", "telemetry_dev_password")
DATABASE_URL = os.getenv(
    "DATABASE_URL",
    "postgresql://telemetry:telemetry_dev_password@localhost:5432/telemetry",
)


def connect_database():
    while True:
        try:
            return psycopg.connect(DATABASE_URL, autocommit=True)
        except Exception as exc:
            print(f"Database unavailable: {exc}; retrying in 5 seconds", flush=True)
            time.sleep(5)


def main():
    database = connect_database()
    credentials = pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASSWORD)
    parameters = pika.ConnectionParameters(
        host=RABBITMQ_HOST,
        credentials=credentials,
        heartbeat=30,
        blocked_connection_timeout=30,
    )

    while True:
        try:
            broker = pika.BlockingConnection(parameters)
            channel = broker.channel()
            channel.queue_declare(queue="telemetry.ingest", durable=True)
            channel.basic_qos(prefetch_count=20)

            def handle_message(ch, method, properties, body):
                try:
                    process_message(database, body)
                    ch.basic_ack(delivery_tag=method.delivery_tag)
                except Exception as exc:
                    print(f"Telemetry processing failed: {exc}", flush=True)
                    # Demo limitation: add bounded retries and a dead-letter queue for production.
                    ch.basic_nack(delivery_tag=method.delivery_tag, requeue=True)

            channel.basic_consume(
                queue="telemetry.ingest",
                on_message_callback=handle_message,
            )
            print("Analytics worker is consuming telemetry.ingest", flush=True)
            channel.start_consuming()
        except Exception as exc:
            print(f"Broker connection lost: {exc}; retrying in 5 seconds", flush=True)
            time.sleep(5)


if __name__ == "__main__":
    main()
