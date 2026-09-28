using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.Security;
using Gggs.CoreHr.Api.Endpoints;
using Gggs.CoreHr.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Gggs.CoreHr.IntegrationTests;

/// <summary>
/// `TenantAuditSaveChangesInterceptor` stages one <see cref="AuditEntryIntegrationEvent"/> per
/// auditable entity change into the service's own outbox, in the same transaction as the change
/// itself. Exercised here through a real endpoint rather than the interceptor directly - an
/// `EntityEntry` from EF's own change tracker is not something a unit test can build without a
/// real `DbContext`, and this codebase does not use EF's InMemory provider (see
/// `SqlServerFixture`'s own reasoning: nothing in-memory stands in for what a real save actually
/// does).
/// </summary>
[Collection(nameof(CoreHrApiCollection))]
public sealed class AuditCaptureTests(CoreHrApiFactory factory)
{
    [Fact]
    public async Task CreatingALegalEntity_StagesAnAuditEntryInTheSameTransaction()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Organization.LegalEntityManage);
        string code = $"AUD-{Guid.NewGuid():N}"[..12];

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/legal-entities",
            new { Code = code, Name = "Audit Test Co", CountryCode = "IN", Currency = "INR" },
            TestContext.Current.CancellationToken);

        LegalEntityResponse legalEntity = (await response.Content.ReadFromJsonAsync<LegalEntityResponse>(
            TestContext.Current.CancellationToken))!;

        AuditEntryIntegrationEvent entry = await FindAuditEntryAsync(legalEntity.Id, "Created");

        entry.Service.ShouldBe("corehr");
        entry.EntityType.ShouldBe(nameof(Gggs.CoreHr.Domain.Organization.LegalEntity));
        entry.OldValuesJson.ShouldBeNull();
        entry.NewValuesJson.ShouldNotBeNull();
        entry.NewValuesJson.ShouldContain(code);
        entry.ChangedFieldsCsv.ShouldBeNull(); // only meaningful for a Modified entry
    }

    [Fact]
    public async Task RenamingALegalEntity_StagesAModifiedEntryNamingOnlyTheChangedField()
    {
        using HttpClient client = factory.CreateClientFor(Permissions.Organization.LegalEntityManage);
        string code = $"AUD-{Guid.NewGuid():N}"[..12];

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/v1/legal-entities",
            new { Code = code, Name = "Before Rename", CountryCode = "IN", Currency = "INR" },
            TestContext.Current.CancellationToken);

        LegalEntityResponse legalEntity = (await createResponse.Content.ReadFromJsonAsync<LegalEntityResponse>(
            TestContext.Current.CancellationToken))!;

        await client.PutAsJsonAsync(
            $"/api/v1/legal-entities/{legalEntity.Id}",
            new
            {
                Name = "After Rename",
                Address = new { Line1 = "", City = "", CountryCode = "IN" },
                CompliancePackCode = (string?)null,
            },
            TestContext.Current.CancellationToken);

        AuditEntryIntegrationEvent entry = await FindAuditEntryAsync(legalEntity.Id, "Modified");

        entry.ChangedFieldsCsv.ShouldNotBeNull();
        entry.ChangedFieldsCsv.ShouldContain("Name");
        entry.OldValuesJson.ShouldNotBeNull();
        entry.OldValuesJson.ShouldContain("Before Rename");
        entry.NewValuesJson.ShouldNotBeNull();
        entry.NewValuesJson.ShouldContain("After Rename");

        // The code never changed, so it has no business being in either diff - a changed-fields
        // list is only useful if it does not also list everything that stayed the same.
        entry.ChangedFieldsCsv.ShouldNotContain("Code");
    }

    /// <summary>Polls briefly: the row lands in the same transaction as the API response, but the
    /// test reads it back through a fresh context and scope right after.</summary>
    private async Task<AuditEntryIntegrationEvent> FindAuditEntryAsync(Guid entityId, string action)
    {
        string messageType = MessageTypeName.For<AuditEntryIntegrationEvent>();

        for (int attempt = 0; attempt < 10; attempt++)
        {
            using CoreHrDbContext db = factory.CreateContext();

            List<string> payloads = await db.Set<OutboxMessage>()
                .Where(message => message.MessageType == messageType)
                .Select(message => message.Payload)
                .ToListAsync(TestContext.Current.CancellationToken);

            foreach (string payload in payloads)
            {
                AuditEntryIntegrationEvent? candidate = JsonSerializer.Deserialize<AuditEntryIntegrationEvent>(
                    payload,
                    OutboxIntegrationEventPublisher.SerializerOptions);

                if (candidate is { } found && found.EntityId == entityId.ToString() && found.Action == action)
                {
                    return found;
                }
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException(
            $"No '{action}' audit entry was staged for entity {entityId} within the time allowed.");
    }
}
