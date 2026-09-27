using Gggs.BuildingBlocks.Application.Hosting;
using Gggs.BuildingBlocks.Observability;
using Gggs.BuildingBlocks.Web;
using Gggs.Gateway.Bff.Endpoints;
using Gggs.Gateway.Bff.Infrastructure;
using Gggs.Gateway.Bff.Session;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Transforms;

/*
 * The browser's only origin.
 *
 * Everything the SPA talks to is same-origin with this process: the app is served from here, the
 * session cookie belongs to here, and /api/* is proxied from here to the services. That is what
 * makes the whole cookie design work - there is no cross-origin request to protect, no CORS policy
 * to get wrong, and no token in the browser to steal.
 *
 * What it deliberately does not do is make decisions. It holds no domain logic and no authorization
 * rules: it attaches a credential and forwards. Every permission is checked by the service that owns
 * the data, because a gateway that authorizes is a gateway that can be bypassed by anything which
 * reaches a service another way.
 */

const string ServiceName = "bff";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

GggsPlatformOptions platform = builder.AddGggsPlatformOptions(ServiceName);

builder.AddGggsLogging(ServiceName);
builder.AddGggsTelemetry(ServiceName);
builder.AddGggsHealthChecks();

builder.Services.AddOptions<BffSessionOptions>()
    .Bind(builder.Configuration.GetSection(BffSessionOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddOptions<SecurityHeaderOptions>()
    .Bind(builder.Configuration.GetSection(SecurityHeaderOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddOptions<IdentityClientOptions>()
    .Bind(builder.Configuration.GetSection(IdentityClientOptions.SectionName))
    .Validate(
        options => builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.ClientSecret),
        "Gggs:Bff:Identity:ClientSecret is required outside development.")
    .ValidateOnStart();

string? redis = builder.Configuration.GetConnectionString("Redis");

if (string.IsNullOrWhiteSpace(redis))
{
    // Sessions then live in this process only: they are lost on restart and invisible to other
    // replicas. Fine for a developer running one instance, wrong anywhere with more than one, which
    // is why the startup log says so out loud rather than leaving it to be discovered.
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redis;
        options.InstanceName = "gggs:bff:";
    });
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BffSessionStore>();
builder.Services.AddSingleton<SessionTokenRefresher>();

// Resilience on the Identity call: a sign-in that hangs holds a browser request open, and the
// default pipeline turns that into a fast failure the SPA can report.
builder.Services.AddHttpClient<IdentityClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Gggs:Services:Identity"] ?? "http://identity");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context =>
    {
        context.AddRequestTransform(async transform =>
        {
            // The credential is attached here and only here. The browser never sent one and never
            // receives one; the proxy supplies it from the session record on every hop, by which point
            // the session middleware has already made sure it has not expired.
            Gggs.Gateway.Bff.Session.BffSession? session = transform.HttpContext.GetSession();

            if (session is not null)
            {
                transform.ProxyRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
            }

            await ValueTask.CompletedTask;
        });

        context.AddResponseTransform(transform =>
        {
            // Upstream cookies are dropped rather than relayed. The services issue their own session
            // cookies, and passing one through would put a second credential in the browser - one
            // the gateway does not control and cannot revoke.
            transform.HttpContext.Response.Headers.Remove("Set-Cookie");
            return ValueTask.CompletedTask;
        });
    });

builder.Services.AddGggsJson();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

WebApplication app = builder.Build();

if (string.IsNullOrWhiteSpace(redis))
{
    app.Logger.LogWarning(
        "No Redis connection is configured, so sessions are held in memory: they will not survive a " +
        "restart and will not be shared between replicas. Configure ConnectionStrings:Redis before " +
        "running more than one instance.");
}

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

// First, so the headers are on every response including errors and static files.
app.UseMiddleware<SecurityHeadersMiddleware>();

// Order matters. The guard strips headers and enforces the CSRF header before anything reads the
// request; the session is then resolved so both the endpoints and the proxy can see it.
app.UseMiddleware<BrowserRequestGuard>();
app.UseMiddleware<SessionResolutionMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapBffAuthEndpoints();

// Anything not matched above and not /bff is proxied. Routes come from configuration so adding a
// service is a deployment change rather than a code change.
app.MapReverseProxy();

// The SPA, served from this same origin - which is what makes the cookie work without CORS and
// without a token in the browser. Absent during development, when the Angular dev server serves it
// and proxies here instead.
if (Directory.Exists(app.Environment.WebRootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();

    // Deep links are client-side routes: /employees/123 must return the application, not a 404.
    // Scoped to exclude /api and /bff, so a mistyped API path still fails as an API path rather
    // than quietly returning HTML that the caller cannot parse.
    app.MapFallbackToFile("index.html")
        .Add(builder => ((RouteEndpointBuilder)builder).Order = int.MaxValue);
}

app.Logger.LogInformation("Gateway starting in the {Profile} profile.", platform.DeploymentProfile);

await app.RunAsync();
