# Architecture

```text
Device / Simulator
       |
       v
ASP.NET Core Ingestion API
       |
       | durable message
       v
RabbitMQ queue: telemetry.ingest
       |
       v
Python Analytics Worker
       |
       +------> PostgreSQL: telemetry_events
       |
       +------> PostgreSQL: hourly_device_summary
```

## Reliability considerations

- Durable queue and persistent messages reduce the chance of losing queued events during broker restarts.
- Worker acknowledgements happen only after database writes complete.
- Event IDs and a unique primary key make raw-event insertion idempotent.
- The raw event insert and aggregate update share a database transaction.
- The worker reconnects after broker interruptions.
- The current retry strategy requeues failures indefinitely. Add bounded retries and a dead-letter queue before production use.
- The API health endpoint is a basic liveness check. Add dependency-aware readiness checks for production.

## Scaling discussion

The API and worker can be scaled independently. Multiple worker replicas can consume from the same queue. Before increasing concurrency, verify database capacity, queue depth, processing latency, duplicate handling, and ordering requirements.

No throughput claim is made until load tests are run and results are documented.
