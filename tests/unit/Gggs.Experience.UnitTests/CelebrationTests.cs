using Gggs.Modules.Engagement.Application;
using Gggs.Modules.Engagement.Domain;
using Shouldly;

namespace Gggs.Experience.UnitTests;

public sealed class CelebrationTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    [Fact]
    public void A_wish_goes_on_the_day_or_up_to_a_week_late_but_never_early()
    {
        CelebrationService.IsSendable(Today, Today).ShouldBeTrue();
        CelebrationService.IsSendable(Today.AddDays(-CelebrationService.LateDays), Today).ShouldBeTrue();
        CelebrationService.IsSendable(Today.AddDays(-CelebrationService.LateDays - 1), Today).ShouldBeFalse();
        CelebrationService.IsSendable(Today.AddDays(1), Today).ShouldBeFalse();
    }

    [Fact]
    public void A_birthday_wish_keeps_no_years_and_a_blank_note_is_no_note()
    {
        CelebrationWish wish = CelebrationWish.Record(Guid.NewGuid(), CelebrationKind.Birthday, Today, 31, "   ", Guid.NewGuid(), " Priya Menon ", DateTime.UtcNow);

        wish.Years.ShouldBe(0);
        wish.Note.ShouldBeNull();
        wish.SentByName.ShouldBe("Priya Menon");
    }

    [Fact]
    public void An_anniversary_wish_keeps_its_years_and_a_trimmed_note()
    {
        CelebrationWish wish = CelebrationWish.Record(Guid.NewGuid(), CelebrationKind.WorkAnniversary, Today, 5, "  Five great years!  ", Guid.NewGuid(), "HR", DateTime.UtcNow);

        wish.Years.ShouldBe(5);
        wish.Note.ShouldBe("Five great years!");
    }
}
