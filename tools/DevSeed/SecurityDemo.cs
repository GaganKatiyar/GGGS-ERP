using Gggs.Modules.Identity.Application.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.DevSeed;

/// <summary>
/// Development-only security data (M12): an example single sign-on provider, switched off, so the
/// administration page shows what a configured provider looks like. Turning it on needs a real
/// Entra ID app registration - the authority and client ID here are placeholders.
/// </summary>
internal static class SecurityDemo
{
    public static async Task SeedAsync(IServiceProvider scope)
    {
        await ExperienceDemo.TopUpRolesAsync(scope);
        SsoService sso = scope.GetRequiredService<SsoService>();
        if ((await sso.ListAsync(CancellationToken.None)).Count > 0)
        {
            Console.WriteLine("Single sign-on providers already present.");
            return;
        }

        await sso.CreateAsync(new SsoProviderRequest(
            Key: "entra",
            DisplayName: "Microsoft",
            Authority: "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0",
            ClientId: "00000000-0000-0000-0000-000000000000",
            ClientSecret: null,
            Scopes: "openid profile email",
            AllowedDomains: "acme.example",
            Enabled: false), CancellationToken.None);
        Console.WriteLine("Security demo: an example (disabled) Entra ID provider added.");
    }
}
