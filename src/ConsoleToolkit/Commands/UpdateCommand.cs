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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Onova;
using Onova.Exceptions;
using Onova.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleToolkit.Commands
{
    public class UpdateCommand : AsyncCommand<UpdateSettings>
    {
        public override async Task<int> ExecuteAsync(CommandContext context, UpdateSettings settings, CancellationToken cancellationToken)
        {
            try
            {
                AnsiConsole.MarkupLine("[bold teal]ConsoleToolkit Update Checker[/]");
                AnsiConsole.WriteLine();

                // Get current version from assembly
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version!;
                AnsiConsole.MarkupLine($"[cyan]Current version:[/] [green]{currentVersion}[/]");

                // Create local and GitHub package resolvers
                var localVersionsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "versions");
                var localResolver = new LocalPackageResolver(localVersionsPath, "ConsoleToolkit-*.zip");

                //var githubResolver = new GithubPackageResolver("ProfessorAire", "ConsoleToolkit", "ConsoleToolkit-*.zip");
                var webResolver = new WebPackageResolver("http://evands.com/programs/ct/manifest.txt");

                // Create aggregate resolver
                var aggregateResolver = new AggregatePackageResolver(
                [localResolver, webResolver]); //githubResolver]);

                AnsiConsole.MarkupLineInterpolated($"[grey]Checking for updates from local versions:[/] [fuchsia]'{localVersionsPath}'[/]");
                AnsiConsole.MarkupLineInterpolated($"[grey]Checking for updates from GitHub repository:[/] [teal]'ProfessorAire/ConsoleToolkit'[/]");

                // Create update manager with ZIP extractor
                using var manager = new UpdateManager(aggregateResolver, new ZipPackageExtractor());

                // Check for updates
                Version? latestVersion = null;
                await AnsiConsole.Status()
          .StartAsync("Checking for updates...", async ctx =>
          {
              try
              {
                  var result = await manager.CheckForUpdatesAsync();
                  if (result.CanUpdate && result.LastVersion != null)
                  {
                      latestVersion = result.LastVersion;
                  }
              }
              catch (Exception ex)
              {
                  AnsiConsole.MarkupLineInterpolated($"[red]!Failed to check for updates.\r\n{ex}[/]");

                  // Silently handle check errors
              }
          });

                if (latestVersion == null)
                {
                    AnsiConsole.MarkupLine("[green]✓[/] You are already on the latest version!");
                    return 0;
                }

                AnsiConsole.MarkupLine($"[cyan]Latest version:[/]  [green]{latestVersion}[/]");

                if (currentVersion >= latestVersion)
                {
                    AnsiConsole.MarkupLine("[green]✓[/] You are already on the latest version!");
                    return 0;
                }

                AnsiConsole.WriteLine();

                // Prompt user to update
                if (!settings.AutoConfirm)
                {
                    var confirm = AnsiConsole.Confirm($"Update to version [green]{latestVersion}[/]?", defaultValue: true);
                    if (!confirm)
                    {
                        AnsiConsole.MarkupLine("[yellow]Update cancelled.[/]");
                        return 0;
                    }
                }

                // Prepare update with progress
                await AnsiConsole.Progress()
                    .AutoClear(false)
                    .Columns(
                 new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new SpinnerColumn())
                    .StartAsync(async ctx =>
                        {
                            var downloadTask = ctx.AddTask("[green]Downloading update[/]");

                              await manager.PrepareUpdateAsync(latestVersion, new Progress<double>(p =>
                                  {
                                      downloadTask.Value = p * 100;
                                  }), cancellationToken);

                            downloadTask.Value = 100;
                            downloadTask.StopTask();
                        });

                AnsiConsole.MarkupLine("[green]✓[/] Update downloaded and ready to install!");
                AnsiConsole.MarkupLine("[yellow]![/] The application will now restart to apply the update.");
                AnsiConsole.WriteLine();

                if (!settings.AutoConfirm)
                {
                    AnsiConsole.Prompt(
               new ConfirmationPrompt("Press [green]Enter[/] to install the update or 'n' to cancel...")
                    {
                        DefaultValue = true,
                        ShowDefaultValue = false,
                        ShowChoices = false
                    });
                }

                // Launch updater (this will restart the application)
                manager.LaunchUpdater(latestVersion, false);

                return 0;
            }
            catch (UpdaterAlreadyLaunchedException)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Update is already in progress.");
                return 1;
            }
            catch (LockFileNotAcquiredException)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Another instance of the updater is running.");
                return 1;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                if (settings.Verbose)
                {
                    AnsiConsole.WriteException(ex);
                }
                return 1;
            }
        }
    }

    public class UpdateSettings : CommandSettings
    {
        [CommandOption("-y|--yes")]
        [Description("Automatically confirm all prompts")]
        public bool AutoConfirm { get; set; }

        [CommandOption("-v|--verbose")]
        [Description("Show detailed error information")]
        public bool Verbose { get; set; }
    }
}
