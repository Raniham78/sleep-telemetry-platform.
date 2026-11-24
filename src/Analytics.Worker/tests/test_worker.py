import json
import unittest
from unittest.mock import MagicMock

from processing import process_message


class WorkerTests(unittest.TestCase):
    def setUp(self):
        self.connection = MagicMock()
        self.cursor = self.connection.cursor.return_value.__enter__.return_value

    def test_duplicate_event_does_not_update_hourly_aggregate(self):
        self.cursor.fetchone.return_value = None
        event = {
            "eventId": "e4f5c5a6-6789-4abc-9876-123456789012",
            "deviceId": "pod-001",
            "recordedAt": "2026-11-24T21:30:00Z",
            "temperatureCelsius": 29.5,
            "heartRateBpm": 62,
            "movement": 0.12,
        }

        process_message(self.connection, json.dumps(event).encode("utf-8"))

        self.assertEqual(self.cursor.execute.call_count, 1)

    def test_new_event_updates_raw_and_hourly_tables(self):
        self.cursor.fetchone.return_value = ("e4f5c5a6-6789-4abc-9876-123456789012",)
        event = {
            "eventId": "e4f5c5a6-6789-4abc-9876-123456789012",
            "deviceId": "pod-001",
            "recordedAt": "2026-11-24T21:30:00Z",
            "temperatureCelsius": 29.5,
            "heartRateBpm": 62,
            "movement": 0.12,
        }

        process_message(self.connection, json.dumps(event).encode("utf-8"))

        self.assertEqual(self.cursor.execute.call_count, 2)


if __name__ == "__main__":
    unittest.main()
