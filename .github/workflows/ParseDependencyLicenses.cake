// ParseDependencyLicenses.cake - Extract and validate NuGet package licenses
#nullable enable

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

#load "./Environment.cake"

var artifactsDir = GetArtifactsPath();
var projectPath = Argument("project", "../../src/AvConsoleToolkit/AvConsoleToolkit.csproj");

// MIT-compatible licenses (permissive licenses)
var mitCompatibleLicenses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "ISC", "0BSD",
    "MS-PL", "Unlicense", "CC0-1.0", "CC-BY-3.0", "CC-BY-4.0", "Python-2.0",
    "Zlib", "BSL-1.0", "PostgreSQL"
};

// Incompatible licenses (copyleft and restrictive)
var incompatibleLicenses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "GPL-2.0", "GPL-3.0", "LGPL-2.1", "LGPL-3.0", "AGPL-3.0", "MPL-2.0",
    "EPL-1.0", "EPL-2.0", "CDDL-1.0", "CPL-1.0", "OSL-3.0"
};

record PackageInfo(string Name, string Version, string? LicenseType, string? LicenseUrl, string LicenseText);

string RunCommand(string command, string args)
{
    var settings = new ProcessSettings 
    { 
        Arguments = args, 
        RedirectStandardOutput = true, 
        Silent = true,
        WorkingDirectory = System.IO.Path.GetDirectoryName(projectPath)
    };
    var proc = StartAndReturnProcess(command, settings);
    var output = proc.GetStandardOutput();
    proc.WaitForExit();
    return string.Join("\n", output ?? new string[0]);
}

List<(string Name, string Version)> GetProjectDependencies()
{
    // Use --include-transitive to get all dependencies, not just top-level
    var output = RunCommand("dotnet", $"list \"{projectPath}\" package --include-transitive --format json");
    var packages = new List<(string, string)>();
    
    using var doc = JsonDocument.Parse(output);
    var projects = doc.RootElement.GetProperty("projects");
    
    foreach (var project in projects.EnumerateArray())
    {
        if (!project.TryGetProperty("frameworks", out var frameworks)) continue;
        
        foreach (var framework in frameworks.EnumerateArray())
        {
            // Process both top-level and transitive packages
            if (framework.TryGetProperty("topLevelPackages", out var topLevel))
            {
                foreach (var pkg in topLevel.EnumerateArray())
                {
                    var name = pkg.GetProperty("id").GetString();
                    var version = pkg.GetProperty("resolvedVersion").GetString();
                    if (name != null && version != null)
                    {
                        packages.Add((name, version));
                    }
                }
            }
            
            if (framework.TryGetProperty("transitivePackages", out var transitive))
            {
                foreach (var pkg in transitive.EnumerateArray())
                {
                    var name = pkg.GetProperty("id").GetString();
                    var version = pkg.GetProperty("resolvedVersion").GetString();
                    if (name != null && version != null)
                    {
                        packages.Add((name, version));
                    }
                }
            }
        }
    }
    
    return packages.Distinct().ToList();
}

async Task<PackageInfo> GetPackageLicenseInfo(string packageName, string version, HttpClient http)
{
    Information($"Fetching license info for {packageName} {version}...");
    
    // First, get the service index to find the package base address
    var serviceIndexUrl = "https://api.nuget.org/v3/index.json";
    var serviceIndexResponse = await http.GetAsync(serviceIndexUrl);
    var serviceIndexJson = await serviceIndexResponse.Content.ReadAsStringAsync();
    using var serviceIndexDoc = JsonDocument.Parse(serviceIndexJson);
    
    string? packageBaseAddress = null;
    foreach (var resource in serviceIndexDoc.RootElement.GetProperty("resources").EnumerateArray())
    {
        var type = resource.GetProperty("@type").GetString();
        if (type == "PackageBaseAddress/3.0.0")
        {
            packageBaseAddress = resource.GetProperty("@id").GetString();
            break;
        }
    }
    
    if (packageBaseAddress == null)
    {
        Warning($"Could not find PackageBaseAddress in service index");
        return new PackageInfo(packageName, version, null, null, "License information not available");
    }
    
    // Download the .nuspec file from the package
    var nuspecUrl = $"{packageBaseAddress.TrimEnd('/')}/{packageName.ToLowerInvariant()}/{version.ToLowerInvariant()}/{packageName.ToLowerInvariant()}.nuspec";
    var nuspecResponse = await http.GetAsync(nuspecUrl);
    
    if (!nuspecResponse.IsSuccessStatusCode)
    {
        Warning($"Failed to fetch .nuspec for {packageName} {version} from {nuspecUrl}");
        return new PackageInfo(packageName, version, null, null, "License information not available");
    }
    
    var nuspecXml = await nuspecResponse.Content.ReadAsStringAsync();
    
    string? licenseType = null;
    string? licenseUrl = null;
    
    // Parse the .nuspec XML to extract license information
    var licenseMatch = Regex.Match(nuspecXml, @"<license\s+type=""expression"">([^<]+)</license>", RegexOptions.IgnoreCase);
    if (licenseMatch.Success)
    {
        licenseType = licenseMatch.Groups[1].Value.Trim();
    }
    else
    {
        // Try old-style licenseUrl
        var licenseUrlMatch = Regex.Match(nuspecXml, @"<licenseUrl>([^<]+)</licenseUrl>", RegexOptions.IgnoreCase);
        if (licenseUrlMatch.Success)
        {
            licenseUrl = licenseUrlMatch.Groups[1].Value.Trim();
        }
    }
    
    // Fetch license text
    string licenseText = string.Empty;
    
    if (!string.IsNullOrEmpty(licenseUrl))
    {
        try
        {
            var licenseResponse = await http.GetAsync(licenseUrl);
            if (licenseResponse.IsSuccessStatusCode)
            {
                licenseText = await licenseResponse.Content.ReadAsStringAsync();
                
                // If it's an HTML page, try to extract text content
                if (licenseText.Contains("<html", StringComparison.OrdinalIgnoreCase))
                {
                    licenseText = Regex.Replace(licenseText, @"<[^>]+>", " ");
                    licenseText = Regex.Replace(licenseText, @"\s+", " ").Trim();
                }
            }

            var possible = mitCompatibleLicenses.Where(l => licenseText.Contains(l, StringComparison.Ordinal)).ToArray();
            if (possible.Any())
            {
                licenseType ??= $"Unknown/{string.Join(", ", possible)}?";
                licenseText = string.Empty;
            }
        }
        catch (Exception ex)
        {
            Warning($"Failed to fetch license text from {licenseUrl}: {ex.Message}");
        }
    }
    
    return new PackageInfo(packageName, version, licenseType, licenseUrl, licenseText);
}

bool IsLicenseCompatible(string? licenseType)
{
    if (string.IsNullOrWhiteSpace(licenseType)) return false;
    
    // Handle complex expressions like "MIT OR Apache-2.0"
    var licenses = Regex.Split(licenseType, @"\s+(?:OR|AND)\s+", RegexOptions.IgnoreCase);
    
    foreach (var lic in licenses)
    {
        var cleanLic = lic.Trim().Replace("+", ""); // Remove + suffix
        
        if (incompatibleLicenses.Contains(cleanLic))
        {
            return false;
        }
    }
    
    return true;
}

Task("Default")
    .Does(async () =>
{
    if (!DirectoryExists(artifactsDir)) CreateDirectory(artifactsDir);
    
    var projectFile = MakeAbsolute(File(projectPath));
    if (!FileExists(projectFile))
    {
        throw new InvalidOperationException($"Project file not found: {projectFile}");
    }
    
    Information($"Analyzing dependencies for: {projectFile}");
    
    var dependencies = GetProjectDependencies();
    Information($"Found {dependencies.Count} top-level dependencies");
    
    var licenseDoc = new System.Text.StringBuilder();
    licenseDoc.AppendLine("# Third-Party Licenses");
    licenseDoc.AppendLine();
    licenseDoc.AppendLine("This document contains the licenses for all third-party NuGet packages used in this project.");
    licenseDoc.AppendLine();
    licenseDoc.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
    licenseDoc.AppendLine();
    licenseDoc.AppendLine("---");
    licenseDoc.AppendLine();
    
    var incompatiblePackages = new List<string>();
    
    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd("AvConsoleToolkit-License-Parser");
    
    foreach (var (name, version) in dependencies)
    {
        var packageInfo = await GetPackageLicenseInfo(name, version, http);
        
        // Validate license compatibility
        if (!IsLicenseCompatible(packageInfo.LicenseType))
        {
            var licType = packageInfo.LicenseType ?? "Unknown";
            incompatiblePackages.Add($"{name} {version} ({licType})");
            Warning($"Incompatible license detected: {name} {version} - {licType}");
        }
        
        // Add to document
        licenseDoc.AppendLine($"## {packageInfo.Name} v{packageInfo.Version}");
        licenseDoc.AppendLine();
        
        if (!string.IsNullOrEmpty(packageInfo.LicenseType))
        {
            licenseDoc.AppendLine($"**License Type:** {packageInfo.LicenseType}");
            licenseDoc.AppendLine();
        }
        
        if (!string.IsNullOrEmpty(packageInfo.LicenseUrl))
        {
            licenseDoc.AppendLine($"**License URL:** {packageInfo.LicenseUrl}");
            licenseDoc.AppendLine();
        }
        
        if (!string.IsNullOrEmpty(packageInfo.LicenseText))
        {
          licenseDoc.AppendLine("**License Text:**");
          licenseDoc.AppendLine();
          licenseDoc.AppendLine("```text");
          licenseDoc.AppendLine(packageInfo.LicenseText);
          licenseDoc.AppendLine("```");
          licenseDoc.AppendLine();
        }

          licenseDoc.AppendLine("---");
          licenseDoc.AppendLine();
    }
    
    // Write the license document
    var outputPath = System.IO.Path.Combine(artifactsDir, "THIRD_PARTY_LICENSES.md");
    System.IO.File.WriteAllText(outputPath, licenseDoc.ToString());
    Information($"License document written to: {outputPath}");
    
    // Fail if incompatible licenses found
    if (incompatiblePackages.Count > 0)
    {
        Error("The following packages have licenses incompatible with MIT:");
        foreach (var pkg in incompatiblePackages)
        {
            Error($"  - {pkg}");
        }
        throw new InvalidOperationException($"Found {incompatiblePackages.Count} package(s) with incompatible licenses. Please review and resolve.");
    }
    
    Information("All package licenses are compatible with MIT license.");
});

RunTarget(Argument("target", "Default"));
