# ADR-11: One codebase, two deployment profiles

- **Status:** Accepted (2026-09-23)
- **Context:** Architecture §20.1

## Decision

The platform supports two deployment profiles from one codebase and one set of container images:

- **Cloud** — our Azure SaaS: Container Apps, Azure SQL, Service Bus, Blob Storage, Key Vault,
  Azure Monitor.
- **OnPremise** — a customer's own servers: IIS or Docker, their SQL Server, RabbitMQ, a file share
  or MinIO, an encrypted local secret file, and any OTLP collector.

`Gggs:Platform:DeploymentProfile` selects the adapters at startup. Every infrastructure dependency
is a port with an implementation per profile, and **no application or domain code may reference a
cloud vendor SDK**.

## Why

Selling self-hosted installs was a product requirement, and retrofitting it is the expensive path:
by the time a `BlobServiceClient` appears in a document handler and a Key Vault call in a
provisioning saga, the on-premise build is a fork rather than a configuration.

The rule is enforced rather than documented. `DeploymentProfileRuleTests` walks every compiled
assembly and fails the build if anything outside the named adapter projects references `Azure.*`,
`Microsoft.Azure.*` or an AWS SDK, and asserts that each port has both implementations.

Local development runs the **OnPremise** profile. That is deliberate: the adapters customers depend
on are exercised every day, and no developer needs Azure credentials to start a service.

Telemetry is emitted as plain OpenTelemetry over OTLP rather than through a vendor SDK, so the same
traces and metrics land in Azure Monitor or in a customer's Seq or Grafana without a code change.

## Consequences

- Roughly 10-15% extra effort per infrastructure concern, and a permanent second CI path. Accepted
  as the cost of the requirement; tracked as risk 16.
- Some capabilities are cloud-only and are documented as such: multi-region data residency, elastic
  pools, auto-failover groups, and managed malware scanning (on-premise uses a ClamAV/ICAP adapter).
- The profile default is **Cloud**, so a misconfigured on-premise install fails loudly on its first
  Key Vault call instead of quietly writing customer data somewhere unexpected.
- `SecretNames` and `TenantStorageKey` define naming once, so both profiles agree on where a
  tenant's secrets and files live and a migration between them is mechanical.
