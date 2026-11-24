# Architecture Decision Records

## Why asynchronous ingestion?

Device telemetry should not wait for analytics processing to complete. The API validates and enqueues an event, then returns `202 Accepted`. A worker processes events independently.

## Why RabbitMQ?

It provides a straightforward durable queue for demonstrating producer/consumer decoupling, acknowledgements, and independent scaling.

## Why PostgreSQL?

It is easy to run locally and supports relational constraints, timestamps, indexes, and aggregation queries.

## Why a separate Python worker?

It demonstrates a polyglot service boundary and lets the ingestion API stay focused on accepting and validating telemetry. The Python component is a portfolio demonstration, not a claim of extensive professional Python experience.

## Known limitations

- No authentication or authorization yet.
- No bounded retry policy or dead-letter queue yet.
- The API currently establishes a broker connection at startup; production code should include a resilient connection lifecycle.
- No cloud deployment or load-test results are included.
