using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Engagement.Domain;

/// <summary>What is being celebrated.</summary>
public enum CelebrationKind
{
    Birthday = 1,
    WorkAnniversary = 2,
}

/// <summary>
/// HR wished an employee on their birthday or work anniversary: the employee and their manager were
/// sent the greeting. One per person, kind and day, so nobody is wished twice for the same occasion.
/// </summary>
public sealed class CelebrationWish : TenantAggregateRoot
{
    public const int NoteMaxLength = 500;

    private CelebrationWish()
    {
        SentByName = null!;
    }

    public Guid EmployeeId { get; private set; }

    public CelebrationKind Kind { get; private set; }

    /// <summary>The birthday or anniversary itself, which may be a few days before it was sent.</summary>
    public DateOnly OccurredOn { get; private set; }

    /// <summary>Years of service for an anniversary; 0 for a birthday.</summary>
    public int Years { get; private set; }

    /// <summary>The personal note HR added to the standard wording, if any.</summary>
    public string? Note { get; private set; }

    public Guid SentByUserId { get; private set; }

    /// <summary>Who sent it, as they were named then.</summary>
    public string SentByName { get; private set; }

    public DateTime SentAt { get; private set; }

    public static CelebrationWish Record(Guid employeeId, CelebrationKind kind, DateOnly occurredOn, int years, string? note, Guid sentByUserId, string sentByName, DateTime sentAt)
        => new()
        {
            EmployeeId = employeeId,
            Kind = kind,
            OccurredOn = occurredOn,
            Years = kind == CelebrationKind.WorkAnniversary ? years : 0,
            Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), NoteMaxLength),
            SentByUserId = sentByUserId,
            SentByName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(sentByName).Trim(), 200),
            SentAt = sentAt,
        };
}
