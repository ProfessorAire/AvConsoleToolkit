// Package.cake - Package published exe into a zip with version and changelog
#load "./Environment.cake"
var artifactsDir = GetArtifactsPath();
var publishDir = GetPublishPath();

Task("Default")
    .Does(() =>
{
    var derivedVersion = GetEnvironmentVariable("RELEASE_VERSION");

    if (derivedVersion is null) throw new InvalidOperationException("Derived version not found. Run DetermineVersion first.");

    var exeFiles = GetFiles(System.IO.Path.Combine(publishDir, "**", "AvConsoleToolkit.exe"));
    if (!exeFiles.Any()) exeFiles = GetFiles(System.IO.Path.Combine(publishDir, "**", "*.exe"));
    if (!exeFiles.Any()) throw new InvalidOperationException("Published executable not found in publish directory.");

    var mdFiles = GetFiles(System.IO.Path.Combine(artifactsDir, "*.md"));
    if (!mdFiles.Any(f => f.GetFilename().ToString().Equals("changelog.md", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("changelog.md not found in artifacts directory.");
    }

    if (!mdFiles.Any(f => f.GetFilename().ToString().Equals("THIRD_PARTY_LICENSES.md", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("THIRD_PARTY_LICENSES.md not found in artifacts directory.");
    }

    var tmpFolder = System.IO.Path.Combine(artifactsDir, $"AvConsoleToolkit-{derivedVersion}");
    if (DirectoryExists(tmpFolder)) DeleteDirectory(tmpFolder, new DeleteDirectorySettings { Recursive = true, Force = true });
    CreateDirectory(tmpFolder);

    foreach (var f in exeFiles) CopyFileToDirectory(f, tmpFolder);

    CopyFile(System.IO.Path.Combine(artifactsDir, "changelog.md"), System.IO.Path.Combine(tmpFolder, "changelog.md"));
    CopyFile(System.IO.Path.Combine(artifactsDir, "THIRD_PARTY_LICENSES.md"), System.IO.Path.Combine(tmpFolder, "THIRD_PARTY_LICENSES.md"));

    var zipName = $"AvConsoleToolkit-v{derivedVersion}.zip";
    var zipPath = System.IO.Path.Combine(artifactsDir, zipName);
    Zip(tmpFolder, zipPath);
    Information($"Packaged {zipPath}");
});

RunTarget(Argument("target", "Default"));
