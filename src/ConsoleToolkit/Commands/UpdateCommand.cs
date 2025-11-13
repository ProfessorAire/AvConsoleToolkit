// <copyright file="UpdateCommand.cs">
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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToolkit.Configuration;
using Onova;
using Onova.Exceptions;
using Onova.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleToolkit.Commands
{
    /// <summary>
    /// Command to check for and apply updates to the ConsoleToolkit application.
    /// Supports local and GitHub-based update sources, and provides interactive or automatic update flows.
    /// </summary>
    public class UpdateCommand : AsyncCommand<UpdateSettings>
    {
        /// <summary>
        /// Executes the update command asynchronously.
        /// Checks for updates, downloads, and applies them if available.
        /// </summary>
        /// <param name="context">The command context.</param>
        /// <param name="settings">Settings for update behavior.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Returns0 if successful,1 if an error occurs.</returns>
        public override async Task<int> ExecuteAsync(CommandContext context, UpdateSettings settings, CancellationToken cancellationToken)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Automatic updates are only supported on Windows at this time.");
                    return 1;
                }

                AnsiConsole.MarkupLine("[bold teal]ConsoleToolkit Update Checker[/]");
                AnsiConsole.WriteLine();

                // Get current version from assembly
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version!;
                AnsiConsole.MarkupLine($"[cyan]Current version:[/] [green]{currentVersion}[/]");

                // Create local and GitHub package resolvers
                var localVersionsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "versions");
                var localResolver = new LocalPackageResolver(localVersionsPath, "ConsoleToolkit-*.zip");

                GithubPackageResolver? githubResolver;

                // Todo: Remove this before a v1.0 public release.
                if (currentVersion.MajorRevision < 1)
                {
                    var token = AppConfig.Settings.GithubToken;
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        token = AnsiConsole.Prompt(new TextPrompt<string>("Enter a GitHub access token with rights to view the ProfessorAire/ConsoleToolkit repo in order to check for pre-release updates from GitHub.\r\nLeave this blank to only check for updates from the program's 'versions' directory:")
                        {
                            AllowEmpty = true
                        });
                    }

                    if (string.IsNullOrWhiteSpace(token))
                    {
                        AnsiConsole.MarkupLine("[yellow]No token provided, will only examine the local versions directory for updates.[/]");
                        githubResolver = null;
                    }
                    else
                    {
                        var httpClient = new HttpClient();
                        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ConsoleToolkit-Updater");
                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        githubResolver = new GithubPackageResolver(httpClient, "ProfessorAire", "ConsoleToolkit", "ConsoleToolkit-*.zip");
                    }
                }
                else
                {
                    githubResolver = new GithubPackageResolver("ProfessorAire", "ConsoleToolkit", "ConsoleToolkit-*.zip");
                }

                IPackageResolver updateResolver = githubResolver is null ? localResolver : githubResolver;

                if (Equals(updateResolver, localResolver))
                {
                    AnsiConsole.MarkupLineInterpolated($"[grey]Checking for updates from local versions:[/] [fuchsia]'{localVersionsPath}'[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[grey]Checking for updates from [/][fuchsia]GitHub.[/]");
                }

                // Create update manager with ZIP extractor
                using var manager = new UpdateManager(localResolver, new ZipPackageExtractor());

                // Check for updates
                Version? latestVersion = null;
                await AnsiConsole.Status()
                  .StartAsync("Checking for updates...", async ctx =>
                  {
                      try
                      {
                          if (OperatingSystem.IsWindows())
                          {
                              var result = await manager.CheckForUpdatesAsync(cancellationToken);
                              if (result.CanUpdate && result.LastVersion != null)
                              {
                                  latestVersion = result.LastVersion;
                              }
                          }
                      }
                      catch (Exception ex)
                      {
                          AnsiConsole.MarkupLineInterpolated($"[red]!Failed to check for updates.\r\n{ex}[/]");
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
                        if (OperatingSystem.IsWindows())
                        {
                            var downloadTask = ctx.AddTask("[green]Downloading update[/]");

                            await manager.PrepareUpdateAsync(latestVersion, new Progress<double>(p =>
                            {
                                downloadTask.Value = p * 100;
                            }), cancellationToken);

                            downloadTask.Value = 100;
                            downloadTask.StopTask();
                        }
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
}
