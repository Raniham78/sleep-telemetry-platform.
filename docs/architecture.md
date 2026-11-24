# Architecture and design notes

## Request flow

1. A client sends telemetry to `POST /api/v1/telemetry`.
2. ASP.NET Core validates the payload and assigns a unique event ID.
3. The API publishes a persistent message to the durable `telemetry.ingest` RabbitMQ queue and returns `202 Accepted`.
4. The Python worker consumes the event.
5. The worker opens a PostgreSQL transaction, inserts the raw event and updates the relevant hourly aggregate.
6. The worker acknowledges the message only after the transaction commits.
7. The API's GET endpoint uses EF Core to query persisted raw telemetry.

## Consistency and idempotency

The ingestion endpoint is asynchronous. An accepted event is not guaranteed to be immediately queryable. The raw event table has a primary key on `event_id`. The worker updates the hourly aggregate only when the raw insert succeeds, and both operations run in the same transaction. This prevents duplicate deliveries from incrementing the aggregate more than once.

## Operational caveats

- The demo uses local development credentials in Docker Compose. Do not reuse them outside a local environment.
- A failed message is currently requeued immediately. A production system should use bounded retries, exponential backoff and a dead-letter queue to avoid poison-message loops.
- RabbitMQ publisher confirms are not yet enabled. A production API should enable confirms and handle channel/connection failures before returning success.
- The readiness endpoint checks database connectivity; broker connectivity should be added to the full readiness policy.
- No scale or throughput claims are made. They require repeatable load tests and operational evidence.
