using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Identity.Application.Authentication;

namespace Gggs.Modules.Identity.Endpoints;

public sealed record MfaCodeRequest(string Code);

/// <summary>The signed-in user's own multi-factor sign-in: set up an authenticator, confirm it, replace recovery codes, switch off.</summary>
public static class MfaEndpoints
{
    public static IEndpointRouteBuilder MapMfaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/me/security/mfa").WithTags("Security");

        group.MapGet("/", async (MfaService mfa, CancellationToken ct) => TypedResults.Ok(await mfa.StatusAsync(ct)))
            .WithName("MyMfaStatus").RequirePermission(Permissions.Self.ProfileView);
        group.MapPost("/setup", async (MfaService mfa, CancellationToken ct) => TypedResults.Ok(await mfa.SetupAsync(ct)))
            .WithName("SetUpMfa").RequirePermission(Permissions.Self.ProfileView);
        group.MapPost("/confirm", async (MfaCodeRequest body, MfaService mfa, CancellationToken ct) => TypedResults.Ok(await mfa.ConfirmAsync(body.Code, ct)))
            .WithName("ConfirmMfa").RequirePermission(Permissions.Self.ProfileView);
        group.MapPost("/recovery-codes", async (MfaCodeRequest body, MfaService mfa, CancellationToken ct) => TypedResults.Ok(await mfa.RegenerateRecoveryCodesAsync(body.Code, ct)))
            .WithName("RegenerateRecoveryCodes").RequirePermission(Permissions.Self.ProfileView);
        group.MapPost("/disable", async (MfaCodeRequest body, MfaService mfa, CancellationToken ct) =>
            {
                await mfa.DisableAsync(body.Code, ct);
                return TypedResults.NoContent();
            })
            .WithName("DisableMfa").RequirePermission(Permissions.Self.ProfileView);
        return endpoints;
    }
}
