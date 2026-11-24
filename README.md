# Sleep Telemetry & Distributed Processing Platform

A portfolio project demonstrating an event-driven telemetry platform inspired by connected sleep devices.

## Architecture

- **Ingestion API** — ASP.NET Core 8 receives device telemetry and publishes events to RabbitMQ.
- **Message broker** — RabbitMQ buffers events and separates ingestion from processing.
- **Analytics worker** — a Python consumer validates events and computes hourly summaries.
- **Database** — PostgreSQL stores raw telemetry and aggregates.
- **Local environment** — Docker Compose.
- **CI** — GitHub Actions builds the .NET API and checks Python syntax.

> This is a learning and portfolio project. It does not claim production-scale throughput, cloud deployment, or production SRE experience.

## Run locally

Requirements: Docker Desktop or Docker Engine with Docker Compose.

```bash
docker compose up --build
```

API health endpoint: http://localhost:8080/health  
RabbitMQ management UI: http://localhost:15672 (guest / guest)

## Submit a telemetry event

```bash
curl -X POST http://localhost:8080/api/telemetry \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"pod-001","recordedAt":"2026-11-24T21:30:00Z","temperatureCelsius":29.5,"heartRateBpm":62,"movement":0.12}'
```

A successful request returns `202 Accepted` with the event identifier. The worker processes the event asynchronously.

## Design topics demonstrated

- Asynchronous, event-driven processing.
- Producer/consumer separation through a durable queue.
- Independent scaling of ingestion and processing components.
- Data validation and relational persistence.
- Basic idempotency for raw event insertion.
- Containerized local development and CI automation.

## Next improvements

Add bounded retries and a dead-letter queue, stronger idempotency for aggregate updates, authentication and authorization, OpenTelemetry traces and metrics, integration tests, load tests, and a cloud deployment. Document measured performance only after running tests.

## Technology

C#, ASP.NET Core 8, Python 3.12, RabbitMQ, PostgreSQL, Docker Compose, GitHub Actions.
