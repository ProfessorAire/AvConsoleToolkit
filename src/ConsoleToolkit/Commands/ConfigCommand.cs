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

using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToolkit.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleToolkit.Commands
{
    public class ConfigCommand : AsyncCommand<ConfigSettings>
    {
        private readonly ConfigManager configManager;

        public ConfigCommand(ConfigManager configManager)
        {
            this.configManager = configManager;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ConfigSettings settings, CancellationToken cancellationToken)
        {
            if (settings.Value == null)
            {
                var val = this.configManager.GetConfigValue(settings.Key);
                if (val is List<string> arr)
                {
                    AnsiConsole.MarkupLine($"[green]{settings.Key}:[/] {string.Join(", ", arr)}");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[green]{settings.Key}:[/] {val}");
                }
            }
            else
            {
                var scope = settings.Global ? ConfigScope.Global
                    : settings.User ? ConfigScope.User
                    : ConfigScope.Local;
                await this.configManager.SetConfigValueAsync(scope, settings.Key, settings.Value);
                AnsiConsole.MarkupLine($"Set [yellow]{settings.Key}[/] in [blue]{scope}[/] config.");
            }
            return 0;
        }
    }

    public class ConfigSettings : CommandSettings
    {
        [CommandOption("--global")]
        [Description("Set value in global config")]
        public bool Global { get; set; }

        [CommandArgument(0, "<key>")]
        [Description("The config key (section.key)")]
        public string Key { get; set; } = string.Empty;

        [CommandOption("--local")]
        [Description("Set value in local config")]
        public bool Local { get; set; }

        [CommandOption("--user")]
        [Description("Set value in user config")]
        public bool User { get; set; }

        [CommandArgument(1, "[value]")]
        [Description("The value to set (optional)")]
        public string? Value { get; set; }
    }
}
