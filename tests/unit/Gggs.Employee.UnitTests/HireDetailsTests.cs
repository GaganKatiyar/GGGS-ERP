using Gggs.Modules.Employee.Application;
using Shouldly;

namespace Gggs.Employee.UnitTests;

public sealed class StatutoryFormatTests
{
    [Theory]
    [InlineData("234567890124", true)]
    [InlineData("234567890120", false)] // wrong check digit
    [InlineData("134567890124", false)] // cannot start with 1
    [InlineData("23456789012", false)] // eleven digits
    public void Aadhaar_needs_twelve_digits_and_a_valid_check_digit(string value, bool valid)
        => StatutoryFormats.Aadhaar(value).ShouldBe(valid);

    [Theory]
    [InlineData("K1234567", true)]
    [InlineData("AB12345", true)]
    [InlineData("K12-4567", false)]
    [InlineData("K1234567890", false)]
    public void Passport_is_six_to_nine_letters_or_digits(string value, bool valid)
        => StatutoryFormats.Passport(value).ShouldBe(valid);
}
