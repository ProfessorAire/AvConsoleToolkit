// DetermineVersion.cake - Determine next semantic version from conventional commits
#nullable enable
using System.Text.RegularExpressions;
using System.Globalization;

#load "./Environment.cake"

var artifactsDir = GetArtifactsPath();

string RunGit(string args)
{
    var settings = new ProcessSettings { Arguments = args, RedirectStandardOutput = true, Silent = true };
    var proc = StartAndReturnProcess("git", settings);
    var output = proc.GetStandardOutput();
    proc.WaitForExit();
    return string.Join("\n", output ?? new string[0]);
}

string? GetLatestSemVerTag()
{
    var outStr = RunGit("tag --list \"v*.*.*\" --sort=-v:refname");
    if (string.IsNullOrWhiteSpace(outStr)) return null;
    var first = outStr.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    return first;
}

IList<(string Sha, string Header, string Body)> GetCommitsSince(string? fromTag)
{
    string range = string.IsNullOrWhiteSpace(fromTag) ? "HEAD" : $"{fromTag}..HEAD";
    var fmt = "%H%x01%s%x01%b%x01==END==";
    var raw = RunGit($"log {range} --pretty=format:{fmt}");
    var entries = new List<(string, string, string)>();
    if (string.IsNullOrWhiteSpace(raw)) return entries;
    var parts = raw.Split(new[] { "==END==" }, StringSplitOptions.RemoveEmptyEntries);
    foreach (var p in parts)
    {
        var segs = p.Split('\x01');
        if (segs.Length >= 3)
        {
            var sha = segs[0].Trim();
            var header = segs[1].Trim();
            var body = string.Join("\n", segs.Skip(2)).Trim();
            entries.Add((sha, header, body));
        }
    }
    return entries;
}

bool IsBreaking((string Sha, string Header, string Body) commit)
{
    var header = commit.Header;
    var body = commit.Body ?? string.Empty;
    var m = Regex.Match(header, @"^[a-z]+(\([^)]+\))?(?<bang>!)?:", RegexOptions.IgnoreCase);
    if (m.Success && m.Groups["bang"].Success && m.Groups["bang"].Value == "!") return true;
    if (Regex.IsMatch(body, @"BREAKING CHANGE", RegexOptions.IgnoreCase)) return true;
    return false;
}

string? GetType((string Sha, string Header, string Body) commit)
{
    var m = Regex.Match(commit.Header, @"^(?<type>[a-z]+)(?:\([^\)]+\))?(?:!?)?:", RegexOptions.IgnoreCase);
    return m.Success ? m.Groups["type"].Value.ToLowerInvariant() : null;
}

SemanticVersion ParseSemVerTag(string tag)
{
    var v = tag.TrimStart('v', 'V');
    var parts = v.Split('-', 2);
    var numbers = parts[0].Split('.');
    int major = numbers.Length > 0 ? int.Parse(numbers[0], CultureInfo.InvariantCulture) : 0;
    int minor = numbers.Length > 1 ? int.Parse(numbers[1], CultureInfo.InvariantCulture) : 0;
    int patch = numbers.Length > 2 ? int.Parse(numbers[2], CultureInfo.InvariantCulture) : 0;
    return new SemanticVersion(major, minor, patch);
}

SemanticVersion Bump(SemanticVersion baseVersion, string level)
{
    if (level == "major") return new SemanticVersion(baseVersion.Major + 1, 0, 0);
    if (level == "minor") return new SemanticVersion(baseVersion.Major, baseVersion.Minor + 1, 0);
    return new SemanticVersion(baseVersion.Major, baseVersion.Minor, baseVersion.Patch + 1);
}

Task("Default")
    .Does(() =>
{
    if (!DirectoryExists(artifactsDir)) CreateDirectory(artifactsDir);

    var latestTag = GetLatestSemVerTag();
    Information($"Latest tag: {latestTag ?? "(none)"}");
    SemanticVersion baseVersion = latestTag is null ? new SemanticVersion(1, 0, 0) : ParseSemVerTag(latestTag);

    var commits = GetCommitsSince(latestTag);
    if (commits.Count == 0)
    {
        throw new Exception("No changes to release. If you need to republish a version, please delete the previous tag and rerun the workflow.");
        return;
    }

    bool anyBreaking = false;
    bool anyFeat = false;
    bool anyFix = false;
    var breakingChanges = new List<(string ShortSha, string Header, string Body)>();
    var grouped = new Dictionary<string, List<(string ShortSha, string Header, string Body)>>();

    // Allowed types for changelog (unless breaking)
    var allowedTypes = new HashSet<string> { "feat", "fix", "docs", "task" };

    foreach (var c in commits)
    {
        var isBreaking = IsBreaking(c);
        var type = GetType(c) ?? "other";

        if (isBreaking)
        {
            anyBreaking = true;
            breakingChanges.Add((c.Sha.Substring(0, 7), c.Header, c.Body));
        }

        if (type == "feat") anyFeat = true;
        if (type == "fix") anyFix = true;

        // Only group non-breaking changes of allowed types
        if (!isBreaking && allowedTypes.Contains(type))
        {
            if (!grouped.ContainsKey(type)) grouped[type] = new List<(string, string, string)>();
            grouped[type].Add((c.Sha.Substring(0, 7), c.Header, c.Body));
        }
    }

    SemanticVersion newVersion;
    if (latestTag is null)
    {
        Information("No existing version tag found. Assuming base version {0}.", baseVersion);
        newVersion = baseVersion;
    }
    else
    {
        string level = anyBreaking ? "major" : anyFeat ? "minor" : anyFix ? "patch" : "patch";
        newVersion = Bump(baseVersion, level);
        Information($"New version: {newVersion} (level: {level})");
    }

    var changelog = new System.Text.StringBuilder();
    changelog.AppendLine($"# Release v{newVersion} - {DateTime.UtcNow:yyyy-MM-dd}");
    changelog.AppendLine();

    // Breaking Changes section (if any)
    if (breakingChanges.Count > 0)
    {
        changelog.AppendLine("## Breaking Changes");
        changelog.AppendLine();
        foreach (var entry in breakingChanges)
        {
            var stripped = Regex.Replace(entry.Header, @"^[a-z]+(\([^\)]+\))?(?:!?)?:\s*", "", RegexOptions.IgnoreCase);
            changelog.AppendLine($"* {stripped} ({entry.ShortSha})");
        }
        changelog.AppendLine();
    }

    // Order sections: feat, fix, docs, task
    var count = 0;
    var orderedTypes = new[] { "feat", "fix", "docs", "task" };
    foreach (var type in orderedTypes)
    {
        if (!grouped.ContainsKey(type)) continue;
        count++;
        var heading = type switch
        {
            "feat" => "## Features",
            "fix" => "## Bug Fixes",
            "docs" => "## Documentation",
            "task" => "## Other Tasks",
            _ => $"## {type}"
        };
        changelog.AppendLine(heading);
        changelog.AppendLine();
        foreach (var entry in grouped[type])
        {
            var stripped = Regex.Replace(entry.Header, @"^[a-z]+(\([^\)]+\))?(?:!?)?:\s*", "", RegexOptions.IgnoreCase);
            changelog.AppendLine($"* {stripped} ({entry.ShortSha})");
        }
        changelog.AppendLine();
    }

    if (count == 0 && breakingChanges.Count == 0 && newVersion.ToString() == "1.0.0")
    {
        changelog.AppendLine("* Initial Release");
    }
    else if (count == 0 && breakingChanges.Count == 0)
    {
        throw new Exception("No commits found for changelog. Please ensure commits follow Conventional Commits specification.");
    }

    SetEnvironmentVariable("RELEASE_VERSION", newVersion.ToString());
    System.IO.File.WriteAllText(System.IO.Path.Combine(artifactsDir, "changelog.md"), changelog.ToString());
    Information("Wrote artifacts/version.txt and artifacts/changelog.md");
});

RunTarget(Argument("target", "Default"));
record SemanticVersion(int Major, int Minor, int Patch)
{
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}