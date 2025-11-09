// <copyright file="Device.cs">
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IniParser;
using IniParser.Model;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleToolkit.Commands.Config
{
    public class SetConfigCommand : AsyncCommand<SetConfigSettings>
    {
        public override async Task<int> ExecuteAsync(CommandContext context, SetConfigSettings settings, CancellationToken cancellationToken)
        {
            string configPath;
            if (settings.Global)
            {
                configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ConsoleToolkit",
                    "ct.config");
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            }
            else
            {
                configPath = Path.Combine(Environment.CurrentDirectory, "ct.config");
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            }

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

            AnsiConsole.MarkupLine($"[green]Set {key}{(section != null ? $"in [[{section}]]" : string.Empty)} to '{value}' in {(settings.Global ? "user" : "local")} config.[/]");
            return 0;
        }
    }

    public class SetConfigSettings : CommandSettings
    {
        [CommandOption("--global")]
        [Description("Write to the global config location for the currently logged in user.")]
        public bool Global { get; set; }

        [CommandArgument(1, "<key>")]
        [Description("The config key to set")]
        public string Key { get; set; } = string.Empty;

        [CommandArgument(0, "[section]")]
        [Description("The config section the key belongs to (optional)")]
        public string? Section { get; set; }

        [CommandArgument(2, "<value>")]
        [Description("The new config value")]
        public string Value { get; set; } = string.Empty;
    }
}