using Gggs.Modules.Platform.Endpoints;
using Shouldly;

namespace Gggs.Platform.UnitTests;

/// <summary>How the About page splits the assembly's informational version into version and commit.</summary>
public sealed class AboutVersionTests
{
    [Fact]
    public void AVersionWithASourceRevision_IsSplitAtThePlus()
    {
        (string version, string? commit) = AboutEndpoints.ParseInformationalVersion("1.0.0+01e0f7c380709f3588a68debb7af26dd6888b86c");

        version.ShouldBe("1.0.0");
        commit.ShouldBe("01e0f7c380709f3588a68debb7af26dd6888b86c");
    }

    [Fact]
    public void AVersionWithoutARevision_HasNoCommit()
    {
        (string version, string? commit) = AboutEndpoints.ParseInformationalVersion("2026.11.0");

        version.ShouldBe("2026.11.0");
        commit.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AMissingVersion_ReadsUnknown(string? informational)
    {
        (string version, string? commit) = AboutEndpoints.ParseInformationalVersion(informational);

        version.ShouldBe("unknown");
        commit.ShouldBeNull();
    }
}
