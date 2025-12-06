// <copyright file="GitHubReleasePackageResolver.cs">
// The MIT License
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Onova.Services;
using Spectre.Console;

namespace AvConsoleToolkit.Updates
{
    /// <summary>
    /// A custom GitHub package resolver that supports semantic versioning and pre-release versions.
    /// Unlike the built-in Onova resolver, this resolver properly handles pre-release versions
    /// and allows filtering based on whether pre-release versions should be included.
    /// </summary>
    public sealed class GitHubReleasePackageResolver : IPackageResolver
    {
        private readonly string assetNamePattern;

        private readonly HttpClient httpClient;

        private readonly bool includePreRelease;

        private readonly string owner;

        private readonly string repository;

        private readonly bool verbose;

        private Dictionary<SemanticVersion, GitHubRelease>? _releasesCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="GitHubReleasePackageResolver"/> class.
        /// </summary>
        /// <param name="owner">The GitHub repository owner.</param>
        /// <param name="repository">The GitHub repository name.</param>
        /// <param name="assetNamePattern">The wildcard pattern for matching release asset names (e.g., "MyApp-*.zip").</param>
        /// <param name="includePreRelease">Whether to include pre-release versions.</param>
        /// <param name="verbose">Whether to perform verbose logging.</param>
        public GitHubReleasePackageResolver(string owner, string repository, string assetNamePattern, bool includePreRelease = false, bool verbose = false)
            : this(CreateDefaultHttpClient(), owner, repository, assetNamePattern, includePreRelease, verbose)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitHubReleasePackageResolver"/> class with a custom <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use for API requests.</param>
        /// <param name="owner">The GitHub repository owner.</param>
        /// <param name="repository">The GitHub repository name.</param>
        /// <param name="assetNamePattern">The wildcard pattern for matching release asset names (e.g., "MyApp-*.zip").</param>
        /// <param name="includePreRelease">Whether to include pre-release versions.</param>
        /// <param name="verbose">Whether to perform verbose logging.</param>
        public GitHubReleasePackageResolver(HttpClient httpClient, string owner, string repository, string assetNamePattern, bool includePreRelease = false, bool verbose = false)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentException.ThrowIfNullOrWhiteSpace(owner);
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);
            ArgumentException.ThrowIfNullOrWhiteSpace(assetNamePattern);

            this.httpClient = httpClient;
            this.owner = owner;
            this.repository = repository;
            this.assetNamePattern = assetNamePattern;
            this.includePreRelease = includePreRelease;
            this.verbose = verbose;
        }

        /// <summary>
        /// Downloads a package for the specified version to a file.
        /// </summary>
        /// <param name="version">The version to download.</param>
        /// <param name="destFilePath">The destination file path.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task DownloadPackageAsync(Version version, string destFilePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            await this.EnsureReleasesCachedAsync(cancellationToken);

            // Find matching release by converting System.Version to SemanticVersion for lookup
            var matchingRelease = this._releasesCache!
                .FirstOrDefault(kvp => kvp.Key.Major == version.Major &&
                                       kvp.Key.Minor == version.Minor &&
                                       kvp.Key.Patch == version.Build);

            if (matchingRelease.Value == null)
            {
                throw new InvalidOperationException($"Version {version} not found in available releases.");
            }

            var assetUrl = matchingRelease.Value.AssetUrl;
            if (string.IsNullOrEmpty(assetUrl))
            {
                throw new InvalidOperationException($"No matching asset found for version {version}.");
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(destFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var destStream = File.Create(destFilePath);
            await this.DownloadAssetAsync(assetUrl, destStream, progress, cancellationToken);
        }

        /// <summary>
        /// Downloads a package for the specified semantic version to a file.
        /// </summary>
        /// <param name="version">The semantic version to download.</param>
        /// <param name="destFilePath">The destination file path.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task DownloadPackageAsync(SemanticVersion version, string destFilePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            await this.EnsureReleasesCachedAsync(cancellationToken);

            if (!this._releasesCache!.TryGetValue(version, out var release))
            {
                throw new InvalidOperationException($"Version {version} not found in available releases.");
            }

            var assetUrl = release.AssetUrl;
            if (string.IsNullOrEmpty(assetUrl))
            {
                throw new InvalidOperationException($"No matching asset found for version {version}.");
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(destFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var destStream = File.Create(destFilePath);
            await this.DownloadAssetAsync(assetUrl, destStream, progress, cancellationToken);
        }

        /// <summary>
        /// Gets all available package versions from GitHub releases.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A list of available versions.</returns>
        public async Task<IReadOnlyList<Version>> GetPackageVersionsAsync(CancellationToken cancellationToken = default)
        {
            await this.EnsureReleasesCachedAsync(cancellationToken);

            // Convert semantic versions to System.Version (losing pre-release info for Onova compatibility)
            // Onova requires System.Version, so we have to convert
            return this._releasesCache!.Keys
                .OrderByDescending(v => v)
                .Select(v => v.ToVersion())
                .ToList();
        }

        /// <summary>
        /// Gets all available semantic versions from GitHub releases.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A list of available semantic versions, ordered from newest to oldest.</returns>
        public async Task<IReadOnlyList<SemanticVersion>> GetSemanticVersionsAsync(CancellationToken cancellationToken = default)
        {
            await this.EnsureReleasesCachedAsync(cancellationToken);

            return this._releasesCache!.Keys
                .OrderByDescending(v => v)
                .ToList();
        }

        private static HttpClient CreateDefaultHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AvConsoleToolkit", "1.0"));
            return client;
        }

        private static Regex WildcardToRegex(string pattern)
        {
            var escapedPattern = Regex.Escape(pattern);
            escapedPattern = escapedPattern.Replace("\\*", ".*").Replace("\\?", ".");
            return new Regex($"^{escapedPattern}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        private async Task DownloadAssetAsync(string assetUrl, Stream destStream, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            using var response = await this.httpClient.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

            var buffer = new byte[81920];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytesRead += bytesRead;

                if (contentLength.HasValue && contentLength.Value > 0)
                {
                    progress?.Report(((double)totalBytesRead) / contentLength.Value);
                }
            }

            progress?.Report(1.0);
        }

        private async Task EnsureReleasesCachedAsync(CancellationToken cancellationToken)
        {
            if (this._releasesCache != null)
            {
                return;
            }

            this._releasesCache = [];

            var apiUrl = $"https://api.github.com/repos/{this.owner}/{this.repository}/releases";
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await this.httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);

            var assetPattern = WildcardToRegex(this.assetNamePattern);

            foreach (var releaseElement in document.RootElement.EnumerateArray())
            {
                var tagName = releaseElement.GetProperty("tag_name").GetString();
                var isPreRelease = releaseElement.GetProperty("prerelease").GetBoolean();
                var isDraft = releaseElement.GetProperty("draft").GetBoolean();

                // Skip drafts
                if (isDraft)
                {
                    continue;
                }

                // Skip pre-releases if not including them
                if (isPreRelease && !this.includePreRelease)
                {
                    continue;
                }

                // Parse the tag as a semantic version
                if (!SemanticVersion.TryParse(tagName, out var semanticVersion))
                {
                    continue;
                }

                // Find matching asset
                string? assetUrl = null;
                if (releaseElement.TryGetProperty("assets", out var assetsElement))
                {
                    foreach (var asset in assetsElement.EnumerateArray())
                    {
                        var assetName = asset.GetProperty("name").GetString();
                        if (assetName != null && assetPattern.IsMatch(assetName))
                        {
                            assetUrl = asset.GetProperty("browser_download_url").GetString();
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(assetUrl))
                {
                    this._releasesCache[semanticVersion] = new GitHubRelease(tagName!, assetUrl, isPreRelease);
                    if (this.verbose)
                    {
                        AnsiConsole.MarkupLine($"[grey]Found release:[/] [green]{semanticVersion}[/] (Pre-release: {isPreRelease})");
                    }
                }
            }
        }

        /// <summary>
        /// Represents a GitHub release with its associated metadata.
        /// </summary>
        private sealed record GitHubRelease(string TagName, string AssetUrl, bool IsPreRelease);
    }
}
