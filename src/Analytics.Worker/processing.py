from datetime import datetime, timezone
import json


def process_message(connection, payload):
    event = json.loads(payload)
    recorded_at = datetime.fromisoformat(event["recordedAt"].replace("Z", "+00:00"))
    recorded_at = recorded_at.astimezone(timezone.utc)
    hour = recorded_at.replace(minute=0, second=0, microsecond=0)

    # The raw insert and aggregate update are atomic. Duplicate event IDs do not
    # increment the summary twice when RabbitMQ redelivers an acknowledged-late event.
    with connection.transaction():
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO telemetry_events
                    (event_id, device_id, recorded_at, temperature_celsius, heart_rate_bpm, movement)
                VALUES (%s, %s, %s, %s, %s, %s)
                ON CONFLICT (event_id) DO NOTHING
                RETURNING event_id
                """,
                (
                    event["eventId"],
                    event["deviceId"],
                    recorded_at,
                    event["temperatureCelsius"],
                    event["heartRateBpm"],
                    event["movement"],
                ),
            )

            if cursor.fetchone() is None:
                return

            cursor.execute(
                """
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
                """,
                (
                    event["deviceId"],
                    hour,
                    event["temperatureCelsius"],
                    event["heartRateBpm"],
                ),
            )
