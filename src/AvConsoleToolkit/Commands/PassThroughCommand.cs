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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly StringBuilder outputBuffer = new();

        private CommandHistory? commandHistory;

        private string currentLine = string.Empty;

        private int cursorBlinkCounter = 0;

        private int cursorPosition = 0;

        private List<(string Command, int MatchIndex)>? historyMenuItems;

        private int historyMenuSelectedIndex;

        private bool isDisconnected;

        private bool isExecutingNestedCommand;

        private string originalTypedValue = string.Empty;

        private string? pendingNestedCommand;

        private int selectionEnd = -1;

        private int selectionStart = -1;

        private CancellationTokenSource? sessionCancellation;

        private bool shouldExitLiveMode;

        private bool showCursor = true;

        private bool showingHistoryMenu;

        private Ssh.ISshConnection? sshConnection;

        /// <summary>
        /// Gets the command branch prefix for nested commands (e.g., "crestron").
        /// Override this in derived classes to specify the command branch.
        /// </summary>
        protected abstract string CommandBranch { get; }

        /// <summary>
        /// Gets the current settings for the pass-through command.
        /// This allows derived classes to access connection parameters.
        /// </summary>
        protected TSettings? CurrentSettings { get; private set; }

        /// <summary>
        /// Gets the command to send to the remote device to exit the session.
        /// Override this in derived classes to specify device-specific exit commands.
        /// </summary>
        protected abstract string ExitCommand { get; }

        /// <summary>
        /// Gets the device prompt, or <see langword="null"/> if none has been determined.
        /// </summary>
        protected string? Prompt { get; private set; }

        /// <summary>
        /// Gets the Regex used to identify the device prompt.
        /// </summary>
        [GeneratedRegex(@"^([^\r\n]*>) ?$", RegexOptions.Multiline)]
        protected partial Regex PromptRegex { get; }

        /// <summary>
        /// Gets the current SSH connection.
        /// This allows derived classes to share the connection with nested commands.
        /// </summary>
        protected Ssh.ISshConnection? SshConnection => this.sshConnection;

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
        /// Parses a command line string into an array of arguments, respecting quotes.
        /// </summary>
        /// <param name="commandLine">The command line to parse.</param>
        /// <returns>Array of parsed arguments.</returns>
        protected static string[] ParseCommandLine(string commandLine)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
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

        /// <summary>
        /// Gets a dictionary of command mappings for the device.
        /// The key is the command to map from (e.g., "ls"), and the value is the command to map to (e.g., "dir").
        /// Commands are matched case-insensitively as the first word in the command line.
        /// Override this in derived classes to provide device-specific command aliases.
        /// </summary>
        /// <returns>A dictionary of command mappings, or null/empty if no mappings are needed.</returns>
        protected virtual IReadOnlyDictionary<string, string>? GetCommandMappings() => null;

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

        /// <summary>
        /// Applies command mappings to the given command if any are configured.
        /// </summary>
        /// <param name="command">The original command entered by the user.</param>
        /// <returns>The mapped command, or the original command if no mapping applies.</returns>
        private string ApplyCommandMapping(string command)
        {
            var mappings = this.GetCommandMappings();
            if (mappings == null || mappings.Count == 0)
            {
                return command;
            }

            // Extract the first word (the command name)
            var parts = command.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return command;
            }

            var commandName = parts[0];
            var arguments = parts.Length > 1 ? parts[1] : string.Empty;

            // Check if there's a mapping for this command (case-insensitive)
            var mapping = mappings.FirstOrDefault(kvp =>
                string.Equals(kvp.Key, commandName, StringComparison.OrdinalIgnoreCase));

            if (mapping.Key != null)
            {
                // Build the mapped command
                if (string.IsNullOrEmpty(arguments))
                {
                    return mapping.Value;
                }
                else
                {
                    return $"{mapping.Value} {arguments}";
                }
            }

            return command;
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

            this.sessionCancellation?.Dispose();
        }

        private async Task<bool> ConnectAsync(string host, string username, string password, CancellationToken cancellationToken)
        {
            try
            {
                await AnsiConsole.Status()
                    .StartAsync("Connecting to device...", async ctx =>
                    {
                        this.sshConnection = Ssh.ConnectionFactory.Instance.GetSshConnection(host, 22, username, password);

                        // Set the maximum reconnection attempts from settings
                        this.sshConnection.MaxReconnectionAttempts = Configuration.AppConfig.Settings.PassThrough.NumberOfReconnectionAttempts;

                        // Explicitly establish the shell connection
                        await this.sshConnection.ConnectShellAsync(cancellationToken);

                        // Subscribe to connection events for handling disconnection/reconnection
                        this.sshConnection.ShellDisconnected += this.OnShellDisconnected;
                        this.sshConnection.ShellReconnected += this.OnShellReconnected;

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
            if (this.sshConnection != null && this.sshConnection.IsConnected)
            {
                try
                {
                    AnsiConsole.WriteLine($"> {this.ExitCommand}");
                    AnsiConsole.MarkupLine("[yellow]Disconnecting...[/]");
                    await this.sshConnection.WriteLineAsync(this.ExitCommand);
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
            // If there's a selection, delete the selected text
            if (this.selectionStart >= 0 && this.selectionEnd >= 0)
            {
                var start = Math.Min(this.selectionStart, this.selectionEnd);
                var end = Math.Max(this.selectionStart, this.selectionEnd);
                var deleteCount = end - start;
                this.currentLine = this.currentLine.Remove(start, deleteCount);
                this.cursorPosition = start;
                this.selectionStart = -1;
                this.selectionEnd = -1;
            }
            else if (this.cursorPosition > 0)
            {
                this.currentLine = this.currentLine.Remove(this.cursorPosition - 1, 1);
                this.cursorPosition--;
                this.selectionStart = -1;
                this.selectionEnd = -1;
            }
        }

        private void HandleDelete()
        {
            // If there's a selection, delete the selected text
            if (this.selectionStart >= 0 && this.selectionEnd >= 0)
            {
                var start = Math.Min(this.selectionStart, this.selectionEnd);
                var end = Math.Max(this.selectionStart, this.selectionEnd);
                var deleteCount = end - start;
                this.currentLine = this.currentLine.Remove(start, deleteCount);
                this.cursorPosition = start;
                this.selectionStart = -1;
                this.selectionEnd = -1;
            }
            else if (this.cursorPosition < this.currentLine.Length)
            {
                this.currentLine = this.currentLine.Remove(this.cursorPosition, 1);
                this.selectionStart = -1;
                this.selectionEnd = -1;
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
                // Check if we're about to wrap from last to first
                if (this.historyMenuSelectedIndex >= this.historyMenuItems.Count - 1)
                {
                    // Wrap to show original typed value as intermediate step
                    this.historyMenuSelectedIndex = -1;
                    this.currentLine = this.originalTypedValue;
                    this.cursorPosition = this.currentLine.Length;
                    this.selectionStart = -1;
                    this.selectionEnd = -1;
                    this.showingHistoryMenu = false;
                }
                else
                {
                    // Normal navigation: move to next item
                    this.historyMenuSelectedIndex++;
                    this.currentLine = this.historyMenuItems[this.historyMenuSelectedIndex].Command;
                    this.cursorPosition = this.currentLine.Length;
                    this.selectionStart = -1;
                    this.selectionEnd = -1;
                }
            }
            else if (this.historyMenuItems != null && this.historyMenuItems.Count > 0)
            {
                // History items are available but menu not active - activate menu and select first item
                this.showingHistoryMenu = true;
                this.historyMenuSelectedIndex = 0;
                this.currentLine = this.historyMenuItems[0].Command;
                this.cursorPosition = this.currentLine.Length;
                this.selectionStart = -1;
                this.selectionEnd = -1;
            }
            else
            {
                // No history menu - use traditional down arrow history navigation
                var nextCommand = this.commandHistory.GetNext();
                if (nextCommand != null)
                {
                    this.currentLine = nextCommand;
                    this.cursorPosition = this.currentLine.Length;
                    this.selectionStart = -1;
                    this.selectionEnd = -1;
                }
                else
                {
                    // At end of history (most recent), clear the line
                    this.currentLine = string.Empty;
                    this.cursorPosition = 0;
                    this.selectionStart = -1;
                    this.selectionEnd = -1;
                }
            }
        }

        private void HandleEnd(bool shift)
        {
            if (shift)
            {
                // Shift+End: extend or create selection to end of line
                if (this.selectionStart == -1)
                {
                    this.selectionStart = this.cursorPosition;
                }
                this.cursorPosition = this.currentLine.Length;
                this.selectionEnd = this.cursorPosition;
                if (this.selectionStart == this.selectionEnd)
                {
                    this.selectionStart = -1;
                    this.selectionEnd = -1;
                }
            }
            else
            {
                // End: move cursor to end of line
                this.cursorPosition = this.currentLine.Length;
                this.selectionStart = -1;
                this.selectionEnd = -1;
            }
        }

        private void HandleEscape()
        {
            if (this.showingHistoryMenu || this.historyMenuItems != null && this.historyMenuItems.Count > 0)
            {
                this.HideHistoryMenu();
                this.historyMenuItems = null;
            }
            else
            {
                this.currentLine = string.Empty;
                this.cursorPosition = 0;
                this.selectionStart = -1;
                this.selectionEnd = -1;
            }
        }

        private void HandleHome(bool shift)
        {
            if (shift)
            {
                // Shift+Home: extend or create selection to start of line
                if (this.selectionStart == -1)
                {
                    this.selectionStart = this.cursorPosition;
                }
                this.cursorPosition = 0;
                this.selectionEnd = this.cursorPosition;
                if (this.selectionStart == this.selectionEnd)
                {
                    this.selectionStart = -1;
                    this.selectionEnd = -1;
                }
            }
            else
            {
                // Home: move cursor to start of line
                this.cursorPosition = 0;
                this.selectionStart = -1;
                this.selectionEnd = -1;
            }
        }

        private void HandleLeftArrow(bool shift)
        {
            if (this.showingHistoryMenu)
            {
                return;
            }

            if (shift)
            {
                // Shift+Left: extend or create selection
                if (this.selectionStart == -1)
                {
                    this.selectionStart = this.cursorPosition;
                }
                this.cursorPosition = Math.Max(0, this.cursorPosition - 1);
                this.selectionEnd = this.cursorPosition;
                if (this.selectionStart == this.selectionEnd)
                {
                    this.selectionStart = -1;
                    this.selectionEnd = -1;
                }
            }
            else
            {
                // Left: move cursor left
                this.cursorPosition = Math.Max(0, this.cursorPosition - 1);
                this.selectionStart = -1;
                this.selectionEnd = -1;
            }
        }

        private void HandleRightArrow(bool shift)
        {
            if (this.showingHistoryMenu)
            {
                return;
            }

            if (shift)
            {
                // Shift+Right: extend or create selection
                if (this.selectionStart == -1)
                {
                    this.selectionStart = this.cursorPosition;
                }
                this.cursorPosition = Math.Min(this.currentLine.Length, this.cursorPosition + 1);
                this.selectionEnd = this.cursorPosition;
                if (this.selectionStart == this.selectionEnd)
                {
                    this.selectionStart = -1;
                    this.selectionEnd = -1;
                }
            }
            else
            {
                // Right: move cursor right
                this.cursorPosition = Math.Min(this.currentLine.Length, this.cursorPosition + 1);
                this.selectionStart = -1;
                this.selectionEnd = -1;
            }
        }

        private void HandleUpArrow()
        {
            if (this.commandHistory == null)
            {
                return;
            }

            // If history menu items are available but menu not active, activate it and select last item
            if (!this.showingHistoryMenu && this.historyMenuItems != null && this.historyMenuItems.Count > 0)
            {
                this.showingHistoryMenu = true;
                this.historyMenuSelectedIndex = this.historyMenuItems.Count - 1;
                this.currentLine = this.historyMenuItems[this.historyMenuSelectedIndex].Command;
                this.cursorPosition = this.currentLine.Length;
                this.selectionStart = -1;
                this.selectionEnd = -1;
                return;
            }

            // If history menu is showing, navigate up in the menu
            if (this.showingHistoryMenu && this.historyMenuItems != null && this.historyMenuItems.Count > 0)
            {
                // Check if we're about to wrap from first to last
                if (this.historyMenuSelectedIndex <= 0)
                {
                    // Wrap to show original typed value as intermediate step
                    this.historyMenuSelectedIndex = -1;
                    this.currentLine = this.originalTypedValue;
                    this.cursorPosition = this.currentLine.Length;
                    this.selectionStart = -1;
                    this.selectionEnd = -1;
                    this.showingHistoryMenu = false;
                }
                else
                {
                    // Normal navigation: move to previous item
                    this.historyMenuSelectedIndex--;
                    this.currentLine = this.historyMenuItems[this.historyMenuSelectedIndex].Command;
                    this.cursorPosition = this.currentLine.Length;
                    this.selectionStart = -1;
                    this.selectionEnd = -1;
                }
                return;
            }

            // No history menu - use traditional up arrow history navigation
            var previousCommand = this.commandHistory.GetPrevious();
            if (previousCommand != null)
            {
                this.currentLine = previousCommand;
                this.cursorPosition = this.currentLine.Length;
                this.selectionStart = -1;
                this.selectionEnd = -1;
            }
        }

        private void HideHistoryMenu()
        {
            this.showingHistoryMenu = false;
            this.historyMenuSelectedIndex = -1;
            this.historyMenuItems = null;
            this.originalTypedValue = string.Empty;
        }

        private void OnShellDisconnected(object? sender, EventArgs e)
        {
            this.isDisconnected = true;
            this.shouldExitLiveMode = true;
        }

        private async void OnShellReconnected(object? sender, EventArgs e)
        {
            // Call OnConnectedAsync again before resuming
            try
            {
                if (this.sessionCancellation != null && !this.sessionCancellation.Token.IsCancellationRequested)
                {
                    await this.OnConnectedAsync(this.sessionCancellation.Token);
                }
            }
            catch
            {
                // Ignore errors during reconnection initialization
            }

            this.isDisconnected = false;
        }

        private IRenderable RenderPrompt()
        {
            Console.CursorVisible = false;
            var components = new List<IRenderable>();

            // Don't render the prompt if disconnected
            if (this.isDisconnected)
            {
                return new Markup(string.Empty);
            }

            // Build the prompt line with cursor position and selection highlighting
            var promptPrefix = $"{Environment.NewLine}{this.Prompt ?? "ACT>"} ";
            var markup = new StringBuilder(promptPrefix.EscapeMarkup());

            // Build the command line with cursor and selection highlighting
            for (var i = 0; i < this.currentLine.Length; i++)
            {
                var character = this.currentLine[i];

                // Check if this position is within selection
                var isInSelection = this.selectionStart >= 0 && this.selectionEnd >= 0 &&
                                   i >= Math.Min(this.selectionStart, this.selectionEnd) &&
                                   i < Math.Max(this.selectionStart, this.selectionEnd);

                // Check if this is the cursor position
                var isCursorPosition = i == this.cursorPosition;

                if (isCursorPosition && this.showCursor)
                {
                    // Cursor position with background color
                    markup.Append($"[white on blue]{character.ToString().EscapeMarkup()}[/]");
                }
                else if (isInSelection)
                {
                    // Selection highlighting
                    markup.Append($"[white on grey]{character.ToString().EscapeMarkup()}[/]");
                }
                else
                {
                    // Normal character
                    markup.Append(character.ToString().EscapeMarkup());
                }
            }

            // If cursor is at the end of the line, render a space with cursor background
            if (this.cursorPosition == this.currentLine.Length)
            {
                if (this.showCursor)
                {
                    markup.Append("[white on blue] [/]");
                }
                else
                {
                    markup.Append(" ");
                }
            }

            components.Add(new Markup(markup.ToString()));

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
                    var historyMarkup = new StringBuilder();

                    if (isSelected)
                    {
                        // Selected: white on grey for entire row
                        historyMarkup.Append("[white on grey]> ");
                    }
                    else
                    {
                        // Unselected: olive arrow with default background
                        historyMarkup.Append("[olive]>[/] ");
                    }

                    // Highlight the matching portion
                    if (matchIndex >= 0 && matchIndex < command.Length)
                    {
                        var searchLength = Math.Min(this.currentLine.Length, command.Length - matchIndex);

                        // Add text before match
                        if (matchIndex > 0)
                        {
                            historyMarkup.Append(command.Substring(0, matchIndex).EscapeMarkup());
                        }

                        // Add highlighted match
                        if (isSelected)
                        {
                            // Keep white on grey, but make match bold or underlined
                            historyMarkup.Append("[bold]");

                            historyMarkup.Append(command.Substring(matchIndex, searchLength).EscapeMarkup());
                            historyMarkup.Append("[/]");
                        }
                        else
                        {
                            // Cyan highlight for unselected items
                            historyMarkup.Append("[cyan]");
                            historyMarkup.Append(command.Substring(matchIndex, searchLength).EscapeMarkup());
                            historyMarkup.Append("[/]");
                        }

                        // Add text after match
                        if (matchIndex + searchLength < command.Length)
                        {
                            historyMarkup.Append(command.Substring(matchIndex + searchLength).EscapeMarkup());
                        }
                    }
                    else
                    {
                        // No match or match not found, just show the command
                        historyMarkup.Append(command.EscapeMarkup());
                    }

                    // Pad to max width
                    var currentLength = command.Length + 2; // +2 for "> "
                    if (currentLength < maxWidth)
                    {
                        historyMarkup.Append(new string(' ', maxWidth - currentLength));
                    }

                    if (isSelected)
                    {
                        historyMarkup.Append("[/]"); // Close [white on grey]
                    }

                    return new Markup(historyMarkup.ToString());
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
            if (this.sshConnection == null)
            {
                return;
            }

            // Create a cancellation token source that combines external cancellation with our session control
            this.sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sessionToken = this.sessionCancellation.Token;

            // Start background task to read from SSH
            var readTask = Task.Run(async () =>
            {
                while (!sessionToken.IsCancellationRequested)
                {
                    try
                    {
                        // Wait if disconnected
                        while (this.isDisconnected && !sessionToken.IsCancellationRequested)
                        {
                            await Task.Delay(100, sessionToken);
                        }

                        // Pause reading during nested command execution
                        if (this.isExecutingNestedCommand)
                        {
                            await Task.Delay(100, sessionToken);
                            continue;
                        }

                        if (this.sshConnection?.IsConnected == true && this.sshConnection.DataAvailable)
                        {
                            var data = await this.sshConnection.ReadAsync(sessionToken);
                            lock (this.outputBuffer)
                            {
                                if (this.Prompt is null)
                                {
                                    var match = this.PromptRegex.Match(data);
                                    if (match.Success)
                                    {
                                        this.Prompt = match.Groups[1].Value;
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
                    catch (OperationCanceledException)
                    {
                        // Normal cancellation
                        break;
                    }
                    catch (Exception)
                    {
                        // Connection error - wait for reconnection
                        await Task.Delay(100, sessionToken);
                    }
                }
            }, sessionToken);

            // Wait for initial prompt - increased time for devices that send headers
            while (this.Prompt == null)
            {
                await Task.Delay(100, sessionToken);
            }

            // Display initial output
            string lastOutput;
            lock (this.outputBuffer)
            {
                lastOutput = this.outputBuffer.ToString();
                this.outputBuffer.Clear();
            }

            if (!string.IsNullOrEmpty(lastOutput))
            {
                // Strip trailing prompts from initial output to avoid duplicates
                // Some devices send multiple prompts, so we need to remove all of them
                if (this.Prompt != null)
                {
                    var outputToWrite = lastOutput;

                    // Remove all instances of the prompt (with and without trailing space)
                    var promptWithSpace = $"{this.Prompt} ";
                    outputToWrite = outputToWrite.Replace(promptWithSpace, string.Empty);
                    outputToWrite = outputToWrite.Replace(this.Prompt, string.Empty);

                    // Clean up any resulting multiple consecutive newlines
                    while (outputToWrite.Contains("\n\n\n"))
                    {
                        outputToWrite = outputToWrite.Replace("\n\n\n", "\n\n");
                    }
                    while (outputToWrite.Contains("\r\n\r\n\r\n"))
                    {
                        outputToWrite = outputToWrite.Replace("\r\n\r\n\r\n", "\r\n\r\n");
                    }

                    // Trim trailing whitespace
                    outputToWrite = outputToWrite.TrimEnd('\r', '\n', ' ', '\t');
                    if (!string.IsNullOrEmpty(outputToWrite))
                    {
                        outputToWrite += Environment.NewLine;
                    }

                    lastOutput = outputToWrite;
                }

                if (!string.IsNullOrEmpty(lastOutput))
                {
                    AnsiConsole.Write(lastOutput);
                }
            }

            // Main input loop with live prompt display
            var inLiveMode = true;
            var initial = true;

            while (!sessionToken.IsCancellationRequested && !this.isExecutingNestedCommand)
            {
                if (inLiveMode)
                {
                    // Start Live display
                    await AnsiConsole.Live(this.RenderPrompt())
                        .AutoClear(false)
                        .StartAsync(async ctx =>
                        {
                            while (!sessionToken.IsCancellationRequested &&
                                   !this.isExecutingNestedCommand &&
                                   !this.shouldExitLiveMode)
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
                                    // Strip all occurrences of the prompt from device output to avoid duplicates
                                    var outputToWrite = newOutput;
                                    if (this.Prompt != null)
                                    {
                                        // Remove all instances of the prompt (with and without trailing space)
                                        var promptWithSpace = $"{this.Prompt} ";
                                        outputToWrite = outputToWrite.Replace(promptWithSpace, string.Empty);
                                        outputToWrite = outputToWrite.Replace(this.Prompt, string.Empty);

                                        // Clean up any resulting multiple consecutive newlines
                                        while (outputToWrite.Contains("\n\n\n"))
                                        {
                                            outputToWrite = outputToWrite.Replace("\n\n\n", "\n\n");
                                        }
                                        while (outputToWrite.Contains("\r\n\r\n\r\n"))
                                        {
                                            outputToWrite = outputToWrite.Replace("\r\n\r\n\r\n", "\r\n\r\n");
                                        }

                                        // Trim trailing whitespace and ensure single trailing newline
                                        outputToWrite = outputToWrite.TrimEnd('\r', '\n', ' ', '\t');
                                        if (!string.IsNullOrEmpty(outputToWrite))
                                        {
                                            outputToWrite += Environment.NewLine;
                                        }
                                    }

                                    // Write the new output
                                    if (!string.IsNullOrEmpty(outputToWrite))
                                    {
                                        AnsiConsole.Write(outputToWrite);
                                    }

                                    // Update the live display
                                    ctx.UpdateTarget(this.RenderPrompt());
                                }
                                else if (initial)
                                {
                                    ctx.UpdateTarget(this.RenderPrompt());
                                    initial = false;
                                }

                                // Check for keyboard input (only process if not disconnected)
                                if (Console.KeyAvailable)
                                {
                                    var keyInfo = Console.ReadKey(intercept: true);

                                    // Handle Ctrl+X for exit (always allow exit)
                                    if (keyInfo.Key == ConsoleKey.X && keyInfo.Modifiers == ConsoleModifiers.Control)
                                    {
                                        await this.ExitSessionAsync();
                                        return; // Exit the Live context
                                    }

                                    // Ignore all other input when disconnected
                                    if (this.isDisconnected)
                                    {
                                        continue;
                                    }

                                    // Handle Alt+X to delete selected history item
                                    if (keyInfo.Key == ConsoleKey.X && keyInfo.Modifiers == ConsoleModifiers.Alt)
                                    {
                                        if (this.showingHistoryMenu && this.historyMenuItems != null && 
                                            this.historyMenuSelectedIndex >= 0 && this.historyMenuSelectedIndex <
                                            this.historyMenuItems.Count)
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
                                    var needsUpdate = true;
                                    switch (keyInfo.Key)
                                    {
                                        case ConsoleKey.Enter:
                                            if (this.showingHistoryMenu && this.historyMenuItems != null && this.historyMenuSelectedIndex >=
                                                0)
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

                                            // Reset cursor blink on input
                                            this.showCursor = true;
                                            this.cursorBlinkCounter = 0;
                                            break;

                                        case ConsoleKey.Delete:
                                            this.HandleDelete();

                                            // Refresh history matches
                                            this.ShowHistoryMenu();

                                            // Reset cursor blink on input
                                            this.showCursor = true;
                                            this.cursorBlinkCounter = 0;
                                            break;

                                        case ConsoleKey.UpArrow:
                                            this.HandleUpArrow();

                                            // Reset cursor blink on navigation
                                            this.showCursor = true;
                                            this.cursorBlinkCounter = 0;
                                            break;

                                        case ConsoleKey.DownArrow:
                                            this.HandleDownArrow();

                                            // Reset cursor blink on navigation
                                            this.showCursor = true;
                                            this.cursorBlinkCounter = 0;
                                            break;

                                        case ConsoleKey.LeftArrow:
                                            this.HandleLeftArrow(keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));

                                            // Reset cursor blink on navigation
                                            this.showCursor = true;
                                            this.cursorBlinkCounter = 0;
                                            break;

                                        case ConsoleKey.RightArrow:
                                            this.HandleRightArrow(keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));

                                            // Reset cursor blink on navigation
                                            this.showCursor = true;
                                            this.cursorBlinkCounter = 0;
                                            break;

                                        case ConsoleKey.Home:
                                            this.HandleHome(keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));

                                            // Reset cursor blink
                                            this.showCursor = true;
                                            this.cursorBlinkCounter = 0;
                                            break;

                                        case ConsoleKey.End:
                                            this.HandleEnd(keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));

                                            // Reset cursor blink
                                            this.showCursor = true;
                                            this.cursorBlinkCounter = 0;
                                            break;

                                        case ConsoleKey.Escape:
                                            this.HandleEscape();

                                            // Reset cursor blink
                                            this.showCursor = true;
                                            this.cursorBlinkCounter = 0;
                                            break;

                                        case ConsoleKey.Tab:

                                            // Check if derived class wants to handle it
                                            if (!await this.HandleSpecialKeyAsync(keyInfo, sessionToken))
                                            {
                                                // Default: send to device
                                                await this.sshConnection.WriteLineAsync($"{this.currentLine}\t");
                                            }
                                            needsUpdate = false;
                                            break;

                                        default:
                                            if (!char.IsControl(keyInfo.KeyChar))
                                            {
                                                // If there's a selection, delete it first
                                                if (this.selectionStart >= 0 && this.selectionEnd >= 0)
                                                {
                                                    var start = Math.Min(this.selectionStart, this.selectionEnd);
                                                    var end = Math.Max(this.selectionStart, this.selectionEnd);
                                                    var deleteCount = end - start;
                                                    this.currentLine = this.currentLine.Remove(start, deleteCount);
                                                    this.cursorPosition = start;
                                                    this.selectionStart = -1;
                                                    this.selectionEnd = -1;
                                                }

                                                // Insert character at cursor position
                                                this.currentLine = this.currentLine.Insert(this.cursorPosition, keyInfo.KeyChar.ToString());
                                                this.cursorPosition++;

                                                // Refresh history matches automatically
                                                this.ShowHistoryMenu();

                                                // Reset cursor blink on input
                                                this.showCursor = true;
                                                this.cursorBlinkCounter = 0;
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
                                    // Blink cursor every ~500ms (10 iterations * 50ms)
                                    this.cursorBlinkCounter++;
                                    if (this.cursorBlinkCounter >= 10)
                                    {
                                        this.showCursor = !this.showCursor;
                                        this.cursorBlinkCounter = 0;
                                        ctx.UpdateTarget(this.RenderPrompt());
                                    }

                                    await Task.Delay(50, sessionToken);
                                }
                            }
                        });

                    // If we exited because of disconnection, wait for reconnection
                    if (this.shouldExitLiveMode)
                    {
                        this.shouldExitLiveMode = false;
                        inLiveMode = false;

                        // Wait for reconnection
                        while (this.isDisconnected && !sessionToken.IsCancellationRequested)
                        {
                            await Task.Delay(100, sessionToken);
                        }

                        // After reconnection, re-enter live mode (without showing initial prompt)
                        if (!sessionToken.IsCancellationRequested)
                        {
                            inLiveMode = true;
                            initial = false; // Don't show prompt immediately - wait for first output or user input
                        }
                    }

                    // If we exited because of nested command execution, handle it
                    else if (this.isExecutingNestedCommand && this.pendingNestedCommand != null)
                    {
                        // Clear buffer immediately to prevent any accumulated output from showing
                        lock (this.outputBuffer)
                        {
                            this.outputBuffer.Clear();
                        }

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
                            // Clear any buffered output that accumulated during nested command execution
                            // This prevents stray prompts and blank lines from appearing when returning to Live mode
                            lock (this.outputBuffer)
                            {
                                this.outputBuffer.Clear();
                            }

                            // Mark execution complete and go back to live mode (without extra spacing)
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

            // Store the original typed value before activating menu
            if (!this.showingHistoryMenu && (this.historyMenuItems == null || this.historyMenuItems.Count == 0))
            {
                this.originalTypedValue = this.currentLine;
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

        private async Task SubmitCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (this.sshConnection == null || string.IsNullOrWhiteSpace(command))
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

                // Don't echo the command - the nested command will handle its own output
                // This prevents duplicate command lines in the output

                // Queue the nested command for execution AFTER Live display exits
                this.pendingNestedCommand = nestedCommand;

                // Set flag to trigger Live display exit
                this.isExecutingNestedCommand = true;

                // Return immediately - Live display will exit, then command will execute
                return;
            }

            // Apply command mappings if available
            var mappedCommand = this.ApplyCommandMapping(command);

            // Echo the original command (not the mapped one) for user clarity
            AnsiConsole.WriteLine($"{this.Prompt ?? "ACT>"} {command}");

            // Send the mapped command to device
            await this.sshConnection.WriteLineAsync(mappedCommand);
            this.commandHistory?.AddCommand(command);
        }
    }
}
