# Interview discussion guide

Use this file to prepare an honest explanation of the project. Distinguish what the demo implements from what would be required in production.

## Why asynchronous messaging?

The API accepts and validates telemetry, then enqueues a durable message. Analytics can proceed independently, so slow processing does not hold the request open.

## Why RabbitMQ?

It provides a broker-managed queue, acknowledgements and producer/consumer separation. The API and worker can be scaled independently.

## Why EF Core?

The API uses EF Core as its read model to query PostgreSQL with LINQ, `AsNoTracking`, ordering, projection and bounded result sets.

## How is duplicate processing handled?

Each event has a unique ID. The worker inserts it using `ON CONFLICT DO NOTHING`; it updates the hourly aggregate only if the raw event was newly inserted. Both statements share one PostgreSQL transaction.

## What would you improve before production?

Publisher confirms, bounded retry and dead-letter handling, authentication, per-device authorization, distributed tracing, metrics, structured correlation IDs, load testing, deployment automation and operational alerts.

## Be precise in interviews

This is a personal portfolio demonstration. Do not describe it as a production platform or claim real high-volume telemetry experience based on this code.
