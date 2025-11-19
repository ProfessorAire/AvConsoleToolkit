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

    var publishSettings = new DotNetPublishSettings
    {
        Configuration = configuration,
        Runtime = runtime,
        SelfContained = true,
        OutputDirectory = publishDir,
        EnableCompressionInSingleFile = true,
        PublishSingleFile = true,
        ArgumentCustomization = args => args.Append($"/p:VersionPrefix={derivedVersion}")
    };

    DotNetPublish(projectPath, publishSettings);
    Information($"Published to {publishDir}");
});

RunTarget(Argument("target", "Default"));
