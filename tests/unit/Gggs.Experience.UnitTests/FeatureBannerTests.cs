using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Engagement.Domain;
using Shouldly;

namespace Gggs.Experience.UnitTests;

/// <summary>The home dashboard banner's slides: when they show, to whom, and where they may link.</summary>
public sealed class FeatureBannerTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    private static FeatureBannerContent Content(
        BannerKind kind = BannerKind.Announcement,
        string? actionLabel = "Open",
        string? actionLink = "/me/leave",
        string? secondaryLabel = null,
        string? secondaryLink = null,
        DateOnly? startsOn = null,
        DateOnly? endsOn = null,
        IReadOnlyList<Guid>? departments = null,
        IReadOnlyList<Guid>? locations = null)
        => new("Title", "Body", kind, actionLabel, actionLink, secondaryLabel, secondaryLink, startsOn ?? Today, endsOn, 10, departments, locations);

    private static FeatureBanner Published(FeatureBannerContent content)
    {
        FeatureBanner banner = FeatureBanner.Draft(content);
        banner.Publish();
        return banner;
    }

    [Fact]
    public void A_draft_never_shows()
    {
        FeatureBanner.Draft(Content()).IsShownTo(Today, null, null).ShouldBeFalse();
    }

    [Fact]
    public void A_slide_shows_from_its_first_day_through_its_last()
    {
        FeatureBanner banner = Published(Content(startsOn: Today, endsOn: Today.AddDays(2)));

        banner.IsShownTo(Today.AddDays(-1), null, null).ShouldBeFalse();
        banner.IsShownTo(Today, null, null).ShouldBeTrue();
        banner.IsShownTo(Today.AddDays(2), null, null).ShouldBeTrue();
        banner.IsShownTo(Today.AddDays(3), null, null).ShouldBeFalse();
    }

    [Fact]
    public void Unpublishing_hides_a_slide()
    {
        FeatureBanner banner = Published(Content());
        banner.Unpublish();
        banner.IsShownTo(Today, null, null).ShouldBeFalse();
    }

    [Fact]
    public void With_no_audience_chosen_it_shows_to_everyone_even_without_an_employee_record()
    {
        Published(Content()).IsShownTo(Today, null, null).ShouldBeTrue();
    }

    [Fact]
    public void A_targeted_slide_shows_to_its_department_or_its_location_only()
    {
        Guid department = Guid.NewGuid();
        Guid location = Guid.NewGuid();
        FeatureBanner banner = Published(Content(departments: [department], locations: [location]));

        banner.IsShownTo(Today, department, Guid.NewGuid()).ShouldBeTrue();
        banner.IsShownTo(Today, Guid.NewGuid(), location).ShouldBeTrue();
        banner.IsShownTo(Today, Guid.NewGuid(), Guid.NewGuid()).ShouldBeFalse();
        banner.IsShownTo(Today, null, null).ShouldBeFalse();
    }

    [Fact]
    public void A_picture_can_be_set_replaced_and_removed_without_touching_the_rest_of_the_slide()
    {
        FeatureBanner banner = Published(Content());
        banner.ImageId.ShouldBeNull();

        Guid first = Guid.NewGuid();
        banner.SetImage(first);
        banner.ImageId.ShouldBe(first);

        banner.Update(Content(actionLabel: "Open it"));
        banner.ImageId.ShouldBe(first);

        banner.RemoveImage();
        banner.ImageId.ShouldBeNull();
        banner.IsPublished.ShouldBeTrue();
    }

    private static FeatureBannerContent FullPicture(string? link = null)
        => new("A team photo", "Ignored", BannerKind.Announcement, "Ignored", link, "Ignored", "/ignored", Today, null, 10, null, null, BannerLayout.FullPicture);

    [Fact]
    public void A_full_picture_keeps_only_its_description_and_where_a_click_goes()
    {
        FeatureBanner banner = FeatureBanner.Draft(FullPicture("/me/rewards"));

        banner.Layout.ShouldBe(BannerLayout.FullPicture);
        banner.Title.ShouldBe("A team photo");
        banner.Body.ShouldBeNull();
        banner.ActionLabel.ShouldBeNull();
        banner.ActionLink.ShouldBe("/me/rewards");
        banner.SecondaryLabel.ShouldBeNull();
        banner.SecondaryLink.ShouldBeNull();
    }

    [Fact]
    public void A_full_picture_needs_no_link_but_any_link_must_be_safe()
    {
        FeatureBanner.Draft(FullPicture()).ActionLink.ShouldBeNull();
        Should.Throw<DomainRuleException>(() => FeatureBanner.Draft(FullPicture("javascript:alert(1)")));
    }

    [Fact]
    public void A_full_picture_cannot_be_published_without_its_picture_or_lose_it_while_published()
    {
        FeatureBanner banner = FeatureBanner.Draft(FullPicture());
        Should.Throw<DomainRuleException>(banner.Publish);

        banner.SetImage(Guid.NewGuid());
        banner.Publish();
        banner.IsShownTo(Today, null, null).ShouldBeTrue();
        Should.Throw<DomainRuleException>(banner.RemoveImage);

        banner.Unpublish();
        banner.RemoveImage();
        banner.ImageId.ShouldBeNull();
    }

    [Fact]
    public void A_published_slide_becomes_a_full_picture_only_once_it_has_a_picture()
    {
        FeatureBanner banner = Published(Content());
        Should.Throw<DomainRuleException>(() => banner.Update(FullPicture()));
        banner.Layout.ShouldBe(BannerLayout.PictureWithText);

        banner.SetImage(Guid.NewGuid());
        banner.Update(FullPicture());
        banner.Layout.ShouldBe(BannerLayout.FullPicture);
    }

    [Fact]
    public void It_cannot_end_before_it_starts()
    {
        Should.Throw<DomainRuleException>(() => FeatureBanner.Draft(Content(endsOn: Today.AddDays(-1))));
    }

    [Fact]
    public void A_deadline_needs_a_last_day()
    {
        Should.Throw<DomainRuleException>(() => FeatureBanner.Draft(Content(BannerKind.Deadline)));
        FeatureBanner.Draft(Content(BannerKind.Deadline, endsOn: Today)).EndsOn.ShouldBe(Today);
    }

    [Theory]
    [InlineData("Open", null)]
    [InlineData(null, "/me/leave")]
    public void A_button_needs_both_its_wording_and_its_link(string? label, string? link)
    {
        Should.Throw<DomainRuleException>(() => FeatureBanner.Draft(Content(actionLabel: label, actionLink: link)));
    }

    [Fact]
    public void A_second_link_needs_the_main_button_first()
    {
        Should.Throw<DomainRuleException>(() => FeatureBanner.Draft(Content(actionLabel: null, actionLink: null, secondaryLabel: "More", secondaryLink: "/calendar")));
    }

    [Theory]
    [InlineData("/me/leave")]
    [InlineData("/calendar?view=month")]
    [InlineData("https://example.com/news")]
    public void Links_may_be_a_page_here_or_an_https_site(string link)
    {
        FeatureBanner.Draft(Content(actionLink: link)).ActionLink.ShouldBe(link);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("http://example.com")]
    [InlineData("//example.com")]
    [InlineData("/\\example.com")]
    [InlineData("me/leave")]
    public void Any_other_link_is_refused(string link)
    {
        Should.Throw<DomainRuleException>(() => FeatureBanner.Draft(Content(actionLink: link)));
    }
}
