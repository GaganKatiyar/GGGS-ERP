using System.Globalization;
using Gggs.BuildingBlocks.Application.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace Gggs.BuildingBlocks.Observability;

/// <summary>
/// Telemetry wiring shared by every service.
/// <para>
/// Everything is emitted through OpenTelemetry and exported over OTLP, which is what keeps the two
/// deployment profiles honest: Azure Monitor consumes it in the cloud, and a customer's Seq,
/// Grafana or other collector consumes the identical stream on-premise. No vendor SDK appears in
/// the application, so there is nothing to swap when the target changes.
/// </para>
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Structured logging with the fields every support question starts from: which tenant, which
    /// user, which correlation id, which service. Personal data never goes into logs; the ids are
    /// enough to find the record through the audit trail.
    /// </summary>
    public static IHostApplicationBuilder AddGggsLogging(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSerilog((services, configuration) => configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            // Invariant culture: log output must not change shape because a host runs under a
            // different locale, or log parsing breaks on the date and number formats.
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

        return builder;
    }

    /// <summary>
    /// Traces and metrics. Tracing spans HTTP, SQL and (once messaging lands) the broker, so one
    /// correlation id follows a request from the browser through a queue into a background job.
    /// </summary>
    public static IHostApplicationBuilder AddGggsTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string? otlpEndpoint = builder.Configuration["Gggs:Telemetry:OtlpEndpoint"];

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString())
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(instrumentation =>
                    {
                        instrumentation.RecordException = true;
                        // Health probes would otherwise dominate the trace volume and the bill.
                        instrumentation.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health")
                            && !context.Request.Path.StartsWithSegments("/alive");
                    })
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation(instrumentation =>
                    {
                        // Statement text can contain personal data, so it stays out of traces.
                        instrumentation.SetDbStatementForText = false;
                        instrumentation.RecordException = true;
                    });

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return builder;
    }

    /// <summary>
    /// Liveness and readiness. Readiness includes the dependencies a request needs, so an instance
    /// that cannot reach its database is taken out of rotation instead of returning errors.
    /// </summary>
    public static IHostApplicationBuilder AddGggsHealthChecks(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        return builder;
    }

    /// <summary>Reads the deployment profile once, at startup, and shares it with the adapters.</summary>
    public static GggsPlatformOptions AddGggsPlatformOptions(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        GggsPlatformOptions platform = new() { ServiceName = serviceName };
        builder.Configuration.GetSection(GggsPlatformOptions.SectionName).Bind(platform);
        platform.ServiceName = serviceName;

        builder.Services.AddSingleton(platform);
        builder.Services.Configure<GggsPlatformOptions>(options =>
        {
            options.DeploymentProfile = platform.DeploymentProfile;
            options.ServiceName = serviceName;
        });

        return platform;
    }
}
