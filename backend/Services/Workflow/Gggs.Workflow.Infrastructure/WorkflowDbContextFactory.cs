using Gggs.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gggs.Workflow.Infrastructure;

/// <summary>Design-time factory for <c>dotnet ef</c>, running in platform scope as migrations do.</summary>
public sealed class WorkflowDbContextFactory : IDesignTimeDbContextFactory<WorkflowDbContext>
{
    public WorkflowDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("GGGS_WORKFLOW_DB")
            ?? "Server=localhost;Database=gggs_workflow;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.BeginPlatformScope("ef-design-time");

        return new WorkflowDbContext(options, tenantContext);
    }
}
