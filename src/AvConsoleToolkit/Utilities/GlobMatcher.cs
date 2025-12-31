// <copyright file="GlobMatcher.cs">
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
using System.Linq;
using System.Text.RegularExpressions;

namespace AvConsoleToolkit.Utilities
{
    /// <summary>
    /// Provides glob pattern matching functionality for file paths.
    /// Supports wildcards: * (any characters except /), ** (any characters including /), ? (single character), [abc] (character class), [!abc] (negated character class).
    /// </summary>
    internal static partial class GlobMatcher
    {
        /// <summary>
        /// Filters a collection of paths to those matching the glob pattern.
        /// </summary>
        /// <param name="pattern">The glob pattern to match against.</param>
        /// <param name="paths">The paths to filter.</param>
        /// <param name="caseSensitive">Whether the match should be case-sensitive (default: false).</param>
        /// <returns>An enumerable of paths that match the pattern.</returns>
        public static IEnumerable<string> FilterMatches(string pattern, IEnumerable<string> paths, bool caseSensitive = false)
        {
            return paths.Where(path => IsMatch(pattern, path, caseSensitive));
        }

        /// <summary>
        /// Checks if a path matches a glob pattern.
        /// </summary>
        /// <param name="pattern">The glob pattern to match against.</param>
        /// <param name="path">The path to test.</param>
        /// <param name="caseSensitive">Whether the match should be case-sensitive (default: false).</param>
        /// <returns>True if the path matches the pattern; otherwise, false.</returns>
        public static bool IsMatch(string pattern, string path, bool caseSensitive = false)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                throw new ArgumentNullException(nameof(pattern));
            }

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            // Normalize path separators to forward slashes
            path = path.Replace('\\', '/');
            pattern = pattern.Replace('\\', '/');

            var regexPattern = ConvertGlobToRegex(pattern);
            var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

            return Regex.IsMatch(path, regexPattern, options);
        }

        /// <summary>
        /// Converts a glob pattern to a regular expression pattern.
        /// </summary>
        /// <param name="glob">The glob pattern.</param>
        /// <returns>A regex pattern string.</returns>
        private static string ConvertGlobToRegex(string glob)
        {
            var regex = new System.Text.StringBuilder();
            regex.Append('^');

            var i = 0;
            while (i < glob.Length)
            {
                var c = glob[i];

                switch (c)
                {
                    case '*':

                        // Check for ** (matches across directory boundaries)
                        if (i + 1 < glob.Length && glob[i + 1] == '*')
                        {
                            // Check if ** is followed by a path separator
                            if (i + 2 < glob.Length && (glob[i + 2] == '/' || glob[i + 2] == '\\'))
                            {
                                // **/ pattern - match zero or more path segments
                                regex.Append("(?:.*/)?");
                                i += 2; // Skip ** and the separator
                            }
                            else if (i + 2 == glob.Length)
                            {
                                // ** at end of pattern - match everything
                                regex.Append(".*");
                                i++; // Skip the second *
                            }
                            else
                            {
                                // ** in the middle but not followed by separator
                                regex.Append(".*");
                                i++; // Skip the second *
                            }
                        }
                        else
                        {
                            // Single * matches everything except /
                            regex.Append("[^/]*");
                        }
                        break;

                    case '?':

                        // ? matches any single character except /
                        regex.Append("[^/]");
                        break;

                    case '[':

                        // Character class [abc] or negated [!abc] or ranges [a-z]
                        var endBracket = glob.IndexOf(']', i + 1);
                        if (endBracket == -1)
                        {
                            // No closing bracket, treat as literal
                            regex.Append(Regex.Escape("["));
                        }
                        else
                        {
                            var charClass = glob.Substring(i + 1, endBracket - i - 1);

                            // Handle empty character class - treat as literal
                            if (string.IsNullOrEmpty(charClass))
                            {
                                regex.Append(Regex.Escape("[]"));
                                i = endBracket;
                            }
                            // Handle negation: [!abc] becomes [^abc]
                            else if (charClass.StartsWith('!'))
                            {
                                // Handle [!] which is just a negation with nothing - treat as literal
                                if (charClass.Length == 1)
                                {
                                    regex.Append(Regex.Escape("[!]"));
                                    i = endBracket;
                                }
                                else
                                {
                                    regex.Append("[^");

                                    // Don't escape - ranges like [!a-z] need the dash preserved
                                    regex.Append(charClass[1..]);
                                    regex.Append(']');
                                    i = endBracket;
                                }
                            }
                            else
                            {
                                regex.Append('[');

                                // Don't escape - ranges like [a-z] need the dash preserved
                                regex.Append(charClass);
                                regex.Append(']');
                                i = endBracket;
                            }
                        }
                        break;

                    default:

                        // Escape special regex characters
                        regex.Append(Regex.Escape(c.ToString()));
                        break;
                }

                i++;
            }

            regex.Append('$');
            return regex.ToString();
        }
    }
}
