using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Domain.Organization;
using Shouldly;

namespace Gggs.CoreHr.UnitTests;

public sealed class GradeTests
{
    [Fact]
    public void ARankOfZeroOrBelow_IsRejected()
    {
        Should.Throw<DomainRuleException>(() => Grade.Create("L1", "Associate", 0))
            .ErrorCode.ShouldBe("GRADE_RANK_INVALID");
    }

    [Fact]
    public void ASalaryBandWhoseMinimumExceedsItsMaximum_IsRejected()
    {
        Grade grade = Grade.Create("L1", "Associate", 1);

        Should.Throw<DomainRuleException>(() => grade.SetSalaryBand(900_000m, 600_000m, "INR"))
            .ErrorCode.ShouldBe("SALARY_BAND_INVALID");
    }

    [Fact]
    public void ASalaryBandWithNoCurrency_IsRejected()
    {
        Grade grade = Grade.Create("L1", "Associate", 1);

        Should.Throw<DomainRuleException>(() => grade.SetSalaryBand(600_000m, 900_000m, null))
            .ErrorCode.ShouldBe("SALARY_BAND_CURRENCY_REQUIRED");
    }

    [Fact]
    public void AValidSalaryBand_IsAccepted()
    {
        Grade grade = Grade.Create("L1", "Associate", 1);

        grade.SetSalaryBand(600_000m, 900_000m, "inr");

        grade.SalaryBandMinimum.ShouldBe(600_000m);
        grade.Currency.ShouldBe("INR");
    }
}

public sealed class DesignationTests
{
    [Fact]
    public void ANegativeJobLevel_IsRejected()
    {
        Should.Throw<DomainRuleException>(() => Designation.Create("SDE1", "Software Engineer I", jobLevel: -1))
            .ErrorCode.ShouldBe("JOB_LEVEL_INVALID");
    }

    [Fact]
    public void UpdatingKeepsTheCodeButChangesEverythingElse()
    {
        Designation designation = Designation.Create("SDE1", "Software Engineer I", "Engineering", 3);

        designation.Update("Software Engineer II", "Engineering", 4);

        designation.Title.ShouldBe("Software Engineer II");
        designation.JobLevel.ShouldBe(4);
        designation.Code.ShouldBe("SDE1");
    }
}
