using Gggs.Documents.Domain.Documents;
using Shouldly;

namespace Gggs.Documents.UnitTests;

public sealed class DocumentTests
{
    [Fact]
    public void Upload_StartsAsVersionOneAndItsOwnGroup()
    {
        Document document = Document.Upload(
            DocumentOwnerType.Employee,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "passport.pdf",
            "application/pdf",
            1024,
            "t-abc/employee/x/y/v1/passport.pdf",
            new string('a', 64),
            Guid.CreateVersion7(),
            expiryDate: null);

        document.Version.ShouldBe(1);
        document.IsCurrentVersion.ShouldBeTrue();
        document.DocumentGroupId.ShouldBe(document.Id);
        document.ScanStatus.ShouldBe(DocumentScanStatus.Skipped);
    }

    [Fact]
    public void Upload_WithNoBytes_Fails()
    {
        Should.Throw<Gggs.BuildingBlocks.Domain.Exceptions.DomainRuleException>(() => Document.Upload(
            DocumentOwnerType.Employee,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "empty.pdf",
            "application/pdf",
            sizeBytes: 0,
            "t-abc/employee/x/y/v1/empty.pdf",
            new string('a', 64),
            Guid.CreateVersion7(),
            expiryDate: null));
    }

    [Fact]
    public void Upload_WithAnEmptyOwnerId_Fails()
    {
        Should.Throw<ArgumentException>(() => Document.Upload(
            DocumentOwnerType.Employee,
            Guid.Empty,
            Guid.CreateVersion7(),
            "file.pdf",
            "application/pdf",
            1024,
            "t-abc/employee/x/y/v1/file.pdf",
            new string('a', 64),
            Guid.CreateVersion7(),
            expiryDate: null));
    }
}
