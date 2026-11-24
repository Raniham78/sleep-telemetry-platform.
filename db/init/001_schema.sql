CREATE TABLE IF NOT EXISTS telemetry_events (
    event_id UUID PRIMARY KEY,
    device_id VARCHAR(80) NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL,
    temperature_celsius DOUBLE PRECISION NOT NULL,
    heart_rate_bpm INTEGER NOT NULL,
    movement DOUBLE PRECISION NOT NULL,
    received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_temperature_range CHECK (temperature_celsius BETWEEN 0 AND 50),
    CONSTRAINT ck_heart_rate_range CHECK (heart_rate_bpm BETWEEN 25 AND 240),
    CONSTRAINT ck_movement_range CHECK (movement BETWEEN 0 AND 10)
);

CREATE INDEX IF NOT EXISTS ix_telemetry_events_device_recorded
    ON telemetry_events (device_id, recorded_at DESC);

CREATE TABLE IF NOT EXISTS hourly_device_summary (
    device_id VARCHAR(80) NOT NULL,
    hour_utc TIMESTAMPTZ NOT NULL,
    event_count BIGINT NOT NULL,
    avg_temperature DOUBLE PRECISION NOT NULL,
    avg_heart_rate DOUBLE PRECISION NOT NULL,
    PRIMARY KEY (device_id, hour_utc)
);
