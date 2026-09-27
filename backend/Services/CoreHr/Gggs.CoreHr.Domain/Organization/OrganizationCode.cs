using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.CoreHr.Domain.Organization;

/// <summary>
/// The shape every organization structure's code shares: trimmed, upper-cased so a lookup never
/// depends on collation, and short enough for a payslip or an org chart label. One place, so a
/// department and a legal entity can never quietly disagree about what a valid code looks like.
/// <para>
/// Public rather than internal: the application layer has to compute the same normalized form to
/// check "does this code already exist" before an aggregate is even constructed, the same way
/// <c>User.NormalizeEmailForLookup</c> is public for the equivalent check on an email address.
/// </para>
/// </summary>
public static class OrganizationCode
{
    public const int MaxLength = 32;

    public static string Normalize(string code)
        => Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), MaxLength);
}
