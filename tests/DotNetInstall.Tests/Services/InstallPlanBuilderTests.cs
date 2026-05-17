using DotNetInstall.Options;
using DotNetInstall.Services;
using DotNetInstall.Tests.ActionScripts;

namespace DotNetInstall.Tests.Services;

public sealed class InstallPlanBuilderTests
{
    private static InstallOptions DefaultOptions(
        string channel = "LTS",
        string? quality = null,
        string version = "Latest",
        string? runtime = null,
        bool sharedRuntime = false,
        string architecture = "x64",
        string os = "win") =>
        new(Channel: channel,
            Quality: quality,
            Version: version,
            Internal: false,
            GlobalJsonFile: null,
            InstallDir: "<auto>",
            Architecture: architecture,
            Runtime: runtime,
            SharedRuntime: sharedRuntime,
            DryRun: false,
            Yes: false,
            NoPath: false,
            PersistPath: false,
            AzureFeed: null,
            UncachedFeed: null,
            FeedCredential: null,
            ProxyAddress: null,
            ProxyUseDefaultCredentials: false,
            ProxyBypassList: Array.Empty<string>(),
            OverrideNonVersionedFiles: true,
            KeepZip: false,
            ZipPath: null,
            Verbose: false,
            RuntimeId: $"{os}-x64",
            UserProvidedOs: null,
            DownloadTimeoutSeconds: 1200);

    [Fact]
    public async Task BuildAsync_ReturnsLatestLtsRelease_WhenChannelIsLts()
    {
        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5");

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "lts");

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal("8.0", plan.ChannelVersion);
        Assert.Equal("8.0.5", plan.ReleaseVersion);
        Assert.Equal(InstallProductKind.Sdk, plan.ProductKind);
        Assert.Equal("8.0.205", plan.ProductVersion);
        Assert.False(plan.IsPreview);
    }

    [Fact]
    public async Task BuildAsync_SelectsStsChannel_WhenChannelIsSts()
    {
        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "9.0",
            releaseVersion: "9.0.1",
            sdkVersion: "9.0.101",
            runtimeVersion: "9.0.1");

        var client = FakeReleaseMetadataClient.CreateSimple("9.0", "sts", releaseDocument);
        var options = DefaultOptions(channel: "sts");

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal("9.0", plan.ChannelVersion);
        Assert.Equal(InstallProductKind.Sdk, plan.ProductKind);
    }

    [Fact]
    public async Task BuildAsync_SelectsRuntimeProduct_WhenRuntimeOptionIsSet()
    {
        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5");

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "lts", runtime: "dotnet");

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal(InstallProductKind.Runtime, plan.ProductKind);
        Assert.Equal("8.0.5", plan.ProductVersion);
    }

    [Fact]
    public async Task BuildAsync_SelectsSdkProduct_WhenSharedRuntimeIsFalse()
    {
        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5");

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "lts", sharedRuntime: false);

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal(InstallProductKind.Sdk, plan.ProductKind);
    }

    [Fact]
    public async Task BuildAsync_MatchesExplicitVersion()
    {
        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5");

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "lts", version: "8.0.205");

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal("8.0.205", plan.ProductVersion);
    }

    [Fact]
    public async Task BuildAsync_UsesSdkVersionFromGlobalJson()
    {
        using var tempDir = new TemporaryDirectory();
        var globalJsonPath = tempDir.GetPath("global.json");
        await File.WriteAllTextAsync(globalJsonPath, """
        {
          "sdk": {
            "version": "10.0.300"
          }
        }
        """);

        var tenChannelDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "10.0",
            releaseVersion: "10.0.5",
            sdkVersion: "10.0.300",
            runtimeVersion: "10.0.5");

        var eightChannelDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5");

        var client = new FakeReleaseMetadataClient(
            new ReleaseIndexDocument(
            [
                new ReleaseIndexEntry(
                    ChannelVersion: "8.0",
                    Product: ".NET",
                    ReleaseType: "lts",
                    SupportPhase: "active",
                    ReleasesJsonUrl: "https://fake.test/8.0/releases.json",
                    LatestRelease: "8.0.5",
                    LatestSdk: "8.0.205",
                    LatestRuntime: "8.0.5"),
                new ReleaseIndexEntry(
                    ChannelVersion: "10.0",
                    Product: ".NET",
                    ReleaseType: "lts",
                    SupportPhase: "active",
                    ReleasesJsonUrl: "https://fake.test/10.0/releases.json",
                    LatestRelease: "10.0.5",
                    LatestSdk: "10.0.300",
                    LatestRuntime: "10.0.5")
            ]),
            new Dictionary<string, ReleaseDocument>
            {
                ["https://fake.test/8.0/releases.json"] = eightChannelDocument,
                ["https://fake.test/10.0/releases.json"] = tenChannelDocument
            });

        var options = DefaultOptions(channel: "8.0") with
        {
            GlobalJsonFile = new FileInfo(globalJsonPath)
        };

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal("10.0", plan.ChannelVersion);
        Assert.Equal("10.0.300", plan.ProductVersion);
    }

    [Theory]
    [InlineData("feature")]
    [InlineData("latestFeature")]
    [InlineData("minor")]
    [InlineData("latestMinor")]
    [InlineData("major")]
    [InlineData("latestMajor")]
    public async Task BuildAsync_UsesGlobalJsonRollForward_ToResolveWildcardSdkVersion(string rollForward)
    {
        using var tempDir = new TemporaryDirectory();
        var globalJsonPath = tempDir.GetPath("global.json");
        await File.WriteAllTextAsync(globalJsonPath, $$"""
        {
          "sdk": {
            "version": "10.0.100",
            "rollForward": "{{rollForward}}"
          }
        }
        """);

        static ReleaseEntry CreateRelease(string releaseVersion, string releaseDate, string sdkVersion)
        {
            const string rid = "win-x64";
            var sdkFile = new ReleaseFile(
                Name: $"dotnet-sdk-{sdkVersion}-{rid}.zip",
                Rid: rid,
                Url: $"https://builds.dotnet.microsoft.com/dotnet/Sdk/{sdkVersion}/dotnet-sdk-{sdkVersion}-{rid}.zip",
                Hash: null);

            var runtimeFile = new ReleaseFile(
                Name: $"dotnet-runtime-{releaseVersion}-{rid}.zip",
                Rid: rid,
                Url: $"https://builds.dotnet.microsoft.com/dotnet/Runtime/{releaseVersion}/dotnet-runtime-{releaseVersion}-{rid}.zip",
                Hash: null);

            var sdk = new ReleaseProduct(Version: sdkVersion, VersionDisplay: sdkVersion, Files: [sdkFile]);
            var runtime = new ReleaseProduct(Version: releaseVersion, VersionDisplay: releaseVersion, Files: [runtimeFile]);

            return new ReleaseEntry(
                ReleaseVersion: releaseVersion,
                ReleaseDate: releaseDate,
                Sdk: sdk,
                Sdks: [sdk],
                Runtime: runtime,
                AspNetCoreRuntime: null,
                WindowsDesktopRuntime: null);
        }

        var releaseDocument = new ReleaseDocument(
            ChannelVersion: "10.0",
            ReleaseType: "lts",
            SupportPhase: "active",
            Releases:
            [
                CreateRelease("10.0.5", "2026-03-10", "10.0.300"),
                CreateRelease("10.0.1", "2026-01-10", "10.0.100")
            ]);

        var client = FakeReleaseMetadataClient.CreateSimple("10.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "8.0") with
        {
            GlobalJsonFile = new FileInfo(globalJsonPath)
        };

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal("10.0.300", plan.ProductVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("disable")]
    [InlineData("patch")]
    [InlineData("latestPatch")]
    public async Task BuildAsync_UsesExactGlobalJsonSdkVersion_ForStrictRollForwardPolicies(string? rollForward)
    {
        using var tempDir = new TemporaryDirectory();
        var globalJsonPath = tempDir.GetPath("global.json");
        var rollForwardJson = rollForward is null ? string.Empty : $"""
            ,
                "rollForward": "{rollForward}"
        """;
        await File.WriteAllTextAsync(globalJsonPath, $$"""
        {
          "sdk": {
            "version": "10.0.100"{{rollForwardJson}}
          }
        }
        """);

        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "10.0",
            releaseVersion: "10.0.1",
            sdkVersion: "10.0.100",
            runtimeVersion: "10.0.1");

        var client = FakeReleaseMetadataClient.CreateSimple("10.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "8.0") with
        {
            GlobalJsonFile = new FileInfo(globalJsonPath)
        };

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal("10.0.100", plan.ProductVersion);
    }

    [Fact]
    public async Task BuildAsync_ThrowsInstallException_WhenGlobalJsonDoesNotContainSdkVersion()
    {
        using var tempDir = new TemporaryDirectory();
        var globalJsonPath = tempDir.GetPath("global.json");
        await File.WriteAllTextAsync(globalJsonPath, """{ "sdk": { "rollForward": "latestFeature" } }""");

        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5");

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "lts") with
        {
            GlobalJsonFile = new FileInfo(globalJsonPath)
        };

        await Assert.ThrowsAsync<InstallException>(() =>
            InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None));
    }

    [Fact]
    public async Task BuildAsync_ThrowsInstallException_WhenGlobalJsonIsUsedWithRuntimeInstall()
    {
        using var tempDir = new TemporaryDirectory();
        var globalJsonPath = tempDir.GetPath("global.json");
        await File.WriteAllTextAsync(globalJsonPath, """{ "sdk": { "version": "8.0.205" } }""");

        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5");

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "lts", runtime: "dotnet") with
        {
            GlobalJsonFile = new FileInfo(globalJsonPath)
        };

        await Assert.ThrowsAsync<InstallException>(() =>
            InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None));
    }

    [Fact]
    public async Task BuildAsync_MatchesLatestVersion_WhenVersionUsesMajorWildcard()
    {
        const string rid = "win-x64";

        static ReleaseEntry CreateRelease(string releaseVersion, string releaseDate, string sdkVersion)
        {
            var sdkFile = new ReleaseFile(
                Name: $"dotnet-sdk-{sdkVersion}-{rid}.zip",
                Rid: rid,
                Url: $"https://builds.dotnet.microsoft.com/dotnet/Sdk/{sdkVersion}/dotnet-sdk-{sdkVersion}-{rid}.zip",
                Hash: null);

            var runtimeFile = new ReleaseFile(
                Name: $"dotnet-runtime-{releaseVersion}-{rid}.zip",
                Rid: rid,
                Url: $"https://builds.dotnet.microsoft.com/dotnet/Runtime/{releaseVersion}/dotnet-runtime-{releaseVersion}-{rid}.zip",
                Hash: null);

            var sdk = new ReleaseProduct(Version: sdkVersion, VersionDisplay: sdkVersion, Files: [sdkFile]);
            var runtime = new ReleaseProduct(Version: releaseVersion, VersionDisplay: releaseVersion, Files: [runtimeFile]);

            return new ReleaseEntry(
                ReleaseVersion: releaseVersion,
                ReleaseDate: releaseDate,
                Sdk: sdk,
                Sdks: [sdk],
                Runtime: runtime,
                AspNetCoreRuntime: null,
                WindowsDesktopRuntime: null);
        }

        var tenChannelDocument = new ReleaseDocument(
            ChannelVersion: "10.0",
            ReleaseType: "lts",
            SupportPhase: "active",
            Releases:
            [
                CreateRelease("10.0.5", "2026-03-10", "10.0.300"),
                CreateRelease("10.0.4", "2026-02-11", "10.0.201")
            ]);

        var eightChannelDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5");

        var client = new FakeReleaseMetadataClient(
            new ReleaseIndexDocument(
            [
                new ReleaseIndexEntry(
                    ChannelVersion: "8.0",
                    Product: ".NET",
                    ReleaseType: "lts",
                    SupportPhase: "active",
                    ReleasesJsonUrl: "https://fake.test/8.0/releases.json",
                    LatestRelease: "8.0.5",
                    LatestSdk: "8.0.205",
                    LatestRuntime: "8.0.5"),
                new ReleaseIndexEntry(
                    ChannelVersion: "10.0",
                    Product: ".NET",
                    ReleaseType: "lts",
                    SupportPhase: "active",
                    ReleasesJsonUrl: "https://fake.test/10.0/releases.json",
                    LatestRelease: "10.0.5",
                    LatestSdk: "10.0.300",
                    LatestRuntime: "10.0.5")
            ]),
            new Dictionary<string, ReleaseDocument>
            {
                ["https://fake.test/8.0/releases.json"] = eightChannelDocument,
                ["https://fake.test/10.0/releases.json"] = tenChannelDocument
            });

        var options = DefaultOptions(channel: "8.0", version: "10.x");

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal("10.0", plan.ChannelVersion);
        Assert.Equal("10.0.5", plan.ReleaseVersion);
        Assert.Equal("10.0.300", plan.ProductVersion);
    }

    [Fact]
    public async Task BuildAsync_MatchesLatestVersion_WhenVersionUsesMajorMinorWildcard()
    {
        const string rid = "win-x64";

        static ReleaseEntry CreateRelease(string releaseVersion, string releaseDate, string sdkVersion)
        {
            var sdkFile = new ReleaseFile(
                Name: $"dotnet-sdk-{sdkVersion}-{rid}.zip",
                Rid: rid,
                Url: $"https://builds.dotnet.microsoft.com/dotnet/Sdk/{sdkVersion}/dotnet-sdk-{sdkVersion}-{rid}.zip",
                Hash: null);

            var runtimeFile = new ReleaseFile(
                Name: $"dotnet-runtime-{releaseVersion}-{rid}.zip",
                Rid: rid,
                Url: $"https://builds.dotnet.microsoft.com/dotnet/Runtime/{releaseVersion}/dotnet-runtime-{releaseVersion}-{rid}.zip",
                Hash: null);

            var sdk = new ReleaseProduct(Version: sdkVersion, VersionDisplay: sdkVersion, Files: [sdkFile]);
            var runtime = new ReleaseProduct(Version: releaseVersion, VersionDisplay: releaseVersion, Files: [runtimeFile]);

            return new ReleaseEntry(
                ReleaseVersion: releaseVersion,
                ReleaseDate: releaseDate,
                Sdk: sdk,
                Sdks: [sdk],
                Runtime: runtime,
                AspNetCoreRuntime: null,
                WindowsDesktopRuntime: null);
        }

        var tenChannelDocument = new ReleaseDocument(
            ChannelVersion: "10.0",
            ReleaseType: "lts",
            SupportPhase: "active",
            Releases:
            [
                CreateRelease("10.0.5", "2026-03-10", "10.0.300"),
                CreateRelease("10.0.4", "2026-02-11", "10.0.201")
            ]);

        var nineChannelDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "9.0",
            releaseVersion: "9.0.9",
            sdkVersion: "9.0.308",
            runtimeVersion: "9.0.9");

        var client = new FakeReleaseMetadataClient(
            new ReleaseIndexDocument(
            [
                new ReleaseIndexEntry(
                    ChannelVersion: "9.0",
                    Product: ".NET",
                    ReleaseType: "sts",
                    SupportPhase: "active",
                    ReleasesJsonUrl: "https://fake.test/9.0/releases.json",
                    LatestRelease: "9.0.9",
                    LatestSdk: "9.0.308",
                    LatestRuntime: "9.0.9"),
                new ReleaseIndexEntry(
                    ChannelVersion: "10.0",
                    Product: ".NET",
                    ReleaseType: "lts",
                    SupportPhase: "active",
                    ReleasesJsonUrl: "https://fake.test/10.0/releases.json",
                    LatestRelease: "10.0.5",
                    LatestSdk: "10.0.300",
                    LatestRuntime: "10.0.5")
            ]),
            new Dictionary<string, ReleaseDocument>
            {
                ["https://fake.test/9.0/releases.json"] = nineChannelDocument,
                ["https://fake.test/10.0/releases.json"] = tenChannelDocument
            });

        var options = DefaultOptions(channel: "9.0", version: "10.0.x");

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal("10.0", plan.ChannelVersion);
        Assert.Equal("10.0.5", plan.ReleaseVersion);
        Assert.Equal("10.0.300", plan.ProductVersion);
    }

    [Fact]
    public async Task BuildAsync_ThrowsInstallException_WhenChannelNotFound()
    {
        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5");

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "99.0");

        await Assert.ThrowsAsync<InstallException>(() =>
            InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None));
    }

    [Fact]
    public async Task BuildAsync_ThrowsInstallException_WhenVersionNotFound()
    {
        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5");

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "lts", version: "8.0.999");

        await Assert.ThrowsAsync<InstallException>(() =>
            InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None));
    }

    [Fact]
    public async Task BuildAsync_IncludesPreviewRelease_WhenQualityIsPreview()
    {
        var channelVersion = "9.0";
        var previewVersion = "9.0.0-preview.3";
        var previewSdkVersion = "9.0.100-preview.3";
        const string rid = "win-x64";

        var sdkFile = new ReleaseFile(
            Name: $"dotnet-sdk-{previewSdkVersion}-{rid}.zip",
            Rid: rid,
            Url: $"https://builds.dotnet.microsoft.com/dotnet/Sdk/{previewSdkVersion}/dotnet-sdk-{previewSdkVersion}-{rid}.zip",
            Hash: null);

        var runtimeFile = new ReleaseFile(
            Name: $"dotnet-runtime-{previewVersion}-{rid}.zip",
            Rid: rid,
            Url: $"https://builds.dotnet.microsoft.com/dotnet/Runtime/{previewVersion}/dotnet-runtime-{previewVersion}-{rid}.zip",
            Hash: null);

        var sdk = new ReleaseProduct(Version: previewSdkVersion, VersionDisplay: previewSdkVersion, Files: [sdkFile]);
        var runtime = new ReleaseProduct(Version: previewVersion, VersionDisplay: previewVersion, Files: [runtimeFile]);

        var entry = new ReleaseEntry(
            ReleaseVersion: previewVersion,
            ReleaseDate: "2024-01-01",
            Sdk: sdk,
            Sdks: [sdk],
            Runtime: runtime,
            AspNetCoreRuntime: null,
            WindowsDesktopRuntime: null);

        var releaseDocument = new ReleaseDocument(
            ChannelVersion: channelVersion,
            ReleaseType: "sts",
            SupportPhase: "preview",
            Releases: [entry]);

        var client = FakeReleaseMetadataClient.CreateSimple(channelVersion, "sts", releaseDocument);
        var options = DefaultOptions(channel: "sts", quality: "preview");

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.True(plan.IsPreview);
        Assert.Equal(previewSdkVersion, plan.ProductVersion);
    }

    [Fact]
    public async Task BuildAsync_BuildsCandidateUrls_WithAzureFeedOverride()
    {
        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5");

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", releaseDocument);

        var options = new InstallOptions(
            Channel: "lts",
            Quality: null,
            Version: "Latest",
            Internal: false,
            GlobalJsonFile: null,
            InstallDir: "<auto>",
            Architecture: "x64",
            Runtime: null,
            SharedRuntime: false,
            DryRun: false,
            Yes: false,
            NoPath: false,
            PersistPath: false,
            AzureFeed: "https://custom.azure.feed/dotnet",
            UncachedFeed: null,
            FeedCredential: null,
            ProxyAddress: null,
            ProxyUseDefaultCredentials: false,
            ProxyBypassList: Array.Empty<string>(),
            OverrideNonVersionedFiles: true,
            KeepZip: false,
            ZipPath: null,
            Verbose: false,
            RuntimeId: "win-x64",
            UserProvidedOs: null,
            DownloadTimeoutSeconds: 1200);

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.True(plan.CandidateUrls.Count > 1, "Expected more than one candidate URL when Azure feed is specified");
        Assert.Contains(plan.CandidateUrls, u => u.StartsWith("https://custom.azure.feed/"));
    }

    [Fact]
    public async Task BuildAsync_NormalizesMacRuntimeIdAlias_ToCanonicalOsxRid()
    {
        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5",
            rid: "osx-x64");

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "lts") with
        {
            RuntimeId = "osx-x64"
        };

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal("osx-x64", plan.TargetRid);
        Assert.Equal("dotnet-sdk-8.0.205-osx-x64.zip", plan.AssetName);
    }

    [Fact]
    public async Task BuildAsync_NormalizesAlpineRuntimeIdAlias_ToCanonicalLinuxMuslRid()
    {
        var releaseDocument = FakeReleaseMetadataClient.CreateSdkReleaseDocument(
            channelVersion: "8.0",
            releaseVersion: "8.0.5",
            sdkVersion: "8.0.205",
            runtimeVersion: "8.0.5",
            rid: "linux-musl-x64");

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", releaseDocument);
        var options = DefaultOptions(channel: "lts") with
        {
            RuntimeId = "linux-musl-x64"
        };

        var plan = await InstallPlanBuilder.BuildAsync(options, client, CancellationToken.None);

        Assert.Equal("linux-musl-x64", plan.TargetRid);
        Assert.Equal("dotnet-sdk-8.0.205-linux-musl-x64.zip", plan.AssetName);
    }
}
