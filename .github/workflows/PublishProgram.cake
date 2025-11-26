// Publish.cake - Publish single-file self-contained exe
#load "./Environment.cake"
var configuration = Argument("configuration", "Release");
var runtime = Argument("runtime", "win-x64");
var projectPath = Argument("project", "../../src/AvConsoleToolkit/AvConsoleToolkit.csproj");
var artifactsDir = GetArtifactsPath();
var publishDir = GetPublishPath();

Task("Default")
    .Does(() =>
{
    if (!DirectoryExists(artifactsDir) || !DirectoryExists(publishDir))
    {
        throw new InvalidOperationException("Artifacts directories not found. Run Clean first.");
    }

    var derivedVersion = GetEnvironmentVariable("RELEASE_VERSION");

    if (derivedVersion is null)
    {
        throw new InvalidOperationException("Derived version not found. Run DetermineVersion first or provide artifacts/version.txt.");
    }

    Information($"Publishing with version: {derivedVersion}");

    // Parse version to handle pre-release suffixes
    var versionParts = derivedVersion.Split('-', 2);
    var baseVersion = versionParts[0];
    var suffix = versionParts.Length > 1 ? versionParts[1] : "";

    var publishSettings = new DotNetPublishSettings
    {
        Configuration = configuration,
        Runtime = runtime,
        SelfContained = true,
        OutputDirectory = publishDir,
        EnableCompressionInSingleFile = true,
        PublishSingleFile = true,
        ArgumentCustomization = args => {
            args = args.Append($"/p:Version={derivedVersion}");
            args = args.Append($"/p:AssemblyVersion={baseVersion}");
            args = args.Append($"/p:FileVersion={baseVersion}");
            args = args.Append($"/p:InformationalVersion={derivedVersion}");
            if (!string.IsNullOrEmpty(suffix))
            {
                args = args.Append($"/p:VersionSuffix={suffix}");
            }
            return args;
        }
    };

    DotNetPublish(projectPath, publishSettings);
    Information($"Published to {publishDir} with version {derivedVersion}");
});

RunTarget(Argument("target", "Default"));
