# Infrastructure

PeopleOS deploys two ways from the same two images (ADR-11, ADR-12): our Azure SaaS, and a
customer's own servers. Either way it is an **API** container and a **worker** container against
**one** SQL Server database. `Gggs:Platform:DeploymentProfile` picks the adapters at startup.

| | Cloud | OnPremise |
|---|---|---|
| Defined by | `bicep/` | `onprem/` |
| Compute | Container Apps | Docker Compose, or IIS |
| Database | Azure SQL elastic pool | Customer's SQL Server 2019+ |
| Integration events | In-process, delivered by the worker from the outbox | Same |
| External messaging (future integrations only) | Service Bus topic | RabbitMQ topic exchange |
| Documents | Blob, container per tenant | File share, folder per tenant |
| Secrets | Key Vault | Encrypted local file (Data Protection) |
| Telemetry | Azure Monitor (OTLP) | Seq, Grafana or any OTLP collector |

## Cloud

> **Not yet validated.** These templates have not been compiled or deployed — no Azure CLI was
> available on the machine where they were written. Before trusting them, run the lint and
> what-if steps below, and expect to correct API versions and property names.

```bash
az bicep build --file infra/bicep/main.bicep            # compile
az deployment group what-if \
  --resource-group gggs-dev-rg \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/environments/dev.bicepparam  # preview
```

Production differs from the lower environments in ways that cost money, and only those ways: zone
redundancy, Business Critical SQL, Premium Service Bus, geo-redundant storage, and longer
retention. A QA bill should not look like a production one.

Tenants are rows, not resources. Onboarding a customer deploys nothing; a tenant promoted to the
dedicated tier gets its own copy of the `peopleos` database inside the existing pool.

The Service Bus namespace is still provisioned, but nothing in the application uses it yet: module
events are delivered in-process (ADR-12). It stays for the external integrations planned in M11 and
can be removed from `main.bicep` until then if the cost matters.

### Not in the templates yet

Private endpoints and the virtual network are referenced by `publicNetworkAccess: 'Disabled'` but
not yet defined, so a first deployment will leave services unable to reach SQL, Key Vault and
Storage. Front Door and the WAF are also missing. Both are needed before a real environment.

## On-premise

`onprem/` is the release bundle handed to a customer:

```
compose.onprem.yml   services, volumes and the OnPremise profile
install.ps1          idempotent install and upgrade
images/*.tar         application images, for servers with no registry access
migrations/*.exe     self-contained EF migration bundles - no SDK needed on the server
bundle.json          version manifest
```

```powershell
.\install.ps1 -SqlConnectionString "Server=sql01;Database=gggs;Integrated Security=True;Encrypt=True"
```

The script backs up the database, loads the images, applies migrations **before** starting the new
containers (expand/contract means new schema is readable by old code, not the reverse), then waits
for `/health/ready` before reporting success. Running it again is how an upgrade happens.

### What the customer must back up

- **`gggs.env`** — generated credentials.
- **The `gggs-dataprotection` volume** — the keys protecting the local secret store. Lose these and
  every stored integration credential becomes unreadable, even though the file survives.
- **`gggs-storage`** — employee documents. These exist nowhere else.
- **The SQL databases** — on the customer's own backup schedule.

### Cloud-only capabilities

Multi-region data residency, elastic pools, auto-failover groups and managed malware scanning have
no on-premise equivalent. Document scanning on-premise needs a ClamAV or ICAP adapter, which is not
built yet.
