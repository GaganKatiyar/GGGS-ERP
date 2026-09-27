using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Domain.Organization;
using Shouldly;

namespace Gggs.CoreHr.UnitTests;

public sealed class LegalEntityTests
{
    [Fact]
    public void CreatingWithAValidCountryAndCurrency_NormalizesCodeAndCasing()
    {
        LegalEntity entity = LegalEntity.Create("  acme-in  ", "Acme India Private Limited", "in", "inr");

        entity.Code.ShouldBe("ACME-IN");
        entity.CountryCode.ShouldBe("IN");
        entity.Currency.ShouldBe("INR");
    }

    [Theory]
    [InlineData("I")]
    [InlineData("IND")]
    public void ACountryCodeThatIsNotTwoLetters_IsRejected(string countryCode)
    {
        Should.Throw<DomainRuleException>(() => LegalEntity.Create("ACME", "Acme", countryCode, "INR"))
            .ErrorCode.ShouldBe("COUNTRY_CODE_INVALID");
    }

    [Theory]
    [InlineData("R")]
    [InlineData("RUPEE")]
    public void ACurrencyCodeThatIsNotThreeLetters_IsRejected(string currency)
    {
        Should.Throw<DomainRuleException>(() => LegalEntity.Create("ACME", "Acme", "IN", currency))
            .ErrorCode.ShouldBe("CURRENCY_CODE_INVALID");
    }

    [Fact]
    public void ANewLegalEntity_HasNoAddressUntilOneIsSet()
    {
        LegalEntity entity = LegalEntity.Create("ACME", "Acme", "IN", "INR");
        entity.Address.IsEmpty.ShouldBeTrue();

        entity.SetAddress(new Address("221B Baker Street", null, "Mumbai", "MH", "400001", "IN"));

        entity.Address.IsEmpty.ShouldBeFalse();
        entity.Address.City.ShouldBe("Mumbai");
    }
}
