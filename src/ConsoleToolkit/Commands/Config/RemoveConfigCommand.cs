// <copyright file="RemoveConfigCommand.cs">
// The MIT License
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IniParser;
using IniParser.Model;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleToolkit.Commands.Config
{
    /// <summary>
    /// Command to remove a configuration key from either the local or per-user configuration file.
    /// The command will remove empty sections when appropriate and writes changes back to disk.
    /// </summary>
    public class RemoveConfigCommand : AsyncCommand<RemoveConfigSettings>
    {
        /// <summary>
        /// Executes the remove-config command.
        /// Locates the target configuration file (local or user), verifies the specified key (and section if provided),
        /// removes the key, optionally removes an empty section, and writes the updated INI file back to disk.
        /// </summary>
        /// <param name="context">The command execution context provided by Spectre.Console.Cli.</param>
        /// <param name="settings">The settings provided by the user on the command line.</param>
        /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
        /// <returns>Exit code 0 on success; non-zero when the requested key or section cannot be found or on error.</returns>
        /// <exception cref="IOException">I/O errors while reading or writing the configuration file.</exception>
        /// <exception cref="UnauthorizedAccessException">Insufficient permissions to read or write the configuration file.</exception>
        /// <exception cref="OperationCanceledException">The operation was cancelled via <paramref name="cancellationToken"/>.</exception>
        public override async Task<int> ExecuteAsync(CommandContext context, RemoveConfigSettings settings, CancellationToken cancellationToken)
        {
            string configPath;
            if (settings.Local)
            {
                configPath = Path.Combine(Environment.CurrentDirectory, "ct.config");
            }
            else
            {
                configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ConsoleToolkit",
                    "ct.config");
            }

            if (!File.Exists(configPath))
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] Config file not found at {configPath}");
                return 1;
            }

            var parser = new FileIniDataParser();
            IniData data = parser.ReadFile(configPath);

            var section = settings.Section;
            var key = settings.Key;
            var removed = false;

            // Remove the key-value pair
            if (!string.IsNullOrEmpty(section))
            {
                if (data.Sections.ContainsSection(section))
                {
                    if (data[section].ContainsKey(key))
                    {
                        data[section].RemoveKey(key);
                        removed = true;

                        // If section is now empty, optionally remove it
                        if (data[section].Count == 0)
                        {
                            data.Sections.RemoveSection(section);
                            AnsiConsole.MarkupLine($"[yellow]Section [[{section}]] was empty and has been removed.[/]");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Key '{key}' not found in section [[{section}]].[/]");
                        return 1;
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Section [[{section}]] not found.[/]");
                    return 1;
                }
            }
            else
            {
                if (data.Global.ContainsKey(key))
                {
                    data.Global.RemoveKey(key);
                    removed = true;
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Key '{key}' not found in global section.[/]");
                    return 1;
                }
            }

            if (removed)
            {
                // Write back to file
                await Task.Run(() => parser.WriteFile(configPath, data), cancellationToken);
                AnsiConsole.MarkupLine($"[green]Removed {key}{(section != null ? $" from [[{section}]]" : string.Empty)} in {(settings.Local ? "local" : "user")} config.[/]");
            }

            return 0;
        }
    }
}
