using Gggs.BuildingBlocks.Web.Modules;
using Gggs.Modules.Employee.Application;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Employee.Endpoints;
using Gggs.Modules.Employee.Infrastructure;

namespace Gggs.Modules.Employee;

/// <summary>Core HR (spec §14-15): employee records, employment history, lifecycle and Employee 360.</summary>
public sealed class EmployeeModule : IModule
{
    public string Name => "employee";

    public void AddServices(IServiceCollection services, ModuleContext context)
    {
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
        services.AddScoped<IEmployeePayrollDetails, EmployeePayrollDetails>();
        services.AddScoped<IEmployeeHiring, EmployeeHiring>();
        services.AddScoped<EmployeeScope>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<EmployeeQueries>();
        services.AddScoped<EmployeeImportService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapEmployeeEndpoints();
}
