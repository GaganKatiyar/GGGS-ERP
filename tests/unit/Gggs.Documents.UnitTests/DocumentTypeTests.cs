using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Documents.Domain.Documents;
using Shouldly;

namespace Gggs.Documents.UnitTests;

public sealed class DocumentTypeTests
{
    [Fact]
    public void Create_NormalizesTheCodeAndTheExtensionList()
    {
        DocumentType type = DocumentType.Create(
            " passport ", "Passport", DocumentCategory.Employee, [".PDF", "jpg", "JPG"], 5, false, true);

        type.Code.ShouldBe("PASSPORT");
        type.AllowedExtensionsCsv.ShouldBe("pdf,jpg");
    }

    [Fact]
    public void Create_WithNoExtensions_Fails()
    {
        Should.Throw<DomainRuleException>(() =>
            DocumentType.Create("EMPTY", "Empty", DocumentCategory.Employee, [], 5, false, false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void Create_WithAnOutOfRangeSize_Fails(int maxSizeMb)
    {
        Should.Throw<DomainRuleException>(() =>
            DocumentType.Create("CODE", "Name", DocumentCategory.Employee, ["pdf"], maxSizeMb, false, false));
    }

    [Theory]
    [InlineData("pdf", true)]
    [InlineData(".pdf", true)]
    [InlineData("PDF", true)]
    [InlineData("exe", false)]
    public void AllowsExtension_MatchesCaseAndDotInsensitively(string candidate, bool expected)
    {
        DocumentType type = DocumentType.Create("CODE", "Name", DocumentCategory.Employee, ["pdf", "jpg"], 5, false, false);

        type.AllowsExtension(candidate).ShouldBe(expected);
    }

    [Fact]
    public void UpdateRules_ReplacesTheExtensionListRatherThanMergingIt()
    {
        DocumentType type = DocumentType.Create("CODE", "Name", DocumentCategory.Employee, ["pdf"], 5, false, false);

        type.UpdateRules(["png"], 10, true, true);

        type.AllowsExtension("pdf").ShouldBeFalse();
        type.AllowsExtension("png").ShouldBeTrue();
        type.MaxSizeMb.ShouldBe(10);
        type.IsEmployeeUploadable.ShouldBeTrue();
        type.IsSensitive.ShouldBeTrue();
    }
}
