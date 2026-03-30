using DotNetInstallManager.Services;

namespace DotNetInstallManager.Tests.Services;

public sealed class ExistingInstallDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnet-install-existing-tests", Guid.NewGuid().ToString("N"));

    public ExistingInstallDetectorTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void DetectInInstallRoot_ReturnsSdkMatch_WhenVersionDirectoryExists()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sdk", "10.0.300"));
        var plan = CreatePlan(InstallProductKind.Sdk, "10.0.300");

        var matches = ExistingInstallDetector.DetectInInstallRoot(plan, _root);

        var match = Assert.Single(matches);
        Assert.Equal("install root", match.Source);
        Assert.EndsWith(Path.Combine("sdk", "10.0.300"), match.Location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseSdkMatches_ReturnsMatch_WhenSdkVersionIsListed()
    {
        const string output = """
10.0.300 [C:\Program Files\dotnet\sdk]
9.0.308 [C:\Program Files\dotnet\sdk]
""";

        var matches = ExistingInstallDetector.ParseSdkMatches(output, "10.0.300");

        var match = Assert.Single(matches);
        Assert.Equal("dotnet --list-sdks", match.Source);
        Assert.Equal(@"C:\Program Files\dotnet\sdk", match.Location);
    }

    [Fact]
    public void ParseRuntimeMatches_ReturnsMatch_WhenRuntimeVersionIsListed()
    {
        const string output = """
Microsoft.NETCore.App 10.0.5 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
Microsoft.AspNetCore.App 10.0.5 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
""";

        var matches = ExistingInstallDetector.ParseRuntimeMatches(output, "Microsoft.NETCore.App", "10.0.5");

        var match = Assert.Single(matches);
        Assert.Equal("dotnet --list-runtimes", match.Source);
        Assert.Equal(@"C:\Program Files\dotnet\shared\Microsoft.NETCore.App", match.Location);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static InstallPlan CreatePlan(InstallProductKind kind, string version) =>
        new(
            ChannelVersion: "10.0",
            ReleaseVersion: "10.0.5",
            ProductVersion: version,
            ProductKind: kind,
            TargetRid: "win-x64",
            AssetName: "payload.zip",
            SourceUrl: "https://example.test/payload.zip",
            CandidateUrls: ["https://example.test/payload.zip"],
            ExpectedHash: null,
            IsPreview: false);
}
