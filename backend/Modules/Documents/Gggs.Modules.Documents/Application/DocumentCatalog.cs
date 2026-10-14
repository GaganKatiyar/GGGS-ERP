using Gggs.BuildingBlocks.Messaging;
using Gggs.Data;
using Gggs.Modules.Documents.Domain;
using Gggs.Modules.Documents.Infrastructure;
using Gggs.Modules.Platform.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Documents.Application;

internal sealed record DocumentTypeSeed(string Code, string Name, DocumentCategory Category, bool EmployeeVisible, bool RequiresExpiry);

internal sealed record DocumentTemplateSeed(string Code, string Name, string Description, string TypeCode, string Body);

/// <summary>The document types and letters a new tenant starts with.</summary>
internal static class DocumentCatalog
{
    public static IReadOnlyList<DocumentTypeSeed> Types { get; } =
    [
        new("PAN", "PAN card", DocumentCategory.Identity, EmployeeVisible: true, RequiresExpiry: false),
        new("AADHAAR", "Aadhaar card", DocumentCategory.Identity, EmployeeVisible: true, RequiresExpiry: false),
        new("PASSPORT", "Passport", DocumentCategory.Identity, EmployeeVisible: true, RequiresExpiry: true),
        new("EDUCATION", "Education certificate", DocumentCategory.Education, EmployeeVisible: true, RequiresExpiry: false),
        new("OFFER_LETTER", "Offer letter", DocumentCategory.Letter, EmployeeVisible: true, RequiresExpiry: false),
        new("APPOINTMENT", "Appointment letter", DocumentCategory.Letter, EmployeeVisible: true, RequiresExpiry: false),
        new("EMPLOYMENT_CERT", "Employment certificate", DocumentCategory.Letter, EmployeeVisible: true, RequiresExpiry: false),
        new("EXPERIENCE", "Experience letter", DocumentCategory.Letter, EmployeeVisible: true, RequiresExpiry: false),
        new("RELIEVING", "Relieving letter", DocumentCategory.Letter, EmployeeVisible: true, RequiresExpiry: false),
        new("CONTRACT", "Contract", DocumentCategory.Contract, EmployeeVisible: true, RequiresExpiry: false),
        new("POLICY", "Company policy", DocumentCategory.Policy, EmployeeVisible: false, RequiresExpiry: false),
        new("OTHER", "Other", DocumentCategory.Other, EmployeeVisible: false, RequiresExpiry: false),
    ];

    public static IReadOnlyList<DocumentTemplateSeed> Templates { get; } =
    [
        new(
            "EMPLOYMENT_CERTIFICATE",
            "Employment certificate",
            "Confirms that someone works here, in which role and since when.",
            "EMPLOYMENT_CERT",
            """
            <h1>Employment certificate</h1>
            <p>{{today}}</p>
            <p>This is to certify that <strong>{{employeeName}}</strong> (employee code {{employeeCode}}) has been employed with us
            since {{joiningDate}} and currently works as {{designation}} in {{department}}.</p>
            <p>This certificate is issued at the employee's request.</p>
            <p>For the company,<br />Human Resources</p>
            """),
    ];
}

/// <summary>Gives the current tenant every catalog type and template it lacks. Never touches existing ones.</summary>
public sealed class DocumentCatalogSeeder(AppDbContext dbContext, ILogger<DocumentCatalogSeeder> logger)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, DocumentType> types = await dbContext.DocumentTypes
            .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .ToDictionaryAsync(type => type.Code, cancellationToken);

        int added = 0;
        foreach (DocumentTypeSeed seed in DocumentCatalog.Types.Where(seed => !types.ContainsKey(seed.Code)))
        {
            var type = DocumentType.Create(seed.Code, seed.Name, seed.Category, seed.EmployeeVisible, seed.RequiresExpiry, isSystem: true);
            dbContext.DocumentTypes.Add(type);
            types[type.Code] = type;
            added++;
        }

        HashSet<string> templates = [.. await dbContext.DocumentTemplates
            .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .Select(template => template.Code)
            .ToListAsync(cancellationToken)];

        foreach (DocumentTemplateSeed seed in DocumentCatalog.Templates.Where(seed => !templates.Contains(seed.Code) && types.ContainsKey(seed.TypeCode)))
        {
            dbContext.DocumentTemplates.Add(DocumentTemplate.Create(seed.Code, seed.Name, seed.Description, types[seed.TypeCode].Id, seed.Body));
            added++;
        }

        if (added > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Document catalog seeded: {Added} added.", added);
        return added;
    }
}

internal sealed class SeedDocumentCatalogOnTenantProvisioned(DocumentCatalogSeeder seeder)
    : IIntegrationEventConsumer<TenantProvisionedIntegrationEvent>
{
    public Task HandleAsync(TenantProvisionedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => seeder.SeedCurrentTenantAsync(cancellationToken);
}

/// <summary>
/// The e-sign port (spec §107). A provider is told a signature is wanted and may hand back its own
/// reference; completion comes back through PeopleOS (the built-in provider) or a provider webhook.
/// </summary>
internal interface ISignatureProvider
{
    string Name { get; }

    Task<string?> RequestAsync(DocumentSignature request, CancellationToken cancellationToken);
}

/// <summary>
/// Built-in click-to-sign: the signer opens the document in PeopleOS and signs there; the time and
/// address are recorded as evidence. Suitable for acknowledgements and internal letters; contracts
/// that need a qualified electronic signature plug an external provider in behind the same port.
/// </summary>
internal sealed class BuiltInSignatureProvider : ISignatureProvider
{
    public string Name => "built-in";

    public Task<string?> RequestAsync(DocumentSignature request, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
