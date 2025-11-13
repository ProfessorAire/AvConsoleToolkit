// <copyright file="SetConfigCommand.cs">
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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IniParser;
using IniParser.Model;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Config
{
    /// <summary>
    /// Command to set a configuration value in either the local or per-user configuration file.
    /// The command will create the configuration file and any necessary directories if they do not exist.
    /// </summary>
    public class SetConfigCommand : AsyncCommand<SetConfigSettings>
    {
        /// <summary>
        /// Executes the set-config command.
        /// Determines the target config file (local or user), loads or creates an INI file,
        /// updates the specified key (optionally within a section), and writes the file back to disk.
        /// </summary>
        /// <param name="context">The command execution context provided by Spectre.Console.Cli.</param>
        /// <param name="settings">The settings provided by the user on the command line.</param>
        /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
        /// <returns>An exit code: 0 for success; non-zero for error.</returns>
        /// <exception cref="ArgumentException">Invalid key or section names may cause an <see cref="ArgumentException"/> when writing the INI file path.</exception>
        /// <exception cref="IOException">I/O errors while reading or writing the configuration file.</exception>
        /// <exception cref="UnauthorizedAccessException">Insufficient permissions to create or write the configuration file or directory.</exception>
        /// <exception cref="OperationCanceledException">The operation was cancelled via <paramref name="cancellationToken"/>.</exception>
        public override async Task<int> ExecuteAsync(CommandContext context, SetConfigSettings settings, CancellationToken cancellationToken)
        {
            // Validate that the requested key/section exists in the current application settings.
            var sectionName = settings.Section;
            var keyName = settings.Key;

            if (!string.IsNullOrWhiteSpace(sectionName))
            {
                var settingsType = Configuration.AppConfig.Settings.GetType();
                var sectionProp = settingsType.GetProperty(sectionName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (sectionProp == null)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] No settings section named '{sectionName}' exists in application settings.");
                    return 1;
                }

                var nestedType = sectionProp.PropertyType;
                var keyProp = nestedType.GetProperty(keyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (keyProp == null)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] No key named '{keyName}' exists in settings section '{sectionName}'.");
                    return 1;
                }
            }
            else
            {
                var settingsType = Configuration.AppConfig.Settings.GetType();
                var keyProp = settingsType.GetProperty(keyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (keyProp == null)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] No global key named '{keyName}' exists in application settings.");
                    return 1;
                }
            }

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

            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

            var parser = new FileIniDataParser();
            IniData data;

            // Load existing config or create new
            if (File.Exists(configPath))
            {
                data = parser.ReadFile(configPath);
            }
            else
            {
                data = new IniData();
            }

            var section = settings.Section;
            var key = settings.Key;
            var value = settings.Value;

            // Update or add the key-value pair
            if (!string.IsNullOrEmpty(section))
            {
                if (!data.Sections.ContainsSection(section))
                {
                    data.Sections.AddSection(section);
                }

                data[section][key] = value;
            }
            else
            {
                data.Global[key] = value;
            }

            // Write back to file
            await Task.Run(() => parser.WriteFile(configPath, data), cancellationToken);

            AnsiConsole.MarkupLine($"[green]Set {key}{(section != null ? $" in [[{section}]]" : string.Empty)} to '{value}' in {(settings.Local ? "local" : "user")} config.[/]");
            return 0;
        }
    }
}