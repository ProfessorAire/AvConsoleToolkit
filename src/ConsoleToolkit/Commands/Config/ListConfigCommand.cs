// <copyright file="ListConfigCommand.cs">
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using ConsoleToolkit.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleToolkit.Commands.Config
{
    /// <summary>
    /// Command to list merged application configuration values.
    /// Displays top-level (global) settings and nested section properties, and optionally
    /// shows the file paths of configuration sources in order of precedence.
    /// </summary>
    public class ListConfigCommand : Command<ListConfigSettings>
    {
        /// <summary>
        /// Executes the list-config command.
        /// Uses reflection to enumerate properties on <see cref="AppConfig.Settings"/>, grouping
        /// nested interface-typed settings as sections and printing their property values.
        /// </summary>
        /// <param name="context">The command execution context provided by Spectre.Console.Cli.</param>
        /// <param name="settings">The settings provided by the user on the command line.</param>
        /// <param name="cancellationToken">A token that can be used to cancel the operation. Not currently observed.</param>
        /// <returns>Always returns 0 on completion.</returns>
        /// <exception cref="IOException">I/O errors when checking for the presence of configuration files.</exception>
        /// <exception cref="UnauthorizedAccessException">Insufficient permissions while accessing configuration file locations.</exception>
        public override int Execute(CommandContext context, ListConfigSettings settings, CancellationToken cancellationToken)
        {
            var config = AppConfig.Settings;
            var output = new StringBuilder();
            var globalProperties = new List<(string Name, string Value)>();
            var sections = new Dictionary<string, List<(string Name, string Value)>>();

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[teal bold]Merged Configurations[/]");
            AnsiConsole.WriteLine();

            // Use reflection to get all properties from ISettings
            var settingsType = config.GetType();
            var properties = settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var value = prop.GetValue(config);

                // Check if this is a nested settings interface (e.g., IConnectionSettings)
                if (value != null && prop.PropertyType.IsInterface)
                {
                    var sectionName = prop.Name;
                    var sectionProps = new List<(string Name, string Value)>();
                    var nestedProps = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

                    foreach (var nestedProp in nestedProps)
                    {
                        var nestedValue = nestedProp.GetValue(value);
                        var displayValue = nestedValue?.ToString() ?? string.Empty;
                        sectionProps.Add((nestedProp.Name, displayValue));
                    }

                    sections[sectionName] = sectionProps;
                }
                else
                {
                    // Top-level property (no section)
                    var displayValue = value?.ToString() ?? string.Empty;
                    globalProperties.Add((prop.Name, displayValue));
                }
            }

            // Output global properties first (if any)
            if (globalProperties.Count > 0)
            {
                foreach (var (name, value) in globalProperties)
                {
                    output.AppendLine($"[cyan]{name.EscapeMarkup()}[/] = [green]{value.EscapeMarkup()}[/]");
                }
                output.AppendLine();
            }

            // Output sections
            foreach (var section in sections.OrderBy(s => s.Key))
            {
                output.AppendLine($"[yellow][[{section.Key.EscapeMarkup()}]][/]");
                foreach (var (name, value) in section.Value)
                {
                    output.AppendLine($"[cyan]{name.EscapeMarkup()}[/] = [green]{value.EscapeMarkup()}[/]");
                }
                output.AppendLine();
            }

            if (output.Length == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No configuration properties found.[/]");
                return 0;
            }

            AnsiConsole.Markup(output.ToString());

            // Show config file locations
            if (settings.ShowSources)
            {
                var userPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ConsoleToolkit",
                    "ct.config");
                var localPath = Path.Combine(Environment.CurrentDirectory, "ct.config");

                AnsiConsole.MarkupLine("[bold]Configuration Sources (in order of precedence):[/]");
                AnsiConsole.MarkupLine($"  [dim]1. Local:[/]  {localPath} {(File.Exists(localPath) ? "[green](exists)[/]" : "[dim](not found)[/]")}");
                AnsiConsole.MarkupLine($"  [dim]2. User:[/]   {userPath} {(File.Exists(userPath) ? "[green](exists)[/]" : "[dim](not found)[/]")}");
                AnsiConsole.MarkupLine($"  [dim]3. Built-in defaults[/]");
            }

            return 0;
        }
    }
}
