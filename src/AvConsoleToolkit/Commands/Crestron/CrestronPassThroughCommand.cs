// <copyright file="CrestronPassThroughCommand.cs">
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace AvConsoleToolkit.Commands.Crestron
{
    /// <summary>
    /// Crestron-specific implementation of the pass-through command.
    /// Provides interactive SSH sessions tailored for Crestron devices.
    /// </summary>
    public sealed class CrestronPassThroughCommand : PassThroughCommand<PassThroughSettings>
    {
        /// <summary>
        /// Gets the Crestron exit command ("bye").
        /// </summary>
        protected override string ExitCommand => "bye";

        /// <summary>
        /// Handles nested commands (prefixed with ':') by parsing and routing to appropriate Crestron commands.
        /// </summary>
        /// <param name="command">The nested command without the ':' prefix.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the command was handled, false otherwise.</returns>
        protected override async Task<bool> HandleNestedCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            // Parse command into parts
            var parts = ParseCommandLine(command);
            if (parts.Length == 0)
            {
                return false;
            }

            var commandName = parts[0].ToLowerInvariant();

            // Route to appropriate command handler
            return commandName switch
            {
                "program" => await this.HandleProgramCommandAsync(parts.Skip(1).ToArray(), cancellationToken),
                "help" => HandleHelpCommand(),
                _ => HandleUnknownCommand(commandName)
            };
        }

        /// <summary>
        /// Handles tab completion for Crestron devices by forwarding the current buffer and tab to the device.
        /// The base class already sends the buffer + tab, so we just return false to use default behavior.
        /// </summary>
        /// <param name="keyInfo">The console key information.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>False to allow base class to handle tab completion.</returns>
        protected override Task<bool> HandleSpecialKeyAsync(ConsoleKeyInfo keyInfo, CancellationToken cancellationToken)
        {
            // For Crestron, we want the default tab completion behavior
            // which sends the current buffer + tab to the device
            // The device will handle completion and send back results
            return Task.FromResult(false);
        }

        /// <summary>
        /// Called when connected to perform Crestron-specific initialization.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        protected override async Task OnConnectedAsync(CancellationToken cancellationToken)
        {
            // Wait a moment for the device to be ready
            await Task.Delay(100, cancellationToken);

            // Could send any Crestron-specific initialization commands here if needed
            // For example: Set terminal type, configure display options, etc.
        }

        private static bool HandleHelpCommand()
        {
            AnsiConsole.MarkupLine(string.Empty);
            AnsiConsole.MarkupLine("[yellow]Available nested commands:[/]");
            AnsiConsole.MarkupLine("  [cyan]:program upload <file> -s <slot> [options][/]");
            AnsiConsole.MarkupLine("    Upload a program to the device");
            AnsiConsole.MarkupLine("    [dim]Options:[/]");
            AnsiConsole.MarkupLine("      [dim]-s, --slot <slot>         Program slot (1-10)[/]");
            AnsiConsole.MarkupLine("      [dim]-c, --changed-only        Only upload changed files[/]");
            AnsiConsole.MarkupLine("      [dim]-k, --kill                Kill program before upload[/]");
            AnsiConsole.MarkupLine("      [dim]-d, --do-not-start        Don't start program after upload[/]");
            AnsiConsole.MarkupLine("      [dim]--no-zig                  Skip signature file upload[/]");
            AnsiConsole.MarkupLine("      [dim]--no-ip-table             Skip IP table configuration[/]");
            AnsiConsole.MarkupLine(string.Empty);
            AnsiConsole.MarkupLine("  [cyan]:help[/]");
            AnsiConsole.MarkupLine("    Show this help message");
            AnsiConsole.MarkupLine(string.Empty);
            AnsiConsole.MarkupLine("[yellow]Tips:[/]");
            AnsiConsole.MarkupLine("  - Press [green]Tab[/] for command completion on Crestron commands");
            AnsiConsole.MarkupLine("  - Press [green]Up/Down[/] arrows to navigate command history");
            AnsiConsole.MarkupLine("  - Type [green]bye[/] or press [green]Ctrl+X[/] to exit");
            AnsiConsole.MarkupLine(string.Empty);
            return true;
        }

        private static bool HandleUnknownCommand(string commandName)
        {
            AnsiConsole.MarkupLine($"[red]Unknown nested command:[/] {commandName.EscapeMarkup()}");
            AnsiConsole.MarkupLine("[yellow]Type ':help' for a list of available nested commands.[/]");
            return false;
        }

        private static string[] ParseCommandLine(string commandLine)
        {
            var parts = new System.Collections.Generic.List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < commandLine.Length; i++)
            {
                var c = commandLine[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
            {
                parts.Add(current.ToString());
            }

            return parts.ToArray();
        }

        private async Task<bool> HandleProgramCommandAsync(string[] args, CancellationToken cancellationToken)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Program command requires a subcommand (e.g., 'upload')");
                AnsiConsole.MarkupLine("[yellow]Usage:[/] :program upload <file> -s <slot> [options]");
                return false;
            }

            var subcommand = args[0].ToLowerInvariant();

            return subcommand switch
            {
                "upload" or "u" or "load" or "l" => await this.HandleProgramUploadAsync(args.Skip(1).ToArray(), cancellationToken),
                _ => this.HandleUnknownProgramCommand(subcommand)
            };
        }

        private async Task<bool> HandleProgramUploadAsync(string[] args, CancellationToken cancellationToken)
        {
            try
            {
                // Check if we have the required components
                if (this.SshClient == null || this.ShellStream == null || this.CurrentSettings == null)
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] SSH connection not available");
                    return false;
                }

                AnsiConsole.MarkupLine("[cyan]Executing nested program upload command...[/]");
                AnsiConsole.WriteLine();

                // Create a ProgramUploadCommand and execute it with our existing connection
                var uploadCommand = new Program.ProgramUploadCommand();

                // Parse args into settings
                var settings = new Program.ProgramUploadSettings();

                // Set connection details from current session
                settings.Host = this.CurrentSettings.Address!;
                settings.Username = this.CurrentSettings.Username!;
                settings.Password = this.CurrentSettings.Password!;
                settings.Verbose = this.CurrentSettings.Verbose;

                // Parse remaining arguments
                if (!this.ParseProgramUploadArgs(args, settings))
                {
                    return false;
                }

                // Execute the command using the existing SSH connection
                var result = await uploadCommand.ExecuteWithConnectionAsync(
                    settings,
                    this.SshClient,
                    this.ShellStream,
                    cancellationToken);

                return result == 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error executing nested command:[/] {ex.Message.EscapeMarkup()}");
                if (this.CurrentSettings?.Verbose == true)
                {
                    AnsiConsole.WriteException(ex);
                }

                return false;
            }
        }

        private bool HandleUnknownProgramCommand(string subcommand)
        {
            AnsiConsole.MarkupLine($"[red]Unknown program subcommand:[/] {subcommand.EscapeMarkup()}");
            AnsiConsole.MarkupLine("[yellow]Available: upload (u, load, l)[/]");
            return false;
        }

        private bool ParseProgramUploadArgs(string[] args, Program.ProgramUploadSettings settings)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Program file is required");
                AnsiConsole.MarkupLine("[yellow]Usage:[/] :program upload <file> -s <slot> [options]");
                AnsiConsole.MarkupLine("[yellow]Example:[/] :program upload ./demo.cpz -s 1 -c");
                return false;
            }

            settings.ProgramFile = args[0];

            // Parse options
            for (var i = 1; i < args.Length; i++)
            {
                var arg = args[i];

                switch (arg.ToLowerInvariant())
                {
                    case "-s" or "--slot":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var slot))
                        {
                            settings.Slot = slot;
                            i++;
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[red]Error:[/] Invalid or missing slot number");
                            return false;
                        }
                        break;

                    case "-c" or "--changed-only":
                        settings.ChangedOnly = true;
                        break;

                    case "-k" or "--kill":
                        settings.KillProgram = true;
                        break;

                    case "-d" or "--do-not-start":
                        settings.DoNotStart = true;
                        break;

                    case "--no-zig":
                        settings.NoZig = true;
                        break;

                    case "--no-ip-table":
                        settings.NoIpTable = true;
                        break;

                    default:
                        AnsiConsole.MarkupLine($"[yellow]Warning:[/] Unknown option: {arg.EscapeMarkup()}");
                        break;
                }
            }

            // Validate required parameters
            if (settings.Slot == 0)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Slot (-s) is required");
                AnsiConsole.MarkupLine("[yellow]Example:[/] :program upload ./demo.cpz -s 1");
                return false;
            }

            return true;
        }
    }
}
