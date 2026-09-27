namespace Gggs.BuildingBlocks.Security;

/// <summary>Authentication scheme names shared across hosts, so a typo cannot silently select the wrong one.</summary>
public static class GggsAuthenticationSchemes
{
    /// <summary>
    /// Accepts whichever credential the request actually carries: a bearer token if there is an
    /// <c>Authorization</c> header, the session cookie otherwise.
    /// <para>
    /// Only the Identity service needs this. It is both a token issuer and a browser-facing sign-in
    /// endpoint, so it has to set a cookie before any token exists and accept tokens everywhere else.
    /// Every other service takes bearer tokens only, because nothing navigates to them.
    /// </para>
    /// </summary>
    public const string Any = "Gggs";
}
