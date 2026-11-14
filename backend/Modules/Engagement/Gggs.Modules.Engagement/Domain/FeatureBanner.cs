using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Engagement.Domain;

/// <summary>What a banner slide is about, which sets its label and its picture.</summary>
public enum BannerKind
{
    Announcement = 1,
    Payroll = 2,
    Holiday = 3,

    /// <summary>Something to finish by the slide's last day, which the slide shows as its due date.</summary>
    Deadline = 4,

    WhatsNew = 5,
    Recognition = 6,
}

/// <summary>How a slide uses its space.</summary>
public enum BannerLayout
{
    /// <summary>A headline, a sentence and a button beside a picture or drawing.</summary>
    PictureWithText = 1,

    /// <summary>Only a picture, edge to edge. The slide's title is then the description of the picture, which is not shown on the slide.</summary>
    FullPicture = 2,
}

/// <summary>
/// One slide of the home dashboard's banner: what HR wants people to see, for how long and for whom.
/// It shows from its first day to its last once published; with no departments or locations chosen
/// it shows to everyone.
/// </summary>
public sealed class FeatureBanner : TenantAggregateRoot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private FeatureBanner()
    {
        Title = null!;
        AudienceDepartmentIdsJson = "[]";
        AudienceLocationIdsJson = "[]";
    }

    public string Title { get; private set; }

    public string? Body { get; private set; }

    public BannerKind Kind { get; private set; }

    public BannerLayout Layout { get; private set; }

    public string? ActionLabel { get; private set; }

    public string? ActionLink { get; private set; }

    public string? SecondaryLabel { get; private set; }

    public string? SecondaryLink { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly? EndsOn { get; private set; }

    /// <summary>Lower numbers come first.</summary>
    public int Position { get; private set; }

    public bool IsPublished { get; private set; }

    /// <summary>The uploaded picture, if any; without one the slide uses the drawn picture for its kind. A new picture gets a new id, so this is also its cache version.</summary>
    public Guid? ImageId { get; private set; }

    public string AudienceDepartmentIdsJson { get; private set; }

    public string AudienceLocationIdsJson { get; private set; }

    public IReadOnlyList<Guid> AudienceDepartmentIds => JsonSerializer.Deserialize<List<Guid>>(AudienceDepartmentIdsJson, Json) ?? [];

    public IReadOnlyList<Guid> AudienceLocationIds => JsonSerializer.Deserialize<List<Guid>>(AudienceLocationIdsJson, Json) ?? [];

    public static FeatureBanner Draft(FeatureBannerContent content)
    {
        var banner = new FeatureBanner();
        banner.Update(content);
        return banner;
    }

    public void Update(FeatureBannerContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.EndsOn is { } last && last < content.StartsOn)
        {
            throw new DomainRuleException("BANNER_WINDOW", "A slide must end on or after the day it starts.");
        }

        if (content.Kind == BannerKind.Deadline && content.EndsOn is null)
        {
            throw new DomainRuleException("BANNER_DEADLINE", "A deadline slide needs a last day, because that is the date it shows.");
        }

        if (!Enum.IsDefined(content.Kind))
        {
            throw new DomainRuleException("BANNER_KIND", "Choose what the slide is about.");
        }

        if (!Enum.IsDefined(content.Layout))
        {
            throw new DomainRuleException("BANNER_LAYOUT", "Choose the slide type.");
        }

        if (content.Layout == BannerLayout.FullPicture && IsPublished && ImageId is null)
        {
            throw new DomainRuleException("BANNER_NO_PICTURE", "Add a picture before this published slide becomes a full picture.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(content.Title).Trim(), 120);
        Kind = content.Kind;
        Layout = content.Layout;
        if (content.Layout == BannerLayout.FullPicture)
        {
            // Nothing but the picture shows, so nothing else is kept; a click on it may still lead somewhere.
            Body = null;
            ActionLabel = null;
            ActionLink = string.IsNullOrWhiteSpace(content.ActionLink) ? null : SafeLink(content.ActionLink.Trim());
            SecondaryLabel = null;
            SecondaryLink = null;
        }
        else
        {
            Body = string.IsNullOrWhiteSpace(content.Body) ? null : Guard.MaxLength(content.Body.Trim(), 300);
            (ActionLabel, ActionLink) = Action(content.ActionLabel, content.ActionLink);
            (SecondaryLabel, SecondaryLink) = Action(content.SecondaryLabel, content.SecondaryLink);
            if (SecondaryLabel is not null && ActionLabel is null)
            {
                throw new DomainRuleException("BANNER_ACTION", "Add the main button before a second link.");
            }
        }

        StartsOn = content.StartsOn;
        EndsOn = content.EndsOn;
        Position = content.Position;
        AudienceDepartmentIdsJson = JsonSerializer.Serialize((content.AudienceDepartmentIds ?? []).Distinct().ToList(), Json);
        AudienceLocationIdsJson = JsonSerializer.Serialize((content.AudienceLocationIds ?? []).Distinct().ToList(), Json);
    }

    public void SetImage(Guid imageId) => ImageId = imageId;

    public void RemoveImage()
    {
        if (Layout == BannerLayout.FullPicture && IsPublished)
        {
            throw new DomainRuleException("BANNER_NO_PICTURE", "A published full-picture slide needs its picture. Unpublish it first, or choose another picture.");
        }

        ImageId = null;
    }

    public void Publish()
    {
        if (Layout == BannerLayout.FullPicture && ImageId is null)
        {
            throw new DomainRuleException("BANNER_NO_PICTURE", "A full-picture slide needs a picture before it can be published.");
        }

        IsPublished = true;
    }

    public void Unpublish() => IsPublished = false;

    /// <summary>Whether this slide shows today to someone in this department and location.</summary>
    public bool IsShownTo(DateOnly today, Guid? departmentId, Guid? locationId)
    {
        if (!IsPublished || today < StartsOn || (EndsOn is { } last && today > last))
        {
            return false;
        }

        IReadOnlyList<Guid> departments = AudienceDepartmentIds;
        IReadOnlyList<Guid> locations = AudienceLocationIds;
        return (departments.Count == 0 && locations.Count == 0)
            || (departmentId is { } department && departments.Contains(department))
            || (locationId is { } location && locations.Contains(location));
    }

    private static (string? Label, string? Link) Action(string? label, string? link)
    {
        bool hasLabel = !string.IsNullOrWhiteSpace(label);
        bool hasLink = !string.IsNullOrWhiteSpace(link);
        if (hasLabel != hasLink)
        {
            throw new DomainRuleException("BANNER_ACTION", "A button needs both its wording and where it goes.");
        }

        return hasLabel ? (Guard.MaxLength(label!.Trim(), 40), SafeLink(link!.Trim())) : (null, null);
    }

    /// <summary>A page in this app, or an https site. Nothing else may reach an employee's click.</summary>
    private static string SafeLink(string link)
    {
        bool isPage = link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal) && !link.Contains('\\', StringComparison.Ordinal);
        bool isSite = Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;
        if (!isPage && !isSite)
        {
            throw new DomainRuleException("BANNER_LINK", "A link must be a page in this app, such as /me/leave, or start with https://.");
        }

        return Guard.MaxLength(link, 300);
    }
}

/// <summary>Everything about a slide that its author writes.</summary>
public sealed record FeatureBannerContent(
    string Title,
    string? Body,
    BannerKind Kind,
    string? ActionLabel,
    string? ActionLink,
    string? SecondaryLabel,
    string? SecondaryLink,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    int Position,
    IReadOnlyList<Guid>? AudienceDepartmentIds,
    IReadOnlyList<Guid>? AudienceLocationIds,
    BannerLayout Layout = BannerLayout.PictureWithText);
