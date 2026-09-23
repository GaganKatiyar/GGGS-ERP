# ADR-06: Own outbox and transport port, on the native broker SDKs

- **Status:** Accepted (2026-09-23)
- **Context:** Architecture §5.3, §5.4
- **Supersedes:** the open question "MassTransit or Wolverine" from the plan

## Decision

Integration events are published through our own thin layer:

- Domain code depends only on `IIntegrationEventPublisher`, which **writes to the outbox table in
  the same SQL transaction as the business change**.
- A relay drains the outbox and hands messages to an `IMessageTransport` port.
- Two adapters implement it: **Azure Service Bus** (Cloud profile) and **RabbitMQ** (OnPremise),
  both on the vendors' own free SDKs.
- Consumers deduplicate through an inbox table keyed by message id and consumer name.

No general-purpose messaging framework (MassTransit, Wolverine, NServiceBus) is taken as a
dependency.

## Why

**The licensing question would not go away.** MassTransit v9 is commercially licensed, and v8's
free support window closes while this product is still being built. Wolverine's licensing sits with
a single vendor too. For a platform that ships **on-premise to customers** (ADR-11), a per-seat or
per-company runtime licence is not just a line item: it is a term we would have to pass through to
every self-hosted install.

**What we actually need is small.** Publish an event, deliver it at least once, do not lose it if
the process dies mid-transaction, and do not apply it twice. That is an outbox table, a relay, and
an inbox table. The frameworks earn their keep on saga orchestration and routing topologies - and
the two places we need orchestration, approvals and tenant provisioning, are already a custom
workflow engine (ADR-07) and a small provisioning process.

**The transactional part is ours either way.** The one piece that must be exactly right is that the
domain change and the outgoing message commit together. That is a single `SaveChanges` against our
own table; a framework does not make it more correct, and writing it ourselves means the behaviour
is visible in our code rather than in a configuration convention.

**The adapters were needed regardless.** ADR-11 already requires Service Bus and RabbitMQ to be
interchangeable. A framework would still need per-transport configuration, so the port exists in
both designs.

## What we give up

Saga state machines, scheduled/delayed delivery, request-response, built-in retry and dead-letter
tooling, message scheduling, and a large community answering questions. Redelivery is ours to
implement (attempt counters plus backoff on the outbox row, and the broker's own dead-lettering).
If orchestration grows beyond what the workflow engine covers, revisit.

**The escape hatch is deliberate.** Domain code names `IIntegrationEventPublisher` and nothing else.
Adopting MassTransit later means writing an adapter behind that interface and deleting the relay -
not touching a single handler.

## Consequences

- Every integration event carries a tenant id in its envelope. Consumers that receive one without a
  tenant reject it rather than guessing, the same rule background jobs follow.
- At-least-once delivery is the contract, so consumers are idempotent by design: the inbox rejects
  duplicates, and handlers upsert by natural key instead of blind-inserting.
- The outbox table lives in each service's own database, so "saved implies eventually published"
  holds without a distributed transaction.
