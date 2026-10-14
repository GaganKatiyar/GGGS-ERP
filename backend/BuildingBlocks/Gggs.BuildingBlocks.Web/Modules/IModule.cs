using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gggs.BuildingBlocks.Web.Modules;

/// <summary>What a module may read while registering itself.</summary>
public sealed record ModuleContext(IConfiguration Configuration, IHostEnvironment Environment);

/// <summary>
/// One business module of the monolith (ADR-12).
/// <para>
/// Both hosts compose the same list of modules, so a module wires itself once and behaves the same
/// in the API and the worker. Nothing outside a module lists its repositories or services.
/// </para>
/// </summary>
public interface IModule
{
    /// <summary>Short, stable name, e.g. <c>identity</c>. Used in logs, health checks and feature flags.</summary>
    string Name { get; }

    /// <summary>
    /// The assembly holding this module's model contribution, which the shared <c>AppDbContext</c>
    /// applies. Usually the module's own assembly.
    /// </summary>
    Assembly ModelAssembly => GetType().Assembly;

    /// <summary>Services both hosts need: repositories, application services, contract implementations.</summary>
    void AddServices(IServiceCollection services, ModuleContext context);

    /// <summary>API-only registrations: authentication handlers, request-path services.</summary>
    void AddApiServices(IServiceCollection services, ModuleContext context)
    {
    }

    /// <summary>Worker-only registrations: event consumers, recurring jobs, startup seeders.</summary>
    void AddBackgroundServices(IServiceCollection services, ModuleContext context)
    {
    }

    /// <summary>API-only: the module's HTTP endpoints.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
