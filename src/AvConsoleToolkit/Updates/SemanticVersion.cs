// <copyright file="SemanticVersion.cs">
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
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AvConsoleToolkit.Updates
{
    /// <summary>
    /// Represents a semantic version with support for pre-release labels and build metadata.
    /// Implements comparison according to Semantic Versioning 2.0.0 specification.
    /// </summary>
    public sealed partial class SemanticVersion : IComparable<SemanticVersion>, IComparable, IEquatable<SemanticVersion>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticVersion"/> class.
        /// </summary>
        /// <param name="major">The major version number.</param>
        /// <param name="minor">The minor version number.</param>
        /// <param name="patch">The patch version number.</param>
        /// <param name="preRelease">The pre-release label, or <see langword="null"/> for stable releases.</param>
        /// <param name="buildMetadata">The build metadata, or <see langword="null"/> if not specified.</param>
        public SemanticVersion(int major, int minor, int patch, string? preRelease = null, string? buildMetadata = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(major);
            ArgumentOutOfRangeException.ThrowIfNegative(minor);
            ArgumentOutOfRangeException.ThrowIfNegative(patch);

            this.Major = major;
            this.Minor = minor;
            this.Patch = patch;
            this.PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease.Trim();
            this.BuildMetadata = string.IsNullOrWhiteSpace(buildMetadata) ? null : buildMetadata.Trim();
        }

        /// <summary>
        /// Gets the build metadata, or <see langword="null"/> if not specified.
        /// Build metadata is ignored in version comparisons.
        /// </summary>
        public string? BuildMetadata { get; }

        /// <summary>
        /// Gets a value indicating whether this is a pre-release version.
        /// </summary>
        public bool IsPreRelease => !string.IsNullOrEmpty(this.PreRelease);

        /// <summary>
        /// Gets the major version number.
        /// </summary>
        public int Major { get; }

        /// <summary>
        /// Gets the minor version number.
        /// </summary>
        public int Minor { get; }

        /// <summary>
        /// Gets the patch version number.
        /// </summary>
        public int Patch { get; }

        /// <summary>
        /// Gets the pre-release label (e.g., "alpha", "beta.1", "rc.2"), or <see langword="null"/> if this is a stable release.
        /// </summary>
        public string? PreRelease { get; }

        /// <summary>
        /// Determines whether one <see cref="SemanticVersion"/> is greater than or equal to another.
        /// </summary>
        public static bool operator >=(SemanticVersion? left, SemanticVersion? right)
        {
            if (left is null)
            {
                return right is null;
            }

            return left.CompareTo(right) >= 0;
        }

        /// <summary>
        /// Determines whether one <see cref="SemanticVersion"/> is greater than another.
        /// </summary>
        public static bool operator >(SemanticVersion? left, SemanticVersion? right)
        {
            if (left is null)
            {
                return false;
            }

            return left.CompareTo(right) > 0;
        }

        /// <summary>
        /// Determines whether two <see cref="SemanticVersion"/> instances are equal.
        /// </summary>
        public static bool operator ==(SemanticVersion? left, SemanticVersion? right)
        {
            if (left is null)
            {
                return right is null;
            }

            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether one <see cref="SemanticVersion"/> is less than or equal to another.
        /// </summary>
        public static bool operator <=(SemanticVersion? left, SemanticVersion? right)
        {
            if (left is null)
            {
                return true;
            }

            return left.CompareTo(right) <= 0;
        }

        /// <summary>
        /// Determines whether one <see cref="SemanticVersion"/> is less than another.
        /// </summary>
        public static bool operator <(SemanticVersion? left, SemanticVersion? right)
        {
            if (left is null)
            {
                return right is not null;
            }

            return left.CompareTo(right) < 0;
        }

        /// <summary>
        /// Determines whether two <see cref="SemanticVersion"/> instances are not equal.
        /// </summary>
        public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

        /// <summary>
        /// Parses a semantic version string.
        /// </summary>
        /// <param name="versionString">The version string to parse.</param>
        /// <returns>The parsed <see cref="SemanticVersion"/>.</returns>
        /// <exception cref="FormatException">The version string is not a valid semantic version.</exception>
        public static SemanticVersion Parse(string versionString)
        {
            if (!TryParse(versionString, out var version))
            {
                throw new FormatException($"'{versionString}' is not a valid semantic version.");
            }

            return version;
        }

        /// <summary>
        /// Attempts to parse a semantic version string.
        /// </summary>
        /// <param name="versionString">The version string to parse (e.g., "1.2.3", "1.2.3-beta.1", "v1.2.3-rc.1+build.123").</param>
        /// <param name="version">When successful, contains the parsed <see cref="SemanticVersion"/>.</param>
        /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
        public static bool TryParse(string? versionString, [NotNullWhen(true)] out SemanticVersion? version)
        {
            version = null;

            if (string.IsNullOrWhiteSpace(versionString))
            {
                return false;
            }

            // Remove leading 'v' or 'V' if present
            var input = versionString.Trim();
            if (input.StartsWith('v') || input.StartsWith('V'))
            {
                input = input[1..];
            }

            var match = SemanticVersionRegex().Match(input);
            if (!match.Success)
            {
                return false;
            }

            if (!int.TryParse(match.Groups["major"].Value, out var major) ||
                !int.TryParse(match.Groups["minor"].Value, out var minor) ||
                !int.TryParse(match.Groups["patch"].Value, out var patch))
            {
                return false;
            }

            var preRelease = match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : null;
            var buildMetadata = match.Groups["buildmetadata"].Success ? match.Groups["buildmetadata"].Value : null;

            version = new SemanticVersion(major, minor, patch, preRelease, buildMetadata);
            return true;
        }

        /// <inheritdoc/>
        public int CompareTo(SemanticVersion? other)
        {
            if (other is null)
            {
                return 1;
            }

            // Compare major, minor, patch
            var result = this.Major.CompareTo(other.Major);
            if (result != 0)
            {
                return result;
            }

            result = this.Minor.CompareTo(other.Minor);
            if (result != 0)
            {
                return result;
            }

            result = this.Patch.CompareTo(other.Patch);
            if (result != 0)
            {
                return result;
            }

            // Pre-release versions have lower precedence than normal versions
            // 1.0.0-alpha < 1.0.0
            if (this.IsPreRelease && !other.IsPreRelease)
            {
                return -1;
            }

            if (!this.IsPreRelease && other.IsPreRelease)
            {
                return 1;
            }

            // Both are pre-release or both are stable
            if (!this.IsPreRelease && !other.IsPreRelease)
            {
                return 0;
            }

            // Compare pre-release identifiers
            return ComparePreRelease(this.PreRelease!, other.PreRelease!);
        }

        /// <inheritdoc/>
        public int CompareTo(object? obj)
        {
            if (obj is null)
            {
                return 1;
            }

            if (obj is SemanticVersion other)
            {
                return this.CompareTo(other);
            }

            throw new ArgumentException($"Object must be of type {nameof(SemanticVersion)}.", nameof(obj));
        }

        /// <inheritdoc/>
        public bool Equals(SemanticVersion? other)
        {
            if (other is null)
            {
                return false;
            }

            return this.Major == other.Major &&
                   this.Minor == other.Minor &&
                   this.Patch == other.Patch &&
                   string.Equals(this.PreRelease, other.PreRelease, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => this.Equals(obj as SemanticVersion);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(this.Major, this.Minor, this.Patch, this.PreRelease);

        /// <inheritdoc/>
        public override string ToString()
        {
            var result = $"{this.Major}.{this.Minor}.{this.Patch}";

            if (!string.IsNullOrEmpty(this.PreRelease))
            {
                result += $"-{this.PreRelease}";
            }

            if (!string.IsNullOrEmpty(this.BuildMetadata))
            {
                result += $"+{this.BuildMetadata}";
            }

            return result;
        }

        /// <summary>
        /// Converts this <see cref="SemanticVersion"/> to a <see cref="Version"/> object.
        /// Pre-release information is preserved in the revision component:
        /// - Beta versions (e.g., "1.2.3-beta.3") use the beta number as revision (e.g., 1.2.3.3)
        /// - Stable releases use revision 9999 to ensure they are always considered latest (e.g., 1.2.3.9999)
        /// </summary>
        /// <returns>A <see cref="Version"/> representing the major, minor, patch, and revision components.</returns>
        public Version ToVersion()
        {
            int revision;

            if (!this.IsPreRelease)
            {
                // Stable releases always get the highest revision to ensure they're latest
                revision = 9999;
            }
            else
            {
                // Extract numeric suffix from pre-release label (e.g., "beta.3" -> 3)
                revision = ExtractPreReleaseNumber(this.PreRelease!);
            }

            return new Version(this.Major, this.Minor, this.Patch, revision);
        }

        private static int ComparePreRelease(string left, string right)
        {
            var leftParts = left.Split('.');
            var rightParts = right.Split('.');

            var maxLength = Math.Max(leftParts.Length, rightParts.Length);

            for (var i = 0; i < maxLength; i++)
            {
                // Fewer identifiers = lower precedence
                if (i >= leftParts.Length)
                {
                    return -1;
                }

                if (i >= rightParts.Length)
                {
                    return 1;
                }

                var leftPart = leftParts[i];
                var rightPart = rightParts[i];

                var leftIsNumeric = int.TryParse(leftPart, out var leftNum);
                var rightIsNumeric = int.TryParse(rightPart, out var rightNum);

                int comparison;
                if (leftIsNumeric && rightIsNumeric)
                {
                    // Numeric comparison
                    comparison = leftNum.CompareTo(rightNum);
                }
                else if (leftIsNumeric)
                {
                    // Numeric identifiers have lower precedence than alphanumeric
                    return -1;
                }
                else if (rightIsNumeric)
                {
                    return 1;
                }
                else
                {
                    // Alphanumeric comparison (case-sensitive ASCII sort)
                    comparison = string.Compare(leftPart, rightPart, StringComparison.Ordinal);
                }

                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        /// <summary>
        /// Extracts the numeric portion from a pre-release label.
        /// For example, "beta.3" returns 3, "beta" returns 1.
        /// </summary>
        /// <param name="preRelease">The pre-release label.</param>
        /// <returns>The numeric portion of the pre-release label, or 1 if no number is found.</returns>
        private static int ExtractPreReleaseNumber(string preRelease)
        {
            // Split by '.' and look for numeric parts
            var parts = preRelease.Split('.');

            // Check from the end for a numeric part
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                if (int.TryParse(parts[i], out var number))
                {
                    return number;
                }
            }

            // No numeric suffix found, default to 1
            return 1;
        }

        [GeneratedRegex(@"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+(?<buildmetadata>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$", RegexOptions.Compiled)]
        private static partial Regex SemanticVersionRegex();
    }
}
