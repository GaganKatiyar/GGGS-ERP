namespace Gggs.CoreHr.Domain.Organization;

/// <summary>
/// A postal address, embedded wherever one is needed rather than looked up. An address is not an
/// entity in its own right here: nothing else references one by id, and nothing needs its history
/// once it changes.
/// </summary>
public sealed record Address(
    string Line1,
    string? Line2,
    string City,
    string? StateCode,
    string? PostalCode,
    string CountryCode)
{
    /// <summary>An empty address, for a branch or legal entity created before anyone has filled one in.</summary>
    public static Address Empty { get; } = new(string.Empty, null, string.Empty, null, null, string.Empty);

    public bool IsEmpty => Line1.Length == 0 && City.Length == 0 && CountryCode.Length == 0;
}
