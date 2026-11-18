using System.Net.Http.Json;
using Gggs.Modules.Identity.Endpoints;
using Gggs.PlatformApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// The platform console's server (Gggs.PlatformApi) in-process, against the HR API's test database:
/// a separate host sharing only the database, as deployed. Reach it through
/// <see cref="PeopleOsApiFactory.PlatformConsole"/>, which owns and disposes it.
/// </summary>
public sealed class PlatformConsoleFactory(PeopleOsApiFactory api) : WebApplicationFactory<PlatformProgram>
{
    public const string SetupKey = "integration-test-setup-key";

    /// <summary>A browser on the console: https, cookies kept, the CSRF header on every request.</summary>
    public HttpClient CreateWebClient()
    {
        HttpClient client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/"),
            HandleCookies = true,
        });

        client.DefaultRequestHeaders.Add(PeopleOsApiFactory.CsrfHeader, "1");
        return client;
    }

    /// <summary>A client with no credentials and no headers.</summary>
    public HttpClient CreateAnonymousClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost/"),
        HandleCookies = false,
    });

    /// <summary>A platform staff caller holding exactly <paramref name="permissions"/> (the test identity).</summary>
    public HttpClient CreatePlatformClient(params string[] permissions)
    {
        HttpClient client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserTypeHeader, "Platform");
        client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));
        client.DefaultRequestHeaders.Add(PeopleOsApiFactory.CsrfHeader, "1");
        return client;
    }

    /// <summary>A console browser signed in through the console's own <c>/bff/login</c>.</summary>
    public async Task<HttpClient> SignInAsync(string email, string password)
    {
        HttpClient client = CreateWebClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/bff/login", new { Email = email, Password = password });
        BffLoginResponse? body = await response.Content.ReadFromJsonAsync<BffLoginResponse>();

        if (!response.IsSuccessStatusCode || body?.Succeeded != true)
        {
            throw new InvalidOperationException($"{email} could not sign in to the console: {response.StatusCode} {body?.ErrorCode}.");
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            Dictionary<string, string?> settings = api.TestConfiguration();
            settings["Gggs:Identity:PlatformBootstrap:SetupKey"] = SetupKey;
            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureServices(PeopleOsApiFactory.AddTestIdentity);
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }
}
