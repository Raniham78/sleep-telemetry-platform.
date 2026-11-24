# Sleep Telemetry & Distributed Processing Platform

A portfolio project demonstrating an event-driven telemetry platform inspired by connected sleep products. It uses simulated device data only and is not a medical product.

## What it demonstrates

- ASP.NET Core 8 Minimal API in C#.
- Entity Framework Core 8 with PostgreSQL for query/read operations.
- RabbitMQ for asynchronous, durable message delivery.
- A Python analytics worker that consumes events and maintains hourly summaries.
- Docker Compose for a reproducible local environment.
- xUnit tests and GitHub Actions CI.

## Architecture

```text
Device simulator / client
          |
          | POST /api/v1/telemetry
          v
ASP.NET Core 8 API  -----> RabbitMQ (telemetry.ingest)
          |                            |
          | GET telemetry               v
          |                    Python analytics worker
          |                            |
          +---- Entity Framework Core  |
                        |              |
                        v              v
                    PostgreSQL <-------+
                 telemetry_events
                 hourly_device_summary
```

The API validates a request, assigns an event ID and publishes it to RabbitMQ. The worker persists the raw event and updates an hourly aggregate in a single database transaction. The API uses EF Core as its query/read layer. This is an eventually consistent design: a newly accepted event may take a short time to appear in GET results.

## Run locally

Requirements: Docker Desktop or Docker Engine with Docker Compose.

```bash
docker compose up --build
```

- API liveness: http://localhost:8080/health/live
- API readiness: http://localhost:8080/health/ready
- RabbitMQ management: http://localhost:15672 (development credentials are in docker-compose.yml)

## Send a sample event

```bash
curl -X POST http://localhost:8080/api/v1/telemetry \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"pod-001","recordedAt":"2026-11-24T21:30:00Z","temperatureCelsius":29.5,"heartRateBpm":62,"movement":0.12}'
```

Retrieve recent events:

```bash
curl "http://localhost:8080/api/v1/telemetry/pod-001?limit=50"
```

## Run tests

```bash
dotnet test SleepTelemetry.sln
python -m pip install -r src/Analytics.Worker/requirements.txt
cd src/Analytics.Worker && python -m unittest discover -s tests
```

## Design notes and limitations

- RabbitMQ queue is durable; published messages are persistent.
- The worker acknowledges a message only after the database transaction commits.
- Event IDs prevent duplicate raw events from incrementing hourly aggregates twice.
- API and worker can scale independently, subject to database capacity and queue depth.
- The current worker requeues failed messages immediately. A production implementation needs bounded retries, backoff and a dead-letter queue.
- The API publisher does not yet use publisher confirms. Add them before relying on publish success in production.
- Compose credentials are for local development only. Never reuse them outside a local environment.
- No throughput, cloud deployment or production SLO claims are made. Performance claims require repeatable load tests.
