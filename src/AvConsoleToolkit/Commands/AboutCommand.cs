// <copyright file="AboutCommand.cs">
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
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands
{
    /// <summary>
    /// Command that displays information about the application including version, license, and third-party licenses.
    /// </summary>
    public class AboutCommand : AsyncCommand<AboutSettings>
    {
        /// <summary>
        /// Name of the embedded LICENSE resource.
        /// </summary>
        private const string LicenseResourceName = "AvConsoleToolkit.LICENSE";

        /// <summary>
        /// Name of the third-party licenses file to look for alongside the executable.
        /// </summary>
        private const string ThirdPartyLicensesFileName = "THIRD_PARTY_LICENSES.md";

        /// <summary>
        /// Executes the about command, displaying application information and optional license details.
        /// </summary>
        /// <param name="context">The command execution context.</param>
        /// <param name="settings">Command settings controlling what information to display.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Exit code 0 on success.</returns>
        public override async Task<int> ExecuteAsync(CommandContext context, AboutSettings settings, CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // Make method async-compatible

            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version?.ToString() ?? "Unknown";
            var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Christopher McNeely";
            var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "AvConsoleToolkit";
            var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "Application for performing Audio/Visual tasks from the console.";

            // Display header
            var rule = new Rule($"[yellow]{product}[/]")
            {
                Style = Style.Parse("yellow")
            };
            AnsiConsole.Write(rule);
            AnsiConsole.WriteLine();

            // Display basic information
            var infoTable = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[cyan]Property[/]")
                .AddColumn("[white]Value[/]");

            infoTable.AddRow("Version", $"[green]{infoVersion}[/]");
            infoTable.AddRow("Description", description.EscapeMarkup());
            infoTable.AddRow("Copyright", copyright.EscapeMarkup());

            AnsiConsole.Write(infoTable);
            AnsiConsole.WriteLine();

            // Show program license if requested
            if (settings.ShowLicense)
            {
                DisplayProgramLicense(assembly);
            }

            // Show third-party licenses if requested
            if (settings.ShowLicenses)
            {
                DisplayThirdPartyLicenses();
            }

            // If no specific flags, show hint
            if (!settings.ShowLicense && !settings.ShowLicenses)
            {
                AnsiConsole.MarkupLine("[dim]Use --license to view the program license[/]");
                AnsiConsole.MarkupLine("[dim]Use --licenses to view third-party package licenses[/]");
            }

            return 0;
        }

        /// <summary>
        /// Displays the program's license from the embedded LICENSE resource.
        /// </summary>
        /// <param name="assembly">Assembly to read the embedded resource from.</param>
        private static void DisplayProgramLicense(Assembly assembly)
        {
            AnsiConsole.WriteLine();
            var licenseRule = new Rule("[yellow]Program License[/]")
            {
                Style = Style.Parse("yellow")
            };
            AnsiConsole.Write(licenseRule);
            AnsiConsole.WriteLine();

            try
            {
                using var stream = assembly.GetManifestResourceStream(LicenseResourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var licenseText = reader.ReadToEnd();

                    var panel = new Panel(licenseText.EscapeMarkup())
                    {
                        Border = BoxBorder.Rounded,
                        Padding = new Padding(1, 1, 1, 1)
                    };

                    AnsiConsole.Write(panel);
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]License file not found in embedded resources.[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error reading license: {ex.Message.EscapeMarkup()}[/]");
            }

            AnsiConsole.WriteLine();
        }

        /// <summary>
        /// Displays third-party licenses from the THIRD_PARTY_LICENSES.md file if it exists.
        /// </summary>
        private static void DisplayThirdPartyLicenses()
        {
            AnsiConsole.WriteLine();
            var licensesRule = new Rule("[yellow]Third-Party Licenses[/]")
            {
                Style = Style.Parse("yellow")
            };
            AnsiConsole.Write(licensesRule);
            AnsiConsole.WriteLine();

            // Look for THIRD_PARTY_LICENSES.md alongside the executable
            var executablePath = AppContext.BaseDirectory;
            var licensesPath = Path.Combine(executablePath, ThirdPartyLicensesFileName);

            if (File.Exists(licensesPath))
            {
                try
                {
                    var licensesText = File.ReadAllText(licensesPath);

                    // Create table
                    var table = new Table()
                        .Border(TableBorder.Rounded)
                        .AddColumn(new TableColumn("[cyan]Package Name[/]").Width(40))
                        .AddColumn(new TableColumn("[green]License Type[/]").Width(20))
                        .AddColumn(new TableColumn("[blue]License URL[/]").Width(60));

                    // Parse the licenses
                    var lines = licensesText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

                    string? currentPackage = null;
                    string? currentLicense = null;
                    string? currentUrl = null;
                    string? generatedInfo = null;

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("# "))
                        {
                            // Main header - skip it
                            continue;
                        }
                        else if (line.StartsWith("Generated:"))
                        {
                            generatedInfo = line;
                            continue;
                        }
                        else if (line.StartsWith("## "))
                        {
                            // If we have a pending package, add it to the table
                            if (currentPackage != null)
                            {
                                table.AddRow(
                                    currentPackage.EscapeMarkup(),
                                    currentLicense?.EscapeMarkup() ?? "[dim]Not specified[/]",
                                    currentUrl?.EscapeMarkup() ?? "[dim]-[/]"
                                );
                            }

                            // Start new package
                            currentPackage = line.Substring(3).Trim();
                            currentLicense = null;
                            currentUrl = null;
                        }
                        else if (line.StartsWith("**License Type:**"))
                        {
                            currentLicense = line.Replace("**License Type:**", string.Empty).Trim();
                        }
                        else if (line.StartsWith("**License URL:**"))
                        {
                            var urlLine = line.Replace("**License URL:**", string.Empty).Trim();

                            // Extract URL from markdown link format [text](url)
                            if (urlLine.Contains("](") && urlLine.Contains(")"))
                            {
                                var startIdx = urlLine.IndexOf("](") + 2;
                                var endIdx = urlLine.IndexOf(")", startIdx);
                                if (endIdx > startIdx)
                                {
                                    currentUrl = urlLine.Substring(startIdx, endIdx - startIdx);
                                }
                            }
                            else
                            {
                                currentUrl = urlLine;
                            }
                        }
                    }

                    // Add the last package if there is one
                    if (currentPackage != null)
                    {
                        table.AddRow(
                            currentPackage.EscapeMarkup(),
                            currentLicense?.EscapeMarkup() ?? "[dim]Not specified[/]",
                            currentUrl?.EscapeMarkup() ?? "[dim]-[/]"
                        );
                    }

                    // Display generated info if available
                    if (generatedInfo != null)
                    {
                        AnsiConsole.MarkupLine($"[dim]{generatedInfo.EscapeMarkup()}[/]");
                        AnsiConsole.WriteLine();
                    }

                    AnsiConsole.Write(table);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error reading third-party licenses: {ex.Message.EscapeMarkup()}[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]Third-party licenses file not found at:[/]");
                AnsiConsole.MarkupLine($"[dim]{licensesPath.EscapeMarkup()}[/]");
            }

            AnsiConsole.WriteLine();
        }
    }
}
