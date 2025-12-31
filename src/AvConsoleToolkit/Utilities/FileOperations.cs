// <copyright file="FileOperations.cs">
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
using Renci.SshNet.Sftp;
using Spectre.Console;

namespace AvConsoleToolkit.Utilities
{
    /// <summary>
    /// Provides utility methods for file operations with glob pattern support.
    /// </summary>
    public static class FileOperations
    {
        /// <summary>
        /// Checks if a path contains wildcard characters.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns>True if the path contains wildcards; otherwise, false.</returns>
        public static bool ContainsWildcard(string path)
        {
            return path.Contains('*') || path.Contains('?') || path.Contains('[');
        }

        /// <summary>
        /// Finds all files matching a glob pattern on the local file system.
        /// </summary>
        /// <param name="pattern">The glob pattern.</param>
        /// <returns>List of matching file paths.</returns>
        public static List<string> FindMatchingFiles(string pattern)
        {
            // Normalize the pattern
            pattern = pattern.Replace('/', Path.DirectorySeparatorChar);

            // Handle relative path prefixes like ./ and .\
            if (pattern.StartsWith($".{Path.DirectorySeparatorChar}"))
            {
                pattern = pattern.Substring(2);
            }

            // Determine if this is a recursive pattern
            var isRecursive = pattern.Contains($"**{Path.DirectorySeparatorChar}") || pattern.Contains("**");

            // Extract the base directory (the part before any wildcards)
            var baseDirectory = GetBaseDirectoryFromPattern(pattern);

            // If base directory is empty or just ".", use current directory
            if (string.IsNullOrEmpty(baseDirectory) || baseDirectory == ".")
            {
                baseDirectory = Directory.GetCurrentDirectory();
            }
            else if (!Path.IsPathRooted(baseDirectory))
            {
                // Make relative paths absolute
                baseDirectory = Path.GetFullPath(baseDirectory);
            }

            if (!Directory.Exists(baseDirectory))
            {
                return [];
            }

            // Get search option based on pattern
            var searchOption = isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            try
            {
                // Get all files from the base directory
                var allFiles = Directory.GetFiles(baseDirectory, "*", searchOption);

                // Normalize the pattern for matching - make it absolute for comparison
                var absolutePattern = Path.IsPathRooted(pattern) 
                    ? pattern 
                    : Path.GetFullPath(pattern);

                // Convert to forward slashes for glob matching
                var normalizedPattern = absolutePattern.Replace(Path.DirectorySeparatorChar, '/');

                return allFiles
                    .Where(file =>
                    {
                        var normalizedFile = Path.GetFullPath(file).Replace(Path.DirectorySeparatorChar, '/');
                        return GlobMatcher.IsMatch(normalizedPattern, normalizedFile, caseSensitive: false);
                    })
                    .Select(Path.GetFullPath)
                    .ToList();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error searching for files:[/] {ex.Message.EscapeMarkup()}");
                return [];
            }
        }

        /// <summary>
        /// Prompts the user to select one or more files from a list.
        /// </summary>
        /// <param name="files">The list of files to choose from.</param>
        /// <param name="allowMultiple">Whether to allow multiple selections.</param>
        /// <param name="pattern">The original pattern (for display purposes).</param>
        /// <returns>List of selected file paths.</returns>
        public static List<string> PromptForFileSelection(List<string> files, bool allowMultiple, string pattern)
        {
            AnsiConsole.MarkupLine($"[yellow]Found {files.Count} file(s) matching pattern:[/] {pattern.EscapeMarkup()}");
            AnsiConsole.WriteLine();

            // Create table to display files
            var table = new Table();
            table.AddColumn("#");
            table.AddColumn("Filename");
            table.AddColumn("Directory");
            table.AddColumn("Size");
            table.AddColumn("Modified");

            for (var i = 0; i < files.Count; i++)
            {
                var fileInfo = new FileInfo(files[i]);
                var sizeStr = FormatFileSize(fileInfo.Length);

                table.AddRow(
                    (i + 1).ToString(),
                    fileInfo.Name.EscapeMarkup(),
                    fileInfo.DirectoryName?.EscapeMarkup() ?? string.Empty,
                    sizeStr,
                    fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            if (allowMultiple)
            {
                var selections = AnsiConsole.Prompt(
                    new MultiSelectionPrompt<string>()
                        .Title("Select [green]one or more files[/] to process:")
                        .PageSize(10)
                        .MoreChoicesText("[grey](Move up and down to reveal more files)[/]")
                        .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                        .AddChoices(files.Select((f, i) => $"{i + 1}. {Path.GetFileName(f)}")));

                // Map selections back to file paths
                return selections
                    .Select(s =>
                    {
                        var index = int.Parse(s.Split('.')[0]) - 1;
                        return files[index];
                    })
                    .ToList();
            }
            else
            {
                var selection = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a [green]file[/] to process:")
                        .PageSize(10)
                        .MoreChoicesText("[grey](Move up and down to reveal more files)[/]")
                        .AddChoices(files.Select((f, i) => $"{i + 1}. {Path.GetFileName(f)}")));

                // Map selection back to file path
                var selectedIndex = int.Parse(selection.Split('.')[0]) - 1;
                return[files[selectedIndex]];
            }
        }

        /// <summary>
        /// Prompts the user to select one or more files from a provided list of remote files using an interactive
        /// console interface.
        /// </summary>
        /// <remarks>The method displays the available files in a formatted table and uses interactive
        /// prompts to collect user selections. The order of files in the returned list corresponds to the order in
        /// which they appear in the input list. This method is intended for use in console applications and requires
        /// user interaction.</remarks>
        /// <param name="files">The list of remote files available for selection. Cannot be null.</param>
        /// <param name="allowMultiple">true to allow selection of multiple files; otherwise, false to restrict selection to a single file.</param>
        /// <param name="pattern">The search pattern used to filter the displayed files. Displayed to the user for context.</param>
        /// <returns>A list of selected files. The list contains one or more ISftpFile objects chosen by the user. If no files
        /// are selected, the list will be empty.</returns>
        public static List<ISftpFile> PromptForRemoteFileSelection(List<ISftpFile> files, bool allowMultiple, string pattern)
        {
            AnsiConsole.MarkupLine($"[yellow]Found {files.Count} file(s) matching pattern:[/] {pattern.EscapeMarkup()}");
            AnsiConsole.WriteLine();

            if (allowMultiple)
            {
                var selections = AnsiConsole.Prompt(
                    new MultiSelectionPrompt<string>()
                        .Title("Select [green]one or more files[/] to process:")
                        .PageSize(10)
                        .NotRequired()
                        .MoreChoicesText("[grey](Move up and down to reveal more files)[/]")
                        .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                        .AddChoices(files.Select((f, i) => $"{i + 1}. {f.FullName} ({FormatFileSize(f.Length)})")));

                return selections
                    .Select(s =>
                    {
                        var index = int.Parse(s.Split('.')[0]) - 1;
                        return files[index];
                    })
                    .ToList();
            }
            else
            {
                var selection = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a [green]file[/] to process:")
                        .PageSize(10)
                        .MoreChoicesText("[grey](Move up and down to reveal more files)[/]")
                        .AddChoices("Cancel")
                        .AddChoices(files.Select((f, i) => $"{i + 1}. {f.FullName} ({FormatFileSize(f.Length)})")));

                if (selection == "Cancel")
                {
                    return [];
                }

                var selectedIndex = int.Parse(selection.Split('.')[0]) - 1;
                return [files[selectedIndex]];
            }
        }

        /// <summary>
        /// Resolves a file path or glob pattern to one or more matching files.
        /// If a single file is matched, returns it directly.
        /// If multiple files are matched, displays them and prompts for selection.
        /// </summary>
        /// <param name="pathOrPattern">The file path or glob pattern.</param>
        /// <param name="allowMultiple">Whether to allow multiple selections (default: false).</param>
        /// <param name="autoSelectSingle">Whether to automatically select if only one match is found (default: true).</param>
        /// <returns>List of selected file paths, or empty list if cancelled or no matches.</returns>
        public static List<string> ResolveFiles(string pathOrPattern, bool allowMultiple = false, bool autoSelectSingle = true)
        {
            if (string.IsNullOrWhiteSpace(pathOrPattern))
            {
                return [];
            }

            // Check if it's a direct file path (no wildcards)
            if (!ContainsWildcard(pathOrPattern) && File.Exists(pathOrPattern))
            {
                return[Path.GetFullPath(pathOrPattern)];
            }

            // It's a pattern - find matching files
            var matches = FindMatchingFiles(pathOrPattern);

            if (matches.Count == 0)
            {
                AnsiConsole.MarkupLine($"[red]No files found matching pattern:[/] {pathOrPattern.EscapeMarkup()}");
                return [];
            }

            if (matches.Count == 1 && autoSelectSingle)
            {
                return matches;
            }

            // Multiple matches - show them and prompt for selection
            return PromptForFileSelection(matches, allowMultiple, pathOrPattern);
        }

        /// <summary>
        /// Formats a file size in bytes to a human-readable string.
        /// </summary>
        /// <param name="bytes">The size in bytes.</param>
        /// <returns>Formatted size string.</returns>
        private static string FormatFileSize(long bytes)
        {
            string[] sizes = ["B", "KB", "MB", "GB", "TB"];
            double len = bytes;
            var order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Extracts the base directory from a glob pattern (the part before any wildcards).
        /// </summary>
        /// <param name="pattern">The glob pattern.</param>
        /// <returns>The base directory path.</returns>
        private static string GetBaseDirectoryFromPattern(string pattern)
        {
            // Find the first wildcard character
            var wildcardChars = new[] { '*', '?', '[' };
            var firstWildcardIndex = pattern.IndexOfAny(wildcardChars);

            if (firstWildcardIndex == -1)
            {
                // No wildcards - return the directory portion
                return Path.GetDirectoryName(pattern) ?? string.Empty;
            }

            // Find the last directory separator before the first wildcard
            var lastSeparatorBeforeWildcard = pattern.LastIndexOf(Path.DirectorySeparatorChar, firstWildcardIndex);

            if (lastSeparatorBeforeWildcard == -1)
            {
                // No directory separator before wildcard - pattern is in current directory
                return string.Empty;
            }

            return pattern.Substring(0, lastSeparatorBeforeWildcard);
        }
    }
}
