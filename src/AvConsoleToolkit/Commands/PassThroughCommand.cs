// <copyright file="PassThroughCommand.cs">
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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace AvConsoleToolkit.Commands
{
    /// <summary>
    /// Abstract base class for interactive SSH pass-through commands.
    /// Handles SSH connection management, command history, line buffering, and basic I/O.
    /// </summary>
    /// <typeparam name="TSettings">The settings type for the command.</typeparam>
    public abstract partial class PassThroughCommand<TSettings> : AsyncCommand<TSettings>
        where TSettings : PassThroughSettings
    {
        private CommandHistory? commandHistory;

        private string currentLine = string.Empty;

        private ISshClient? sshClient;

        private Ssh.IShellStream? shellStream;

        private bool isExecutingNestedCommand;

        private string? pendingNestedCommand;

        private readonly StringBuilder outputBuffer = new();

        private CancellationTokenSource? sessionCancellation;

        private bool showingHistoryMenu;

        private int historyMenuSelectedIndex;

        private List<(string Command, int MatchIndex)>? historyMenuItems;

        /// <summary>
        /// Gets the command to send to the remote device to exit the session.
        /// Override this in derived classes to specify device-specific exit commands.
        /// </summary>
        protected abstract string ExitCommand { get; }

        /// <summary>
        /// Gets the command branch prefix for nested commands (e.g., "crestron").
        /// Override this in derived classes to specify the command branch.
        /// </summary>
        protected abstract string CommandBranch { get; }

        /// <summary>
        /// Gets the current SSH client connection.
        /// This allows derived classes to share the connection with nested commands.
        /// </summary>
        protected ISshClient? SshClient => this.sshClient;

        /// <summary>
        /// Gets the current shell stream.
        /// This allows derived classes to share the shell stream with nested commands.
        /// </summary>
        internal Ssh.IShellStream? ShellStream => this.shellStream;

        /// <summary>
        /// Gets the current settings for the pass-through command.
        /// This allows derived classes to access connection parameters.
        /// </summary>
        protected TSettings? CurrentSettings { get; private set; }

        /// <summary>
        /// Gets the Regex used to identify the device prompt.
        /// </summary>
        [GeneratedRegex(@"^([^\r\n]*>) ?$", RegexOptions.Multiline)]
        protected partial Regex PromptRegex { get; }

        /// <summary>
        /// Gets the device prompt, or <see langword="null"/> if none has been determined.
        /// </summary>
        protected string? Prompt { get; private set; }

        /// <summary>
        /// Executes the pass-through command, establishing an SSH connection and entering interactive mode.
        /// </summary>
        /// <param name="context">The command context.</param>
        /// <param name="settings">The command settings.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Exit code: 0 for success, non-zero for failure.</returns>
        public override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken)
        {
            try
            {
                // Store settings for nested command access
                this.CurrentSettings = settings;

                // Resolve host address
                var host = settings.Address;
                if (string.IsNullOrEmpty(host))
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Host address is required.");
                    return 1;
                }

                // Resolve credentials
                if (string.IsNullOrEmpty(settings.Username) || string.IsNullOrEmpty(settings.Password))
                {
                    if (settings.Verbose)
                    {
                        AnsiConsole.MarkupLine("[dim]No username/password provided, looking up values from address books.[/]");
                    }

                    var entry = await AvConsoleToolkit.Crestron.ToolboxAddressBook.LookupEntryAsync(host);
                    if (entry == null)
                    {
                        AnsiConsole.MarkupLine("[red]Error:[/] Could not find device in address books and no username/password provided.");
                        AnsiConsole.MarkupLine("[yellow]Provide credentials with -u and -p flags.[/]");
                        return 1;
                    }

                    if (entry.Username == null || entry.Password == null)
                    {
                        AnsiConsole.MarkupLine("[red]Error:[/] Address book entry is missing username or password.");
                        return 1;
                    }

                    settings.Username = entry.Username;
                    settings.Password = entry.Password;
                }

                // Initialize command history
                this.commandHistory = new CommandHistory(host, this.GetMaxHistorySize());
                await this.commandHistory.LoadAsync();

                // Connect to device
                if (!await this.ConnectAsync(host, settings.Username, settings.Password, cancellationToken))
                {
                    return 1;
                }

                AnsiConsole.MarkupLine($"[green]Connected to {host.EscapeMarkup()}[/]");
                AnsiConsole.MarkupLine("[dim]Press Ctrl+X to exit[/]");
                AnsiConsole.WriteLine();

                // Enter interactive mode
                await this.RunInteractiveSessionAsync(cancellationToken);

                return 0;
            }
            catch (Exception ex)
            {
                if (settings.Verbose)
                {
                    AnsiConsole.WriteException(ex);
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
                }

                return 1;
            }
            finally
            {
                await this.CleanupAsync();
            }
        }

        /// <summary>
        /// Gets the maximum number of commands to keep in history.
        /// Override this to customize history size per device type.
        /// </summary>
        /// <returns>Maximum history size (default: 50).</returns>
        protected virtual int GetMaxHistorySize() => 50;

        /// <summary>
        /// Handles a nested command (prefixed with ':').
        /// Override this to implement device-specific nested command routing.
        /// </summary>
        /// <param name="command">The nested command without the ':' prefix.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the command was handled, false otherwise.</returns>
        protected virtual Task<bool> HandleNestedCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(command) || Program.App == null)
            {
                AnsiConsole.MarkupLine("[yellow]Command could not be executed.[/]");
                return Task.FromResult(false);
            }

            try
            {
                // Parse the command line into arguments
                var args = ParseCommandLine(command);
                if (args.Length == 0)
                {
                    return Task.FromResult(false);
                }

                // Prepend the command branch (e.g., "crestron")
                var fullArgs = new List<string>(args.Length + 7);
                fullArgs.Add(this.CommandBranch);
                fullArgs.AddRange(args);

                if (!fullArgs.Contains("-a") && !fullArgs.Contains("--address"))
                {
                    fullArgs.Add("-a");
                    fullArgs.Add(this.CurrentSettings!.Address!);
                }

                if (!fullArgs.Contains("-u") && !fullArgs.Contains("--username"))
                {
                    fullArgs.Add("-u");
                    fullArgs.Add(this.CurrentSettings!.Username!);
                }

                if (!fullArgs.Contains("-p") && !fullArgs.Contains("--password"))
                {
                    fullArgs.Add("-p");
                    fullArgs.Add(this.CurrentSettings!.Password!);
                }

                // Execute using the main command app
                var result = Program.App.Run(fullArgs);

                return Task.FromResult(result == 0);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error executing nested command:[/] {ex.Message.EscapeMarkup()}");
                if (this.CurrentSettings?.Verbose == true)
                {
                    AnsiConsole.WriteException(ex);
                }
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Parses a command line string into an array of arguments, respecting quotes.
        /// </summary>
        /// <param name="commandLine">The command line to parse.</param>
        /// <returns>Array of parsed arguments.</returns>
        protected static string[] ParseCommandLine(string commandLine)
        {
            var parts = new System.Collections.Generic.List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;

            for (int i = 0; i < commandLine.Length; i++)
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

        /// <summary>
        /// Handles special key sequences like tab completion.
        /// Override this to implement device-specific behavior.
        /// </summary>
        /// <param name="keyInfo">The console key information.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the key was handled, false to use default behavior.</returns>
        protected virtual Task<bool> HandleSpecialKeyAsync(ConsoleKeyInfo keyInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        /// <summary>
        /// Called when the SSH connection is established and ready.
        /// Override this to perform device-specific initialization.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        protected virtual Task OnConnectedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private async Task CleanupAsync()
        {
            // Cancel session if still running
            this.sessionCancellation?.Cancel();

            // Save history
            if (this.commandHistory != null)
            {
                await this.commandHistory.SaveAsync();
            }

            // Release connections through SshManager
            if (this.CurrentSettings != null &&
                !string.IsNullOrEmpty(this.CurrentSettings.Address) &&
                !string.IsNullOrEmpty(this.CurrentSettings.Username))
            {
                // Note: We don't release here because the connection might be reused
                // Connections will be cleaned up when the application exits
            }

            this.sessionCancellation?.Dispose();
        }

        private async Task<bool> ConnectAsync(string host, string username, string password, CancellationToken cancellationToken)
        {
            try
            {
                await AnsiConsole.Status()
                    .StartAsync("Connecting to device...", async ctx =>
                    {
                        this.sshClient = await Ssh.SshManager.GetSshClientAsync(host, username, password, cancellationToken);
                        if (!this.sshClient.IsConnected)
                        {
                            ctx.Status("Connecting to SSH client.");
                            await this.sshClient.ConnectAsync(cancellationToken);
                        }

                        this.shellStream = await Ssh.SshManager.GetShellStreamAsync(host, username, password, cancellationToken);
                        await this.OnConnectedAsync(cancellationToken);
                        ctx.Status("Connected");
                    });

                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Connection failed:[/] {ex.Message.EscapeMarkup()}");
                return false;
            }
        }

        private async Task ExitSessionAsync()
        {
            if (this.shellStream != null && this.sshClient != null && this.sshClient.IsConnected)
            {
                try
                {
                    AnsiConsole.WriteLine($"> {this.ExitCommand}");
                    AnsiConsole.MarkupLine("[yellow]Disconnecting...[/]");
                    this.shellStream.WriteLine(this.ExitCommand);
                    await Task.Delay(500); // Give device time to process exit command
                }
                catch
                {
                    // Ignore errors during exit
                }
            }

            // Cancel the session
            this.sessionCancellation?.Cancel();
        }

        private void HandleBackspace()
        {
            if (this.currentLine.Length > 0)
            {
                this.currentLine = this.currentLine[..^1];
            }
        }

        private void HandleDownArrow()
        {
            if (this.commandHistory == null)
            {
                return;
            }

            // If history menu is showing, navigate down in the menu
            if (this.showingHistoryMenu && this.historyMenuItems != null && this.historyMenuItems.Count > 0)
            {
                this.historyMenuSelectedIndex = Math.Min(this.historyMenuItems.Count - 1, this.historyMenuSelectedIndex + 1);
            }
            else if (this.historyMenuItems != null && this.historyMenuItems.Count > 0)
            {
                // History items are available but menu not active - activate menu and select first item
                this.showingHistoryMenu = true;
                this.historyMenuSelectedIndex = 0;
            }
            else
            {
                // No history menu - use traditional down arrow history navigation
                var nextCommand = this.commandHistory.GetNext();
                if (nextCommand != null)
                {
                    this.currentLine = nextCommand;
                }
                else
                {
                    // At end of history, clear line
                    this.currentLine = string.Empty;
                }
            }
        }

        private void HandleDownArrowWithMenu()
        {
            this.HandleDownArrow();
        }

        private void HandleUpArrow()
        {
            if (this.commandHistory == null)
            {
                return;
            }

            // If history menu is showing, navigate up in the menu
            if (this.showingHistoryMenu && this.historyMenuItems != null && this.historyMenuItems.Count > 0)
            {
                if (this.historyMenuSelectedIndex > 0)
                {
                    this.historyMenuSelectedIndex--;
                }
                else
                {
                    // At top, hide menu
                    this.HideHistoryMenu();
                }
            }
            else
            {
                // Menu not showing - use traditional up arrow history navigation
                var previousCommand = this.commandHistory.GetPrevious();
                if (previousCommand != null)
                {
                    this.currentLine = previousCommand;
                }
            }
        }

        private void ShowHistoryMenu()
        {
            if (this.commandHistory == null)
            {
                return;
            }

            // Check if history is enabled in settings
            if (!Configuration.AppConfig.Settings.PassThrough.UseHistoryForPassThrough)
            {
                this.historyMenuItems = null;
                return;
            }

            // If search text is empty, close the history menu
            if (string.IsNullOrWhiteSpace(this.currentLine))
            {
                this.historyMenuItems = null;
                this.showingHistoryMenu = false;
                this.historyMenuSelectedIndex = -1;
                return;
            }

            // Get matching history items with match position information
            this.historyMenuItems = this.commandHistory.SearchByPrefix(this.currentLine, 5);
            
            if (this.historyMenuItems.Count == 0)
            {
                this.historyMenuItems = null;
                return;
            }

            // Menu is not "active" until user presses down arrow
            this.showingHistoryMenu = false;
            this.historyMenuSelectedIndex = -1;
        }

        private void HideHistoryMenu()
        {
            this.showingHistoryMenu = false;
            this.historyMenuSelectedIndex = -1;
            this.historyMenuItems = null;
        }

        private void HandleEscape()
        {
            if (this.showingHistoryMenu || (this.historyMenuItems != null && this.historyMenuItems.Count > 0))
            {
                this.HideHistoryMenu();
                this.historyMenuItems = null;
            }
            else
            {
                this.currentLine = string.Empty;
            }
        }

        private async Task SubmitCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (this.shellStream == null || string.IsNullOrWhiteSpace(command))
            {
                // Just return, prompt will update naturally
                return;
            }

            // Clear the live prompt before executing command
            AnsiConsole.Write("\r");
            AnsiConsole.Write(new string(' ', command.Length + 10));
            AnsiConsole.Write("\r");

            // Check if user manually typed the exit command
            if (command.Equals(this.ExitCommand, StringComparison.OrdinalIgnoreCase) || command.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                await this.ExitSessionAsync();
                return;
            }

            // Check for nested command
            if (command.StartsWith(':'))
            {
                var nestedCommand = command[1..].Trim();
                this.commandHistory?.AddCommand(command);

                // Echo the command first
                AnsiConsole.WriteLine($"{this.Prompt ?? "ACT>"} {command}");
                AnsiConsole.WriteLine(); // Add spacing

                // Queue the nested command for execution AFTER Live display exits
                this.pendingNestedCommand = nestedCommand;

                // Set flag to trigger Live display exit
                this.isExecutingNestedCommand = true;

                // Return immediately - Live display will exit, then command will execute
                return;
            }

            // Echo the command
            AnsiConsole.WriteLine($"{this.Prompt ?? "ACT>"} {command}");

            // Send command to device
            this.shellStream.WriteLine(command);
            this.commandHistory?.AddCommand(command);
        }

        private IRenderable RenderPrompt()
        {
            var components = new List<IRenderable>();

            // Add the prompt line
            components.Add(new Text($"{Environment.NewLine}{this.Prompt ?? "ACT>"} {this.currentLine}"));

            // If there are history items, show them below the prompt
            if (this.historyMenuItems != null && this.historyMenuItems.Count > 0)
            {
                // Find the longest item to set consistent width
                var maxWidth = this.historyMenuItems.Max(item => item.Command.Length) + 4; // +4 for "> " prefix and padding

                var historyRows = this.historyMenuItems.Select((item, index) =>
                {
                    var isSelected = this.showingHistoryMenu && index == this.historyMenuSelectedIndex;
                    var command = item.Command;
                    var matchIndex = item.MatchIndex;

                    // Build the markup with highlighting
                    var markup = new StringBuilder();
                    
                    if (isSelected)
                    {
                        // Selected: white on grey for entire row
                        markup.Append("[white on grey]> ");
                    }
                    else
                    {
                        // Unselected: olive arrow with default background
                        markup.Append("[olive]>[/] ");
                    }

                    // Highlight the matching portion
                    if (matchIndex >= 0)
                    {
                        var searchLength = this.currentLine.Length;
                        
                        // Add text before match
                        if (matchIndex > 0)
                        {
                            if (isSelected)
                            {
                                markup.Append(command.Substring(0, matchIndex).EscapeMarkup());
                            }
                            else
                            {
                                markup.Append(command.Substring(0, matchIndex).EscapeMarkup());
                            }
                        }
                        
                        // Add highlighted match
                        if (isSelected)
                        {
                            // Keep white on grey, but make match bold or underlined
                            markup.Append("[bold]");
                            markup.Append(command.Substring(matchIndex, searchLength).EscapeMarkup());
                            markup.Append("[/]");
                        }
                        else
                        {
                            // Cyan highlight for unselected items
                            markup.Append("[cyan]");
                            markup.Append(command.Substring(matchIndex, searchLength).EscapeMarkup());
                            markup.Append("[/]");
                        }
                        
                        // Add text after match
                        if (matchIndex + searchLength < command.Length)
                        {
                            if (isSelected)
                            {
                                markup.Append(command.Substring(matchIndex + searchLength).EscapeMarkup());
                            }
                            else
                            {
                                markup.Append(command.Substring(matchIndex + searchLength).EscapeMarkup());
                            }
                        }
                    }
                    else
                    {
                        // No match or match not found, just show the command
                        markup.Append(command.EscapeMarkup());
                    }

                    // Pad to max width
                    var currentLength = command.Length + 2; // +2 for "> "
                    if (currentLength < maxWidth)
                    {
                        markup.Append(new string(' ', maxWidth - currentLength));
                    }
                    
                    if (isSelected)
                    {
                        markup.Append("[/]"); // Close [white on grey]
                    }

                    return new Markup(markup.ToString());
                }).ToList();

                var historyPanel = new Panel(new Rows(historyRows))
                {
                    Border = BoxBorder.None,
                    Padding = new Padding(0, 0, 0, 0)
                };

                components.Add(historyPanel);
            }

            return new Rows(components);
        }

        private async Task RunInteractiveSessionAsync(CancellationToken cancellationToken)
        {
            if (this.shellStream == null)
            {
                return;
            }

            // Create a cancellation token source that combines external cancellation with our session control
            this.sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sessionToken = this.sessionCancellation.Token;

            // Flag to track if we need to reconnect
            bool needsReconnect = false;

            // Start background task to read from SSH with reconnection detection
            var readTask = Task.Run(async () =>
            {
                while (!sessionToken.IsCancellationRequested)
                {
                    try
                    {
                        while (!sessionToken.IsCancellationRequested && this.sshClient?.IsConnected == true)
                        {
                            // Pause reading during nested command execution
                            if (this.isExecutingNestedCommand)
                            {
                                await Task.Delay(100, sessionToken);
                                continue;
                            }

                            if (this.shellStream.DataAvailable)
                            {
                                var data = this.shellStream.Read();
                                lock (this.outputBuffer)
                                {
                                    if (this.Prompt is null)
                                    {
                                        var match = this.PromptRegex.Match(data);
                                        if (match.Success)
                                        {
                                            this.Prompt = match.Groups[1].Value;
                                            this.RenderPrompt();
                                        }
                                    }

                                    this.outputBuffer.Append(data);
                                }
                            }
                            else
                            {
                                await Task.Delay(50, sessionToken);
                            }
                        }

                        // Connection lost - signal reconnection needed
                        if (!sessionToken.IsCancellationRequested && this.sshClient?.IsConnected == false)
                        {
                            needsReconnect = true;
                            // Wait for reconnection to complete
                            while (needsReconnect && !sessionToken.IsCancellationRequested)
                            {
                                await Task.Delay(100, sessionToken);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Normal cancellation
                        break;
                    }
                    catch (Exception)
                    {
                        // Connection error - signal reconnection needed
                        if (!sessionToken.IsCancellationRequested)
                        {
                            needsReconnect = true;
                            // Wait for reconnection to complete
                            while (needsReconnect && !sessionToken.IsCancellationRequested)
                            {
                                await Task.Delay(100, sessionToken);
                            }
                        }
                    }
                }
            }, sessionToken);

            // Wait for initial prompt
            await Task.Delay(1000, sessionToken);

            // Display initial output
            string lastOutput;
            lock (this.outputBuffer)
            {
                lastOutput = this.outputBuffer.ToString();
                this.outputBuffer.Clear();
            }

            if (!string.IsNullOrEmpty(lastOutput))
            {
                AnsiConsole.Write(lastOutput);
            }

            // Main input loop with live prompt display
            var inLiveMode = true;
            var initial = true;
            
            while (!sessionToken.IsCancellationRequested && !this.isExecutingNestedCommand)
            {
                // Check if reconnection is needed
                if (needsReconnect)
                {
                    // Exit Live display to show reconnection messages
                    inLiveMode = false;
                    
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]Connection lost - attempting to reconnect...[/]");
                    
                    // Attempt reconnection
                    var reconnected = await this.ReconnectAsync(sessionToken);
                    
                    if (reconnected)
                    {
                        AnsiConsole.MarkupLine("[green]Successfully reconnected![/]");
                        AnsiConsole.WriteLine();
                        needsReconnect = false;
                        inLiveMode = true;
                        initial = true;
                    }
                    else
                    {
                        // Reconnection failed, session will terminate
                        return;
                    }
                }

                if (inLiveMode && this.sshClient?.IsConnected == true)
                {
                    // Start Live display
                    await AnsiConsole.Live(this.RenderPrompt())
                        .AutoClear(false)
                        .StartAsync(async ctx =>
                        {
                            while (!sessionToken.IsCancellationRequested &&
                                   this.sshClient?.IsConnected == true &&
                                   !this.isExecutingNestedCommand &&
                                   !needsReconnect)
                            {
                                // Check for new output from device
                                string newOutput;
                                lock (this.outputBuffer)
                                {
                                    if (this.outputBuffer.Length > 0)
                                    {
                                        newOutput = this.outputBuffer.ToString();
                                        this.outputBuffer.Clear();
                                    }
                                    else
                                    {
                                        newOutput = string.Empty;
                                    }
                                }

                                // If there's new output, write it before the prompt
                                if (!string.IsNullOrEmpty(newOutput))
                                {
                                    // Write the new output
                                    AnsiConsole.Write(newOutput);

                                    // Update the live display
                                    ctx.UpdateTarget(this.RenderPrompt());
                                }
                                else if (initial)
                                {
                                    ctx.UpdateTarget(this.RenderPrompt());
                                    initial = false;
                                }

                                // Check for keyboard input
                                if (Console.KeyAvailable)
                                {
                                    var keyInfo = Console.ReadKey(intercept: true);

                                    // Handle Ctrl+X for exit
                                    if (keyInfo.Key == ConsoleKey.X && keyInfo.Modifiers == ConsoleModifiers.Control)
                                    {
                                        await this.ExitSessionAsync();
                                        return; // Exit the Live context
                                    }

                                    // Handle Alt+X to delete selected history item
                                    if (keyInfo.Key == ConsoleKey.X && keyInfo.Modifiers == ConsoleModifiers.Alt)
                                    {
                                        if (this.showingHistoryMenu && this.historyMenuItems != null && 
                                            this.historyMenuSelectedIndex >= 0 && this.historyMenuSelectedIndex < this.historyMenuItems.Count)
                                        {
                                            // Get the selected command
                                            var commandToDelete = this.historyMenuItems[this.historyMenuSelectedIndex].Command;
                                            
                                            // Remove from history
                                            if (this.commandHistory?.RemoveCommand(commandToDelete) == true)
                                            {
                                                // Refresh the history menu
                                                this.ShowHistoryMenu();
                                                
                                                // Adjust selected index if needed
                                                if (this.historyMenuItems == null || this.historyMenuItems.Count == 0)
                                                {
                                                    // No items left, hide menu
                                                    this.HideHistoryMenu();
                                                }
                                                else if (this.historyMenuSelectedIndex >= this.historyMenuItems.Count)
                                                {
                                                    // Selected index out of bounds, move to last item
                                                    this.historyMenuSelectedIndex = this.historyMenuItems.Count - 1;
                                                    this.showingHistoryMenu = true; // Keep menu active
                                                }
                                                else
                                                {
                                                    // Keep menu active at current (or adjusted) index
                                                    this.showingHistoryMenu = true;
                                                }
                                                
                                                // Update display
                                                ctx.UpdateTarget(this.RenderPrompt());
                                            }
                                        }
                                        continue;
                                    }

                                    // Swallow other control sequences
                                    if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control) ||
                                        keyInfo.Modifiers.HasFlag(ConsoleModifiers.Alt))
                                    {
                                        continue;
                                    }

                                    // Handle special keys
                                    bool needsUpdate = true;
                                    switch (keyInfo.Key)
                                    {
                                        case ConsoleKey.Enter:
                                            if (this.showingHistoryMenu && this.historyMenuItems != null && this.historyMenuSelectedIndex >= 0)
                                            {
                                                // Get the selected command
                                                var selectedCommand = this.historyMenuItems[this.historyMenuSelectedIndex].Command;
                                                
                                                // Clear the history menu display immediately
                                                this.HideHistoryMenu();
                                                this.historyMenuItems = null;
                                                
                                                // Clear currentLine BEFORE submitting (otherwise SubmitCommandAsync uses wrong length for clearing)
                                                this.currentLine = string.Empty;
                                                
                                                // Force update to hide the menu
                                                ctx.UpdateTarget(this.RenderPrompt());
                                                
                                                // Now submit the selected command directly
                                                await this.SubmitCommandAsync(selectedCommand, sessionToken);
                                                this.commandHistory?.ResetPosition();
                                                
                                                // Check if we just queued a nested command for execution
                                                if (this.isExecutingNestedCommand)
                                                {
                                                    // Exit Live context - nested command will execute after this returns
                                                    return;
                                                }
                                                
                                                needsUpdate = false; // Already updated above
                                            }
                                            else if (!string.IsNullOrWhiteSpace(this.currentLine))
                                            {
                                                await this.SubmitCommandAsync(this.currentLine, sessionToken);
                                                this.currentLine = string.Empty;
                                                this.commandHistory?.ResetPosition();
                                                this.historyMenuItems = null;

                                                // Check if we just queued a nested command for execution
                                                if (this.isExecutingNestedCommand)
                                                {
                                                    // Exit Live context - nested command will execute after this returns
                                                    return;
                                                }
                                            }
                                            else
                                            {
                                                needsUpdate = false;
                                            }
                                            break;

                                        case ConsoleKey.Backspace:
                                            this.HandleBackspace();
                                            // Refresh history matches
                                            this.ShowHistoryMenu();
                                            break;

                                        case ConsoleKey.UpArrow:
                                            this.HandleUpArrow();
                                            break;

                                        case ConsoleKey.DownArrow:
                                            this.HandleDownArrow();
                                            break;

                                        case ConsoleKey.Escape:
                                            this.HandleEscape();
                                            break;

                                        case ConsoleKey.Tab:
                                            // Check if derived class wants to handle it
                                            if (!await this.HandleSpecialKeyAsync(keyInfo, sessionToken))
                                            {
                                                // Default: send to device
                                                this.shellStream.WriteLine(this.currentLine + "\t");
                                            }
                                            needsUpdate = false;
                                            break;

                                        default:
                                            if (!char.IsControl(keyInfo.KeyChar))
                                            {
                                                this.currentLine += keyInfo.KeyChar;
                                                // Refresh history matches automatically
                                                this.ShowHistoryMenu();
                                            }
                                            else
                                            {
                                                needsUpdate = false;
                                            }
                                            break;
                                    }

                                    if (needsUpdate)
                                    {
                                        ctx.UpdateTarget(this.RenderPrompt());
                                    }
                                }
                                else
                                {
                                    await Task.Delay(50, sessionToken);
                                }
                            }
                        });

                    // If we exited because of nested command execution, handle it
                    if (this.isExecutingNestedCommand && this.pendingNestedCommand != null)
                    {
                        inLiveMode = false;
                        initial = true;
                        
                        // Execute nested command
                        var commandToExecute = this.pendingNestedCommand;
                        this.pendingNestedCommand = null;

                        try
                        {
                            // Execute nested command (outside Live display context)
                            await this.HandleNestedCommandAsync(commandToExecute, sessionToken);
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Error executing nested command:[/] {ex.Message.EscapeMarkup()}");
                            if (this.CurrentSettings?.Verbose == true)
                            {
                                AnsiConsole.WriteException(ex);
                            }
                        }
                        finally
                        {
                            AnsiConsole.WriteLine(); // Add spacing after nested command

                            // Mark execution complete and go back to live mode
                            this.isExecutingNestedCommand = false;
                            inLiveMode = true;
                        }
                    }
                }
                else
                {
                    // Should not reach here, but just in case
                    await Task.Delay(50, sessionToken);
                }
            }

            try
            {
                await readTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when we cancel the session
            }
            catch
            {
                // Ignore other read task exceptions
            }
        }

        private async Task<bool> ReconnectAsync(CancellationToken cancellationToken)
        {
            if (this.CurrentSettings == null || string.IsNullOrEmpty(this.CurrentSettings.Address))
            {
                return false;
            }

            // Check if reconnection is disabled
            if (this.CurrentSettings.NoReconnect)
            {
                AnsiConsole.MarkupLine("[red]Automatic reconnection is disabled. Session terminated.[/]");
                this.sessionCancellation?.Cancel();
                return false;
            }

            // Get configured max retries (-1 means infinite)
            var maxRetries = Configuration.AppConfig.Settings.PassThrough.NumberOfReconnectionAttempts;
            var isInfiniteRetries = maxRetries < 0;
            
            int retryCount = 0;
            int delayMs = 500;

            while ((isInfiniteRetries || retryCount < maxRetries) && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(delayMs, cancellationToken);
                    
                    if (isInfiniteRetries)
                    {
                        AnsiConsole.MarkupLine($"[yellow]Reconnection attempt {retryCount + 1}...[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Reconnection attempt {retryCount + 1}/{maxRetries}...[/]");
                    }

                    // Attempt to reconnect
                    var reconnected = await this.ConnectAsync(
                        this.CurrentSettings.Address,
                        this.CurrentSettings.Username!,
                        this.CurrentSettings.Password!,
                        cancellationToken);

                    if (reconnected)
                    {
                        return true;
                    }

                    retryCount++;
                    
                    // Cap exponential backoff at 10 seconds
                    delayMs = Math.Min(delayMs * 2, 10000);
                }
                catch (Exception ex)
                {
                    retryCount++;
                    AnsiConsole.MarkupLine($"[red]Reconnection failed: {ex.Message}[/]");

                    if (!isInfiniteRetries && retryCount >= maxRetries)
                    {
                        AnsiConsole.MarkupLine("[red]Maximum reconnection attempts reached. Session terminated.[/]");
                        this.sessionCancellation?.Cancel();
                        return false;
                    }

                    // Cap exponential backoff at 30 seconds
                    delayMs = Math.Min(delayMs * 2, 30000);
                }
            }
            
            // If we exit the loop without success (non-infinite retries exhausted)
            if (!isInfiniteRetries)
            {
                AnsiConsole.MarkupLine("[red]Maximum reconnection attempts reached. Session terminated.[/]");
                this.sessionCancellation?.Cancel();
            }
            
            return false;
        }
    }
}
