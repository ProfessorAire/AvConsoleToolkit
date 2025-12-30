// <copyright file="FileTextEditor.cs">
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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.Configuration;
using AvConsoleToolkit.Connections;
using Spectre.Console;
using TextCopy;

namespace AvConsoleToolkit.Editors
{
    /// <summary>
    /// A built-in nano-like text editor that operates in an alternate screen buffer.
    /// Provides text editing capabilities with keyboard navigation, selection, and configurable display options.
    /// </summary>
    public sealed class FileTextEditor
    {
        private const int MaxUndoHistory = 100;

        private readonly string displayName;

        private readonly string filePath;

        private readonly FileTextEditorKeyBindings keyBindings;

        private readonly List<StringBuilder> lines = [];

        private readonly Func<Task> onSaveCallback;

        // Redo stack - stores states that were undone
        private readonly Stack<UndoState> redoStack = new();

        private readonly IBuiltInEditorSettings settings;

        private readonly Lock syncRoot = new();

        // Undo stack - stores full document state and cursor position
        private readonly Stack<UndoState> undoStack = new();

        private string? connectionStatus;

        private SearchReplaceFocus currentFocus = SearchReplaceFocus.Editor;

        private int currentSearchMatchIndex = -1;

        private FileTextEditorTheme currentTheme;

        private int cursorCol;

        private int cursorRow;

        private int detectedTabDepth = -1;

        private bool handlingKeysInSecondaryProcess;

        private bool hasSelection;

        // Header colors (may be overridden by file extension)
        private Style headerStyle;

        private bool helpScreenVisible;

        private int helpScrollOffset;

        private bool modified;

        private bool needsRender;

        private int replaceCursorPosition;

        private bool replacePaneVisible;

        private string replaceText = string.Empty;

        private bool running;

        private int scrollOffsetX;

        private int scrollOffsetY;

        private int searchCursorPosition;

        private List<(int Row, int Col)> searchMatches = new();

        // Search/Replace state
        private bool searchPaneVisible;

        private string searchText = string.Empty;

        private int selectionEndCol = -1;

        private int selectionEndRow = -1;

        private int selectionStartCol = -1;

        // Selection state
        private int selectionStartRow = -1;

        // Display settings
        private bool showLineNumbers;

        private string statusMessage = string.Empty;

        private DateTime statusMessageTime = DateTime.MinValue;

        private int tabDepth;

        private int uploadProgress = -1;

        private bool verbose;

        private bool wordWrapEnabled;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileTextEditor"/> class.
        /// </summary>
        /// <param name="filePath">Path to the local file to edit.</param>
        /// <param name="displayName">Display name shown in the editor header.</param>
        /// <param name="onSaveCallback">Callback invoked when the file is saved.</param>
        /// <param name="keyBindings">Optional custom key bindings.</param>
        public FileTextEditor(string filePath, string displayName, Func<Task> onSaveCallback, FileTextEditorKeyBindings? keyBindings = null, bool verbose = false)
        {
            this.filePath = filePath;
            this.displayName = displayName;
            this.onSaveCallback = onSaveCallback;
            this.keyBindings = keyBindings ?? FileTextEditorKeyBindings.Default;
            this.verbose = verbose;
            this.settings = AppConfig.Settings.BuiltInEditor;

            // Load settings
            this.showLineNumbers = this.settings.ShowLineNumbers;
            this.wordWrapEnabled = this.settings.WordWrapEnabled;
            this.tabDepth = this.settings.TabDepth;

            // Initialize theme
            this.currentTheme = FileTextEditorTheme.User;
            this.headerStyle = this.currentTheme.GetHeaderForFile(this.filePath);
            this.SelectTheme();
        }

        /// <summary>
        /// Represents the current focus in search/replace mode.
        /// </summary>
        private enum SearchReplaceFocus
        {
            /// <summary>
            /// Focus is on the text editor.
            /// </summary>
            Editor,

            /// <summary>
            /// Focus is on the search text box.
            /// </summary>
            Search,

            /// <summary>
            /// Focus is on the replace text box.
            /// </summary>
            Replace,
        }

        /// <summary>
        /// Gets or sets the current upload progress (0-100). Set to -1 to hide the progress bar.
        /// </summary>
        public int UploadProgress
        {
            get
            {
                return this.uploadProgress;
            }
            set
            {
                this.uploadProgress = value;
                if (value == 100)
                {
                    this.SetStatusMessage($"File Saved -> File Uploaded");
                }

                this.needsRender = true;
            }
        }

        /// <summary>
        /// Gets or sets the current theme used by the editor.
        /// </summary>
        private FileTextEditorTheme CurrentTheme
        {
            get
            {
                return this.currentTheme;
            }
            set
            {
                this.currentTheme = value;
            }
        }

        /// <summary>
        /// Runs the editor session.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if changes were saved; false if discarded.</returns>
        public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            this.LoadFile();
            this.running = true;
            var lastWindowWidth = Console.WindowWidth;
            var lastWindowHeight = Console.WindowHeight;

            // Save original console state and enable Ctrl+C as input
            var originalTreatControlCAsInput = Console.TreatControlCAsInput;
            Console.TreatControlCAsInput = true;

            try
            {
                AnsiConsole.AlternateScreen(async () =>
                {
                    // Initial render
                    this.Render();

                    while (this.running && !cancellationToken.IsCancellationRequested)
                    {
                        // Check for window resize
                        var currentWidth = Console.WindowWidth;
                        var currentHeight = Console.WindowHeight;
                        if (currentWidth != lastWindowWidth || currentHeight != lastWindowHeight)
                        {
                            lastWindowWidth = currentWidth;
                            lastWindowHeight = currentHeight;
                            this.needsRender = true;
                        }

                        if (Console.KeyAvailable && !this.handlingKeysInSecondaryProcess)
                        {
                            var key = Console.ReadKey(true);
                            _ = this.HandleKeyAsync(key, cancellationToken);
                            this.needsRender = true;
                        }
                        else
                        {
                            Thread.Sleep(50);
                        }

                        if (this.needsRender)
                        {
                            this.Render();
                            this.needsRender = false;
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception)
            {
                // Ensure we always restore console state even on exceptions
            }
            finally
            {
                // Restore original console state
                Console.TreatControlCAsInput = originalTreatControlCAsInput;
            }

            return !this.modified;
        }

        /// <summary>
        /// Updates the connection status displayed in the status bar.
        /// </summary>
        /// <param name="status">The connection status to display.</param>
        public void UpdateConnectionStatus(ConnectionStatus status)
        {
            _ = status switch
            {
                ConnectionStatus.Connected => Color.Lime,
                ConnectionStatus.Connecting => Color.Yellow,
                _ => Color.Red
            };

            this.connectionStatus = status == ConnectionStatus.Connected ? "✓" : "X";
            this.needsRender = true;
        }

        /// <summary>
        /// Calculates the greatest common divisor (GCD) of two integers using the Euclidean algorithm.
        /// </summary>
        /// <remarks>The result is always non-negative, regardless of the sign of the input
        /// values.</remarks>
        /// <param name="a">The first integer value. Can be positive, negative, or zero.</param>
        /// <param name="b">The second integer value. Can be positive, negative, or zero.</param>
        /// <returns>The greatest common divisor of the two specified integers. If both values are zero, returns zero.</returns>
        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                var temp = b;
                b = a % b;
                a = temp;
            }

            return a;
        }

        /// <summary>
        /// Parses a hexadecimal color string in the format RRGGBB and returns the corresponding Color value.
        /// </summary>
        /// <remarks>The method ignores a leading '#' character if present. If the input string does not
        /// represent a valid 6-digit hexadecimal color, the defaultColor is returned.</remarks>
        /// <param name="hex">A string containing a hexadecimal color code in the format RRGGBB, optionally prefixed with '#'.</param>
        /// <param name="defaultColor">The Color value to return if the input string is null, empty, or not a valid hexadecimal color.</param>
        /// <returns>A Color value corresponding to the parsed hexadecimal color code, or the specified defaultColor if parsing
        /// fails.</returns>
        private static Color ParseHexColor(string hex, Color defaultColor)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return defaultColor;
            }

            hex = hex.TrimStart('#');
            if (hex.Length == 6 &&
                byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return new Color(r, g, b);
            }

            return defaultColor;
        }

        /// <summary>
        /// Clears the current selection, resetting all selection-related state to indicate that no selection is active.
        /// </summary>
        private void ClearSelection()
        {
            this.hasSelection = false;
            this.selectionStartRow = -1;
            this.selectionStartCol = -1;
            this.selectionEndRow = -1;
            this.selectionEndCol = -1;
        }

        /// <summary>
        /// Closes the search and replace panes and resets related search state.
        /// </summary>
        /// <remarks>Call this method to hide both the search and replace interfaces and clear any active
        /// search results. After calling this method, the editor regains focus and any previous search context is
        /// discarded.</remarks>
        private void CloseSearchReplace()
        {
            this.searchPaneVisible = false;
            this.replacePaneVisible = false;
            this.currentFocus = SearchReplaceFocus.Editor;
            this.searchMatches.Clear();
            this.currentSearchMatchIndex = -1;
        }

        /// <summary>
        /// Cycles through the available application themes in a predefined order and applies the next theme.
        /// </summary>
        private void CycleTheme()
        {
            // Toggle between Dark and Bright themes
            var themeName = this.settings.ThemeName ?? "User";
            switch (themeName)
            {
                case "User":
                    themeName = "NordDark";
                    break;
                case "NordDark":
                    themeName = "NordSemiDark";
                    break;
                case "NordSemiDark":
                    themeName = "NordSemiLight";
                    break;
                case "NordSemiLight":
                    themeName = "NordLight";
                    break;
                default:
                    themeName = "User";
                    break;
            }

            this.settings.ThemeName = themeName;
            this.SelectTheme();
        }

        /// <summary>
        /// Deletes the currently selected text from the document, if a selection exists.
        /// </summary>
        /// <remarks>After deletion, the cursor is placed at the start of the former selection, and the
        /// selection is cleared. If no text is selected, this method performs no action. The operation is recorded for
        /// undo functionality.</remarks>
        private void DeleteSelection()
        {
            if (!this.hasSelection)
            {
                return;
            }

            this.SaveUndoState();

            var (startRow, startCol, endRow, endCol) = this.GetNormalizedSelection();

            if (startRow == endRow)
            {
                // Single line deletion
                this.lines[startRow].Remove(startCol, endCol - startCol);
            }
            else
            {
                // Multi-line deletion
                var endPart = this.lines[endRow].ToString().Substring(endCol);
                this.lines[startRow].Remove(startCol, this.lines[startRow].Length - startCol);
                this.lines[startRow].Append(endPart);

                // Remove intermediate lines
                for (int i = endRow; i > startRow; i--)
                {
                    this.lines.RemoveAt(i);
                }
            }

            this.cursorRow = startRow;
            this.cursorCol = startCol;
            this.ClearSelection();
            this.modified = true;
        }

        /// <summary>
        /// Analyzes the provided lines of text to detect the most likely number of spaces used for indentation per
        /// level, based on leading spaces in non-empty lines.
        /// </summary>
        /// <remarks>This method assumes that indentation is done using spaces only. If any line contains
        /// a leading tab character, the method returns 0 to indicate that tab-based indentation was detected. If no
        /// indented lines are found, or if a consistent indentation depth cannot be determined, the method returns
        /// -1.</remarks>
        /// <param name="content">An array of strings representing the lines of text to analyze for indentation depth.</param>
        /// <returns>The number of spaces per indentation level if a consistent depth is detected and no tabs are present; 0 if
        /// any line uses tab characters for indentation; or -1 if no indentation could be determined.</returns>
        private int DetectTabDepth(string[] content)
        {
            var indentCounts = new Dictionary<int, int>();

            foreach (var line in content)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var leadingSpaces = 0;
                foreach (var c in line)
                {
                    if (c == ' ')
                    {
                        leadingSpaces++;
                    }
                    else if (c == '\t')
                    {
                        // If tabs are used, return 0 to indicate no detection
                        return 0;
                    }
                    else
                    {
                        break;
                    }
                }

                if (leadingSpaces > 0)
                {
                    if (!indentCounts.ContainsKey(leadingSpaces))
                    {
                        indentCounts[leadingSpaces] = 0;
                    }

                    indentCounts[leadingSpaces]++;
                }
            }

            if (indentCounts.Count == 0)
            {
                return -1;
            }

            // Find GCD of all indent levels
            var gcd = indentCounts.Keys.Aggregate(Gcd);
            return gcd > 0 && gcd <= 8 ? gcd : -1;
        }

        /// <summary>
        /// Moves the selection to the next occurrence of the current search text within the document.
        /// </summary>
        /// <remarks>If no search text is specified or no matches are found, the method updates the status
        /// message accordingly and does not change the selection. When the selection is already on a match, calling
        /// this method advances to the next match, wrapping to the first match if necessary. The method also updates
        /// the status message to indicate the current match position.</remarks>
        private void FindNext()
        {
            if (string.IsNullOrEmpty(this.searchText))
            {
                this.SetStatusMessage("No search text");
                return;
            }

            if (this.searchMatches.Count == 0)
            {
                this.SetStatusMessage("No matches found");
                return;
            }

            // If we're already at a match (selection exists and matches search text), move to next
            // Otherwise, go to the current index (first find)
            var shouldMoveToNext = false;
            if (this.hasSelection && this.currentSearchMatchIndex >= 0 && this.currentSearchMatchIndex <
                this.searchMatches.Count)
            {
                var currentMatch = this.searchMatches[this.currentSearchMatchIndex];
                if (this.cursorRow == currentMatch.Row && this.cursorCol == currentMatch.Col)
                {
                    shouldMoveToNext = true;
                }
            }

            if (shouldMoveToNext)
            {
                this.currentSearchMatchIndex = (this.currentSearchMatchIndex + 1) % this.searchMatches.Count;
            }

            var match = this.searchMatches[this.currentSearchMatchIndex];

            // Move cursor to match and select it
            this.cursorRow = match.Row;
            this.cursorCol = match.Col;
            this.selectionStartRow = match.Row;
            this.selectionStartCol = match.Col;
            this.selectionEndRow = match.Row;
            this.selectionEndCol = match.Col + this.searchText.Length;
            this.hasSelection = true;

            // Scroll selection into view
            this.ScrollSelectionIntoView();

            this.SetStatusMessage($"Match {this.currentSearchMatchIndex + 1} of {this.searchMatches.Count}");
        }

        /// <summary>
        /// Returns the current selection coordinates with the start and end positions ordered from top-left to
        /// bottom-right.
        /// </summary>
        /// <remarks>Use this method to obtain selection coordinates in a consistent order, regardless of
        /// the direction in which the selection was made.</remarks>
        /// <returns>A tuple containing the normalized selection coordinates: (startRow, startCol, endRow, endCol), where
        /// (startRow, startCol) is the upper-left corner and (endRow, endCol) is the lower-right corner of the
        /// selection.</returns>
        private (int startRow, int startCol, int endRow, int endCol) GetNormalizedSelection()
        {
            if (this.selectionStartRow < this.selectionEndRow ||
                this.selectionStartRow == this.selectionEndRow && this.selectionStartCol <= this.selectionEndCol)
            {
                return (this.selectionStartRow, this.selectionStartCol, this.selectionEndRow, this.selectionEndCol);
            }

            return (this.selectionEndRow, this.selectionEndCol, this.selectionStartRow, this.selectionStartCol);
        }

        /// <summary>
        /// Retrieves the text currently selected in the editor.
        /// </summary>
        /// <remarks>The returned text preserves line breaks as they appear in the selection. If the
        /// selection spans multiple lines, each line is separated by a line break.</remarks>
        /// <returns>A string containing the selected text. Returns an empty string if no text is selected.</returns>
        private string GetSelectedText()
        {
            if (!this.hasSelection)
            {
                return string.Empty;
            }

            var (startRow, startCol, endRow, endCol) = this.GetNormalizedSelection();
            var sb = new StringBuilder();

            for (int row = startRow; row <= endRow; row++)
            {
                var line = this.lines[row].ToString();
                var start = row == startRow ? startCol : 0;
                var end = row == endRow ? endCol : line.Length;

                sb.Append(line.Substring(start, end - start));
                if (row < endRow)
                {
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Retrieves the current status message if it is recent.
        /// </summary>
        /// <remarks>Use this method to obtain a status message that is only considered valid for a short
        /// period after it is set. If the message is outdated, the method returns an empty string.</remarks>
        /// <returns>A string containing the current status message if it was set within the last three seconds; otherwise, an
        /// empty string.</returns>
        private string GetStatusMessage()
        {
            if (DateTime.Now - this.statusMessageTime < TimeSpan.FromSeconds(3))
            {
                return this.statusMessage;
            }

            return string.Empty;
        }

        /// <summary>
        /// Handles the backspace operation at the current cursor position, removing a character or merging lines as
        /// appropriate.
        /// </summary>
        /// <remarks>This method updates the text buffer and cursor position to reflect the effect of a
        /// backspace key press. If the cursor is at the start of a line, the current line is merged with the previous
        /// line. The method also records the operation for undo functionality and marks the buffer as
        /// modified.</remarks>
        private void HandleBackspace()
        {
            if (this.cursorCol > 0)
            {
                this.SaveUndoState();
                this.lines[this.cursorRow].Remove(this.cursorCol - 1, 1);
                this.cursorCol--;
                this.modified = true;
            }
            else if (this.cursorRow > 0)
            {
                this.SaveUndoState();
                var currentLine = this.lines[this.cursorRow].ToString();
                this.lines.RemoveAt(this.cursorRow);
                this.cursorRow--;
                this.cursorCol = this.lines[this.cursorRow].Length;
                this.lines[this.cursorRow].Append(currentLine);
                this.modified = true;
            }
        }

        /// <summary>
        /// Copies the currently selected text to the clipboard if a selection exists and updates the status message
        /// accordingly.
        /// </summary>
        /// <remarks>If no text is selected, the method updates the status message to indicate that there
        /// is no selection to copy.</remarks>
        private void HandleCopy()
        {
            if (this.hasSelection)
            {
                ClipboardService.SetText(this.GetSelectedText());
                this.SetStatusMessage("Text copied.");
            }
            else
            {
                this.SetStatusMessage("No selection to copy.");
            }
        }

        /// <summary>
        /// Handles the cut operation by removing the selected text and placing it on the clipboard.
        /// </summary>
        /// <remarks>If no text is selected, the method does not modify the clipboard and displays a
        /// status message indicating that there is no selection to cut.</remarks>
        private void HandleCut()
        {
            if (this.hasSelection)
            {
                this.SaveUndoState();
                ClipboardService.SetText(this.GetSelectedText());
                this.DeleteSelection();
                this.SetStatusMessage("Text cut.");
            }
            else
            {
                this.SetStatusMessage("No selection to cut.");
            }
        }

        /// <summary>
        /// Cuts the current line at the cursor position and copies it to the clipboard, removing the line from the
        /// document.
        /// </summary>
        /// <remarks>If the document contains only one line, the line is cleared instead of being removed.
        /// After the operation, the cursor is repositioned to a valid location, and any active selection is cleared.
        /// The document is marked as modified.</remarks>
        private void HandleCutLine()
        {
            this.SaveUndoState();
            ClipboardService.SetText($"{this.lines[this.cursorRow]}{Environment.NewLine}");
            if (this.lines.Count > 1)
            {
                this.lines.RemoveAt(this.cursorRow);
                if (this.cursorRow >= this.lines.Count)
                {
                    this.cursorRow = this.lines.Count - 1;
                }

                this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
            }
            else
            {
                this.lines[this.cursorRow] = new StringBuilder();
                this.cursorCol = 0;
            }

            this.modified = true;
            this.ClearSelection();
            this.SetStatusMessage("Line cut to clipboard.");
        }

        /// <summary>
        /// Handles the delete operation at the current cursor position, removing the character after the cursor or
        /// merging lines as appropriate.
        /// </summary>
        /// <remarks>If the cursor is positioned at the end of a line and there is a subsequent line, this
        /// method merges the next line into the current one. The method also updates the undo state and marks the
        /// content as modified.</remarks>
        private void HandleDelete()
        {
            if (this.cursorCol < this.lines[this.cursorRow].Length)
            {
                this.SaveUndoState();
                this.lines[this.cursorRow].Remove(this.cursorCol, 1);
                this.modified = true;
            }
            else if (this.cursorRow < this.lines.Count - 1)
            {
                this.SaveUndoState();
                var nextLine = this.lines[this.cursorRow + 1].ToString();
                this.lines.RemoveAt(this.cursorRow + 1);
                this.lines[this.cursorRow].Append(nextLine);
                this.modified = true;
            }
        }

        /// <summary>
        /// Handles the insertion of a new line at the current cursor position, updating the text buffer and cursor
        /// accordingly.
        /// </summary>
        /// <remarks>This method is typically called in response to an Enter key press within a text
        /// editing context. It splits the current line at the cursor position, moves the text after the cursor to a new
        /// line, and updates the cursor to the start of the new line. The operation also marks the buffer as modified
        /// and saves the current state for undo functionality.</remarks>
        private void HandleEnter()
        {
            this.SaveUndoState();
            var currentLine = this.lines[this.cursorRow];
            var afterCursor = currentLine.ToString(this.cursorCol, currentLine.Length - this.cursorCol);
            currentLine.Remove(this.cursorCol, currentLine.Length - this.cursorCol);

            this.cursorRow++;
            this.lines.Insert(this.cursorRow, new StringBuilder(afterCursor));
            this.cursorCol = 0;
            this.modified = true;
        }

        /// <summary>
        /// Handles the exit process, prompting the user to save changes if modifications have been made.
        /// </summary>
        /// <remarks>If there are unsaved changes, the method prompts the user to save, discard, or cancel
        /// the exit. If no response is received within 30 seconds, the operation times out and exits without saving.
        /// The method sets the application's running state to false when the exit process completes or is
        /// cancelled.</remarks>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the exit operation.</param>
        /// <returns>A task that represents the asynchronous exit handling operation.</returns>
        private async Task HandleExitAsync(CancellationToken cancellationToken)
        {
            if (this.modified)
            {
                try
                {
                    this.handlingKeysInSecondaryProcess = true;
                    this.SetStatusMessage("Save changes before exit? (Y)es/(N)o/(C)ancel");
                    this.needsRender = true;

                    var timeout = DateTime.Now.AddSeconds(30);
                    while (DateTime.Now < timeout)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            this.running = false;
                            return;
                        }

                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            switch (char.ToUpperInvariant(key.KeyChar))
                            {
                                case 'Y':
                                    await this.HandleSaveAsync(cancellationToken);
                                    this.running = false;
                                    return;
                                case 'N':
                                    this.running = false;
                                    return;
                                case 'C':
                                    this.SetStatusMessage(string.Empty);
                                    return;
                            }
                        }

                        await Task.Delay(50, cancellationToken);
                    }

                    // Timeout - exit without saving
                    this.SetStatusMessage("Exit confirmation timeout - exiting without save");
                    await Task.Delay(1000, CancellationToken.None);
                }
                finally
                {
                    this.handlingKeysInSecondaryProcess = false;
                }
            }

            this.running = false;
        }

        /// <summary>
        /// Handles user input while the help screen is active, updating the help screen's visibility or scroll position
        /// based on the specified key.
        /// </summary>
        /// <remarks>This method processes navigation and exit commands for the help screen, such as
        /// scrolling with arrow keys or closing the help screen with Escape or Ctrl+Q. It should be called only when
        /// the help screen is currently visible.</remarks>
        /// <param name="key">A structure that describes the console key pressed, including any modifier keys.</param>
        private void HandleHelpInput(ConsoleKeyInfo key)
        {
            var helpLines = this.keyBindings.GetHelpText();
            var windowHeight = Console.WindowHeight;
            var contentHeight = windowHeight - 2; // Header + Footer

            if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.Q)
            {
                this.helpScreenVisible = false;
                this.needsRender = true;
                return;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    this.helpScreenVisible = false;
                    this.needsRender = true;
                    break;

                case ConsoleKey.UpArrow:
                    this.helpScrollOffset = Math.Max(0, this.helpScrollOffset - 1);
                    this.needsRender = true;
                    break;

                case ConsoleKey.DownArrow:
                    var maxScroll = Math.Max(0, helpLines.Length - contentHeight);
                    this.helpScrollOffset = Math.Min(maxScroll, this.helpScrollOffset + 1);
                    this.needsRender = true;
                    break;

                case ConsoleKey.PageUp:
                    var scrollUp = windowHeight - 3;
                    this.helpScrollOffset = Math.Max(0, this.helpScrollOffset - scrollUp);
                    this.needsRender = true;
                    break;

                case ConsoleKey.PageDown:
                    var scrollDown = windowHeight - 3;
                    var maxScrollDown = Math.Max(0, helpLines.Length - contentHeight);
                    this.helpScrollOffset = Math.Min(maxScrollDown, this.helpScrollOffset + scrollDown);
                    this.needsRender = true;
                    break;

                case ConsoleKey.Home:
                    this.helpScrollOffset = 0;
                    this.needsRender = true;
                    break;

                case ConsoleKey.End:
                    var maxScrollEnd = Math.Max(0, helpLines.Length - contentHeight);
                    this.helpScrollOffset = maxScrollEnd;
                    this.needsRender = true;
                    break;
            }
        }

        /// <summary>
        /// Processes a keyboard input event and updates the editor state or performs actions based on the specified key
        /// and current editor context.
        /// </summary>
        /// <remarks>This method interprets the provided key according to the current focus and state of
        /// the editor, including handling help navigation, search/replace input, editor commands, and text navigation
        /// or editing. Some actions may be performed asynchronously, such as saving or exiting. If the operation is
        /// canceled via the provided token, pending asynchronous actions may be aborted.</remarks>
        /// <param name="key">The key information representing the user's keyboard input to handle.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation of handling the key input.</returns>
        private async Task HandleKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
        {
            try
            {
                // If help screen is visible, handle help navigation
                if (this.helpScreenVisible)
                {
                    this.HandleHelpInput(key);
                    return;
                }

                // If focus is on search/replace, handle input there
                if (this.currentFocus != SearchReplaceFocus.Editor)
                {
                    if (await this.HandleSearchReplaceInputAsync(key))
                    {
                        return;
                    }
                }

                // Check for editor actions first
                var action = this.keyBindings.GetAction(key);
                switch (action)
                {
                    case FileTextEditorAction.Exit:
                        await this.HandleExitAsync(cancellationToken);
                        return;
                    case FileTextEditorAction.Save:
                        await this.HandleSaveAsync(cancellationToken);
                        return;
                    case FileTextEditorAction.Copy:
                        this.HandleCopy();
                        return;
                    case FileTextEditorAction.Cut:
                        this.HandleCut();
                        return;
                    case FileTextEditorAction.Paste:
                        this.HandlePaste();
                        return;
                    case FileTextEditorAction.CutLine:
                        this.HandleCutLine();
                        return;
                    case FileTextEditorAction.Help:
                        this.ShowHelp();
                        return;
                    case FileTextEditorAction.ToggleLineNumbers:
                        this.showLineNumbers = !this.showLineNumbers;
                        this.SetStatusMessage($"Line numbers: {(this.showLineNumbers ? "on" : "off")}");
                        return;
                    case FileTextEditorAction.ToggleWordWrap:
                        this.wordWrapEnabled = !this.wordWrapEnabled;
                        this.scrollOffsetX = 0;
                        this.SetStatusMessage($"Word wrap: {(this.wordWrapEnabled ? "on" : "off")}");

                        //  Re-render to apply word wrap changes
                        this.Render();
                        return;
                    case FileTextEditorAction.Undo:
                        this.Undo();
                        return;
                    case FileTextEditorAction.Redo:
                        this.Redo();
                        return;
                    case FileTextEditorAction.CycleTheme:
                        this.CycleTheme();
                        return;
                    case FileTextEditorAction.Search:
                        this.OpenSearch();
                        return;
                    case FileTextEditorAction.Replace:
                        this.OpenReplace();
                        return;
                    case FileTextEditorAction.FindNext:
                        if (this.searchPaneVisible)
                        {
                            this.FindNext();
                        }
                        return;
                    case FileTextEditorAction.ReplaceCurrent:
                        if (this.replacePaneVisible)
                        {
                            this.ReplaceCurrent();
                        }
                        return;
                    case FileTextEditorAction.ReplaceAll:
                        if (this.replacePaneVisible)
                        {
                            this.ReplaceAll();
                        }
                        return;
                }

                // Handle selection (Shift key)
                var isSelecting = key.Modifiers.HasFlag(ConsoleModifiers.Shift);
                var isWordMove = key.Modifiers.HasFlag(ConsoleModifiers.Control);

                if (isSelecting && !this.hasSelection)
                {
                    this.StartSelection();
                }

                // Handle navigation and editing keys
                switch (key.Key)
                {
                    case ConsoleKey.Tab:
                        if (this.currentFocus != SearchReplaceFocus.Editor)
                        {
                            return;
                        }
                        else
                        {
                            if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                            {
                                if (this.TryGetNormalizedSelection(out var sel))
                                {
                                    this.SaveUndoState();

                                    var changed = false;
                                    var cursorLineRemoveCount = 0;
                                    for (var i = sel.startRow; i <= sel.endRow; i++)
                                    {
                                        var line = this.lines[i];
                                        var removeCount = 0;
                                        for (var j = 0; j < this.tabDepth && j < line.Length; j++)
                                        {
                                            if (line[j] == ' ')
                                            {
                                                removeCount++;
                                            }
                                            else
                                            {
                                                break;
                                            }
                                        }

                                        if (removeCount > 0)
                                        {
                                            changed = true;
                                            line.Remove(0, removeCount);

                                            // Track removal for cursor line
                                            if (i == this.cursorRow)
                                            {
                                                cursorLineRemoveCount = removeCount;
                                            }

                                            // Adjust selection boundaries for this line
                                            if (i == sel.startRow)
                                            {
                                                this.selectionStartCol = Math.Max(0, this.selectionStartCol - removeCount);
                                            }

                                            if (i == sel.endRow)
                                            {
                                                this.selectionEndCol = Math.Max(0, this.selectionEndCol - removeCount);
                                            }
                                        }
                                    }

                                    if (changed)
                                    {
                                        this.cursorCol = Math.Max(0, this.cursorCol - cursorLineRemoveCount);
                                        this.modified = true;
                                    }
                                }
                                else
                                {
                                    var line = this.lines[this.cursorRow];
                                    var removeCount = 0;
                                    for (var i = 0; i < this.tabDepth && i < line.Length; i++)
                                    {
                                        if (line[i] == ' ')
                                        {
                                            removeCount++;
                                        }
                                        else
                                        {
                                            break;
                                        }
                                    }

                                    if (removeCount > 0)
                                    {
                                        this.SaveUndoState();
                                        line.Remove(0, removeCount);
                                        this.cursorCol = Math.Max(0, this.cursorCol - removeCount);
                                        this.modified = true;
                                    }
                                }
                            }
                            else if (this.hasSelection)
                            {
                                this.SaveUndoState();

                                var sel = this.GetNormalizedSelection();
                                for (var i = sel.startRow; i <= sel.endRow; i++)
                                {
                                    var line = this.lines[i];
                                    line.Insert(0, " ", this.tabDepth);
                                }

                                this.selectionStartCol += this.tabDepth;
                                this.selectionEndCol += this.tabDepth;
                                this.modified = true;
                                this.needsRender = true;
                            }
                            else
                            {
                                this.DeleteSelection();
                                this.InsertText(new string(' ', this.tabDepth));
                            }
                        }
                        break;

                    case ConsoleKey.Escape:
                        if (this.searchPaneVisible)
                        {
                            // Close search/replace pane but leave selection intact
                            this.CloseSearchReplace();
                            return;
                        }
                        break;

                    case ConsoleKey.UpArrow:
                        this.MoveCursorUp(isSelecting);
                        break;

                    case ConsoleKey.DownArrow:
                        this.MoveCursorDown(isSelecting);
                        break;

                    case ConsoleKey.LeftArrow:
                        if (isWordMove)
                        {
                            this.MoveCursorWordLeft(isSelecting);
                        }
                        else
                        {
                            this.MoveCursorLeft(isSelecting);
                        }

                        break;

                    case ConsoleKey.RightArrow:
                        if (isWordMove)
                        {
                            this.MoveCursorWordRight(isSelecting);
                        }
                        else
                        {
                            this.MoveCursorRight(isSelecting);
                        }

                        break;

                    case ConsoleKey.Home:
                        if (isWordMove)
                        {
                            this.cursorRow = 0;
                            this.cursorCol = 0;
                        }
                        else
                        {
                            this.cursorCol = 0;
                        }

                        this.UpdateSelection(isSelecting);
                        break;

                    case ConsoleKey.End:
                        if (isWordMove)
                        {
                            this.cursorRow = this.lines.Count - 1;
                            this.cursorCol = this.lines[this.cursorRow].Length;
                        }
                        else
                        {
                            this.cursorCol = this.lines[this.cursorRow].Length;
                        }

                        this.UpdateSelection(isSelecting);
                        break;

                    case ConsoleKey.PageUp:
                        var pageUp = Console.WindowHeight - 3;
                        this.cursorRow = Math.Max(0, this.cursorRow - pageUp);
                        this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
                        this.UpdateSelection(isSelecting);
                        break;

                    case ConsoleKey.PageDown:
                        var pageDown = Console.WindowHeight - 3;
                        this.cursorRow = Math.Min(this.lines.Count - 1, this.cursorRow + pageDown);
                        this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
                        this.UpdateSelection(isSelecting);
                        break;

                    case ConsoleKey.Enter:
                        this.DeleteSelection();
                        this.HandleEnter();
                        break;

                    case ConsoleKey.Backspace:
                        if (this.hasSelection)
                        {
                            this.DeleteSelection();
                        }
                        else
                        {
                            this.HandleBackspace();
                        }

                        break;

                    case ConsoleKey.Delete:
                        if (this.hasSelection)
                        {
                            this.DeleteSelection();
                        }
                        else
                        {
                            this.HandleDelete();
                        }

                        break;

                    case ConsoleKey.A:
                        if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
                        {
                            this.SelectAll();
                        }
                        else if (!char.IsControl(key.KeyChar))
                        {
                            this.DeleteSelection();
                            this.InsertChar(key.KeyChar);
                        }

                        break;

                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            this.DeleteSelection();
                            this.InsertChar(key.KeyChar);
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                if (this.verbose)
                {
                    AnsiConsole.Write(new Text($"Error while processing key-stroke '{key.Key}'.{Environment.NewLine}", new Style(Color.Red)));
                    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                }
            }
        }

        /// <summary>
        /// Handles pasting text from the clipboard into the current document, replacing any selected content with the
        /// clipboard contents.
        /// </summary>
        /// <remarks>If the clipboard contains multiple lines, each line is inserted as a separate line in
        /// the document. If the clipboard is empty or null, no changes are made to the document and a status message is
        /// displayed.</remarks>
        private void HandlePaste()
        {
            var clipboardData = ClipboardService.GetText();
            if (!string.IsNullOrEmpty(clipboardData))
            {
                this.DeleteSelection();

                var clipboardLines = clipboardData.Split(["\r\n", "\n"], StringSplitOptions.None);
                for (var i = 0; i < clipboardLines.Length; i++)
                {
                    if (i > 0)
                    {
                        this.HandleEnter();
                    }

                    this.InsertText(clipboardLines[i]);
                }

                this.SetStatusMessage("Text pasted.");
            }
            else
            {
                this.SetStatusMessage("Clipboard is empty.");
            }
        }

        /// <summary>
        /// Performs the save operation and invokes the associated save callback asynchronously.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous save operation.</param>
        /// <returns>A task that represents the asynchronous save operation.</returns>
        private async Task HandleSaveAsync(CancellationToken cancellationToken)
        {
            this.SaveFile();

            try
            {
                await this.onSaveCallback();
            }
            catch (Exception ex)
            {
                this.SetStatusMessage($"Upload failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Processes a keyboard input event for the search or replace text box and updates the state accordingly.
        /// </summary>
        /// <remarks>This method handles navigation keys, text editing, and focus changes within the
        /// search and replace UI. It should be called when the search or replace input box has focus to process user
        /// input appropriately.</remarks>
        /// <param name="key">A <see cref="ConsoleKeyInfo"/> structure representing the key that was pressed.</param>
        /// <returns>true if the key was handled by the search or replace input logic; otherwise, false.</returns>
        private async Task<bool> HandleSearchReplaceInputAsync(ConsoleKeyInfo key)
        {
            var isSearch = this.currentFocus == SearchReplaceFocus.Search;
            ref var text = ref (isSearch ? ref this.searchText : ref this.replaceText);
            ref var cursorPos = ref (isSearch ? ref this.searchCursorPosition : ref this.replaceCursorPosition);

            // Check for Tab/Shift+Tab/Escape
            if (key.Key == ConsoleKey.Tab)
            {
                // Handle Shift+Tab to go backwards
                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                {
                    if (this.currentFocus == SearchReplaceFocus.Replace)
                    {
                        this.currentFocus = SearchReplaceFocus.Search;
                    }
                    else if (this.currentFocus == SearchReplaceFocus.Search)
                    {
                        this.currentFocus = SearchReplaceFocus.Editor;
                    }
                }
                else
                {
                    // Tab forward
                    if (this.currentFocus == SearchReplaceFocus.Search)
                    {
                        if (this.replacePaneVisible)
                        {
                            this.currentFocus = SearchReplaceFocus.Replace;
                        }
                        else
                        {
                            this.currentFocus = SearchReplaceFocus.Editor;
                        }
                    }
                    else if (this.currentFocus == SearchReplaceFocus.Replace)
                    {
                        this.currentFocus = SearchReplaceFocus.Editor;
                    }
                }

                return true;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                // Close search/replace and return to editor
                this.CloseSearchReplace();
                return true;
            }

            // Handle text input in search/replace boxes
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    if (isSearch)
                    {
                        this.FindNext();
                    }
                    else
                    {
                        this.ReplaceCurrent();
                    }
                    return true;

                case ConsoleKey.Backspace:
                    if (cursorPos > 0)
                    {
                        text = text.Remove(cursorPos - 1, 1);
                        cursorPos--;
                        if (isSearch)
                        {
                            this.UpdateSearchMatches();
                        }
                    }
                    return true;

                case ConsoleKey.Delete:
                    if (cursorPos < text.Length)
                    {
                        text = text.Remove(cursorPos, 1);
                        if (isSearch)
                        {
                            this.UpdateSearchMatches();
                        }
                    }
                    return true;

                case ConsoleKey.LeftArrow:
                    if (cursorPos > 0)
                    {
                        cursorPos--;
                    }
                    return true;

                case ConsoleKey.RightArrow:
                    if (cursorPos < text.Length)
                    {
                        cursorPos++;
                    }
                    return true;

                case ConsoleKey.Home:
                    cursorPos = 0;
                    return true;

                case ConsoleKey.End:
                    cursorPos = text.Length;
                    return true;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        text = text.Insert(cursorPos, key.KeyChar.ToString());
                        cursorPos++;
                        if (isSearch)
                        {
                            this.UpdateSearchMatches();
                        }

                        return true;
                    }
                    break;
            }

            return false;
        }

        /// <summary>
        /// Inserts the specified character at the current cursor position in the text buffer.
        /// </summary>
        /// <param name="c">The character to insert at the current cursor location.</param>
        private void InsertChar(char c)
        {
            this.SaveUndoState();
            this.lines[this.cursorRow].Insert(this.cursorCol, c);
            this.cursorCol++;
            this.modified = true;
        }

        /// <summary>
        /// Inserts the specified text at the current cursor position within the document.
        /// </summary>
        /// <param name="text">The text to insert at the current cursor position. Cannot be null.</param>
        private void InsertText(string text)
        {
            this.SaveUndoState();
            this.lines[this.cursorRow].Insert(this.cursorCol, text);
            this.cursorCol += text.Length;
            this.modified = true;
        }

        /// <summary>
        /// Loads the contents of the file specified by the current file path into the editor, replacing any existing
        /// lines and resetting the editor state.
        /// </summary>
        /// <remarks>If the file does not exist or is empty, the editor is initialized with a single empty
        /// line. The method also attempts to detect the tab depth from the file's contents and updates the editor's tab
        /// settings accordingly. Cursor position, scroll offsets, and selection are reset, and the modified state is
        /// cleared.</remarks>
        private void LoadFile()
        {
            this.lines.Clear();
            if (File.Exists(this.filePath))
            {
                var content = File.ReadAllLines(this.filePath);
                foreach (var line in content)
                {
                    this.lines.Add(new StringBuilder(line));
                }

                // Try to detect tab depth from file
                this.detectedTabDepth = this.DetectTabDepth(content);
                if (this.detectedTabDepth > 0)
                {
                    this.tabDepth = this.detectedTabDepth;
                }
            }

            if (this.lines.Count == 0)
            {
                this.lines.Add(new StringBuilder());
            }

            this.cursorRow = 0;
            this.cursorCol = 0;
            this.scrollOffsetY = 0;
            this.scrollOffsetX = 0;
            this.modified = false;
            this.ClearSelection();
        }

        /// <summary>
        /// Moves the cursor down by one line, optionally extending the current selection.
        /// </summary>
        /// <param name="isSelecting"><see langword="true"/> to extend the current selection while moving the cursor; <see langword="false"/> to move the cursor without modifying the
        /// selection.</param>
        private void MoveCursorDown(bool isSelecting)
        {
            if (this.cursorRow < this.lines.Count - 1)
            {
                this.cursorRow++;
                this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
            }

            this.UpdateSelection(isSelecting);
        }

        /// <summary>
        /// Moves the cursor one position to the left, optionally updating the selection.
        /// </summary>
        /// <param name="isSelecting"><see langword="true"/> to extend the current selection; <see langword="false"/> to move the cursor without modifying the selection.</param>
        private void MoveCursorLeft(bool isSelecting)
        {
            if (this.cursorCol > 0)
            {
                this.cursorCol--;
            }
            else if (this.cursorRow > 0)
            {
                this.cursorRow--;
                this.cursorCol = this.lines[this.cursorRow].Length;
            }

            this.UpdateSelection(isSelecting);
        }

        /// <summary>
        /// Moves the cursor one position to the right, optionally extending the current selection.
        /// </summary>
        /// <param name="isSelecting"><see langword="true"/> to extend the current selection while moving the cursor; otherwise, <see langword="false"/> to clear any selection.</param>
        private void MoveCursorRight(bool isSelecting)
        {
            if (this.cursorCol < this.lines[this.cursorRow].Length)
            {
                this.cursorCol++;
            }
            else if (this.cursorRow < this.lines.Count - 1)
            {
                this.cursorRow++;
                this.cursorCol = 0;
            }

            this.UpdateSelection(isSelecting);
        }

        /// <summary>
        /// Moves the cursor up by one line, optionally extending the current selection.
        /// </summary>
        /// <param name="isSelecting"><see langword="true"/> to extend the current selection while moving the cursor; <see langword="false"/> to move the cursor without modifying the
        /// selection.</param>
        private void MoveCursorUp(bool isSelecting)
        {
            if (this.cursorRow > 0)
            {
                this.cursorRow--;
                this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
            }

            this.UpdateSelection(isSelecting);
        }

        /// <summary>
        /// Moves the cursor to the beginning of the previous word in the text editor, optionally extending the current
        /// selection.
        /// </summary>
        /// <param name="isSelecting"><see langword="true"/> to extend the current selection to the new cursor position; <see langword="false"/> to move the cursor without modifying
        /// the selection.</param>
        private void MoveCursorWordLeft(bool isSelecting)
        {
            if (this.cursorCol == 0 && this.cursorRow > 0)
            {
                this.cursorRow--;
                this.cursorCol = this.lines[this.cursorRow].Length;
            }
            else
            {
                var line = this.lines[this.cursorRow].ToString();
                var pos = this.cursorCol - 1;

                // Skip whitespace
                while (pos > 0 && char.IsWhiteSpace(line[pos]))
                {
                    pos--;
                }

                // Skip word characters
                while (pos > 0 && !char.IsWhiteSpace(line[pos - 1]))
                {
                    pos--;
                }

                this.cursorCol = Math.Max(0, pos);
            }

            this.UpdateSelection(isSelecting);
        }

        /// <summary>
        /// Moves the cursor to the beginning of the next word to the right, optionally extending the current selection.
        /// </summary>
        /// <param name="isSelecting"><see langword="true"/> to extend the current selection to the new cursor position; <see langword="false"/> to move the cursor without selecting
        /// text.</param>
        private void MoveCursorWordRight(bool isSelecting)
        {
            var line = this.lines[this.cursorRow].ToString();

            if (this.cursorCol >= line.Length && this.cursorRow < this.lines.Count - 1)
            {
                this.cursorRow++;
                this.cursorCol = 0;
            }
            else
            {
                var pos = this.cursorCol;

                // Skip current word
                while (pos < line.Length && !char.IsWhiteSpace(line[pos]))
                {
                    pos++;
                }

                // Skip whitespace
                while (pos < line.Length && char.IsWhiteSpace(line[pos]))
                {
                    pos++;
                }

                this.cursorCol = pos;
            }

            this.UpdateSelection(isSelecting);
        }

        /// <summary>
        /// Opens the replace pane and prepares the search and replace interface for user input.
        /// </summary>
        /// <remarks>If the search pane is not already open and there is a current selection in the
        /// editor, the selected text is used to initialize the search text. The method also ensures that the search
        /// pane is focused and updates the current search matches.</remarks>
        private void OpenReplace()
        {
            this.searchPaneVisible = true;
            this.replacePaneVisible = true;

            // If search pane wasn't already open, initialize search text from selection
            if (this.currentFocus == SearchReplaceFocus.Editor)
            {
                this.currentFocus = SearchReplaceFocus.Search;

                if (this.hasSelection)
                {
                    var selectedText = this.GetSelectedText();
                    if (!selectedText.Contains('\n') && !selectedText.Contains('\r'))
                    {
                        this.searchText = selectedText;
                        this.searchCursorPosition = this.searchText.Length;
                    }
                }
            }
            else
            {
                // Search was already open, just switch to search focus
                this.currentFocus = SearchReplaceFocus.Search;
            }

            this.UpdateSearchMatches();
        }

        /// <summary>
        /// Displays the search pane and initializes the search input, optionally using the current selection as the
        /// initial search text.
        /// </summary>
        /// <remarks>If a text selection exists and does not span multiple lines, the selected text is
        /// used as the initial search query. The search pane receives focus, and search matches are updated to reflect
        /// the current input.</remarks>
        private void OpenSearch()
        {
            this.searchPaneVisible = true;
            this.currentFocus = SearchReplaceFocus.Search;

            // If there's a selection, use it as the initial search text
            if (this.hasSelection)
            {
                var selectedText = this.GetSelectedText();
                if (!selectedText.Contains('\n') && !selectedText.Contains('\r'))
                {
                    this.searchText = selectedText;
                    this.searchCursorPosition = this.searchText.Length;
                }
            }

            this.UpdateSearchMatches();
        }

        /// <summary>
        /// Performs the most recent undone action, restoring the editor state to its next redoable state if available.
        /// </summary>
        /// <remarks>If there are no actions available to redo, the method does nothing and displays a
        /// status message indicating that there is nothing to redo. This method updates the undo stack to allow the
        /// redone action to be undone again.</remarks>
        private void Redo()
        {
            if (this.redoStack.Count == 0)
            {
                this.SetStatusMessage("Nothing to redo.");
                return;
            }

            // Save current state to undo stack before redoing
            var currentLinesCopy = new string[this.lines.Count];
            for (var i = 0; i < this.lines.Count; i++)
            {
                currentLinesCopy[i] = this.lines[i].ToString();
            }
            var currentState = new UndoState(currentLinesCopy, this.cursorRow, this.cursorCol, this.modified);
            this.undoStack.Push(currentState);

            var state = this.redoStack.Pop();

            // Restore lines efficiently
            this.lines.Clear();
            this.lines.Capacity = state.Lines.Length;
            foreach (var line in state.Lines)
            {
                this.lines.Add(new StringBuilder(line));
            }

            this.cursorRow = Math.Min(state.CursorRow, this.lines.Count - 1);
            this.cursorCol = Math.Min(state.CursorCol, this.lines[this.cursorRow].Length);
            this.modified = state.Modified;
            this.ClearSelection();
            this.SetStatusMessage("Redo.");
        }

        /// <summary>
        /// Renders the editor interface, including the header, content area, status bar, and help bar, to the console
        /// window.
        /// </summary>
        private void Render()
        {
            // Don't render if we're shutting down
            if (!this.running)
            {
                return;
            }

            lock (this.syncRoot)
            {
                // Double-check after acquiring lock
                if (!this.running)
                {
                    return;
                }

                // If help screen is visible, render that instead
                if (this.helpScreenVisible)
                {
                    this.RenderHelp();
                    return;
                }

                var windowWidth = Console.WindowWidth;
                var windowHeight = Console.WindowHeight;
                var headerLines = 1;
                var searchReplaceLines = this.searchPaneVisible ? 1 : 0;
                var editorHeight = windowHeight - headerLines - searchReplaceLines - 2; // status bar = 1, help bar = 1

                // Calculate gutter width
                var gutterWidth = this.showLineNumbers ? Math.Max(4, this.lines.Count.ToString().Length + 1) + 1 : 0;
                var contentWidth = windowWidth - gutterWidth;

                Console.CursorVisible = false;
                Console.SetCursorPosition(0, 0);

                // Header bar - centered text with file name
                var headerText = this.displayName;
                if (this.modified)
                {
                    headerText += " [Modified]";
                }

                // Ensure the header always renders as exactly one line
                if (headerText.Length >= windowWidth)
                {
                    // Truncate header if it's too long
                    headerText = $"{headerText.Substring(0, Math.Max(0, windowWidth - 6))}...";
                }

                var paddingLeft = Math.Max(0, (windowWidth - headerText.Length) / 2);
                var paddingRight = Math.Max(0, windowWidth - paddingLeft - headerText.Length) - 1;
                var header = $"{new string(' ', paddingLeft)}{headerText}{new string(' ', paddingRight)}";

                this.WriteText(header, this.headerStyle);

                // Add the connection status icon:
                this.WriteText(this.connectionStatus ?? " ", new Style(this.connectionStatus == "✓" ? Color.Lime : Color.Red, this.currentTheme.Header.Background));

                // Render search/replace pane if visible
                if (this.searchPaneVisible)
                {
                    this.RenderSearchReplaceLine(windowWidth);
                }

                // Adjust scroll offset
                if (this.cursorRow < this.scrollOffsetY)
                {
                    this.scrollOffsetY = this.cursorRow;
                }
                else if (this.cursorRow >= this.scrollOffsetY + editorHeight)
                {
                    this.scrollOffsetY = (this.cursorRow - editorHeight) + 1;
                }

                // Horizontal scroll for non-wrap mode
                if (!this.wordWrapEnabled)
                {
                    if (this.cursorCol < this.scrollOffsetX)
                    {
                        this.scrollOffsetX = this.cursorCol;
                    }
                    else if (this.cursorCol >= this.scrollOffsetX + contentWidth)
                    {
                        this.scrollOffsetX = (this.cursorCol - contentWidth) + 1;
                    }
                }

                // Editor content - handle word wrap properly
                var wrapGlyphText = this.settings.WordWrapGlyph;
                if (string.IsNullOrEmpty(wrapGlyphText))
                {
                    wrapGlyphText = "/";
                }

                var continueGlyphText = this.settings.ContinuationGlyph;
                if (string.IsNullOrWhiteSpace(continueGlyphText))
                {
                    continueGlyphText = ">";
                }

                var wrapIndent = "  "; // 2 character indent for wrapped lines
                var currentSourceLine = this.scrollOffsetY;
                var currentWrapOffset = 0;

                // When word wrap is enabled, we need to track which source line and offset we're at
                if (this.wordWrapEnabled)
                {
                    // Skip wrapped lines that scrolled off the top
                    // This is simplified - for now we start at scrollOffsetY source line
                    currentSourceLine = this.scrollOffsetY;
                }

                var searchReplaceOffset = this.searchPaneVisible ? 1 : 0;

                for (var screenRow = 0; screenRow < editorHeight; screenRow++)
                {
                    Console.SetCursorPosition(0, screenRow + 1 + searchReplaceOffset);

                    if (this.wordWrapEnabled)
                    {
                        // Word wrap mode
                        if (currentSourceLine < this.lines.Count)
                        {
                            var fullLineText = this.lines[currentSourceLine].ToString();

                            // Calculate effective width accounting for indent and wrap glyph space
                            var indentSpace = currentWrapOffset > 0 ? wrapIndent.Length : 0;
                            var effectiveWidth = contentWidth - indentSpace - 1; // -1 for wrap glyph space

                            // Draw gutter
                            if (this.showLineNumbers)
                            {
                                if (currentWrapOffset == 0)
                                {
                                    // First segment of line - show line number
                                    var lineNum = (currentSourceLine + 1).ToString().PadLeft(gutterWidth - 1);
                                    this.WriteText($"{lineNum} ", this.currentTheme.Gutter);
                                }
                                else
                                {
                                    // Continuation - blank gutter
                                    this.WriteText(new string(' ', gutterWidth), this.currentTheme.Gutter);
                                }
                            }

                            // Get the segment to display
                            var remainingText = currentWrapOffset < fullLineText.Length
                                ? fullLineText.Substring(currentWrapOffset)
                                : string.Empty;

                            string lineText;
                            var needsWrapGlyph = false;
                            var lineIndexForSelection = currentSourceLine; // Capture before potential increment

                            if (currentWrapOffset > 0)
                            {
                                // Continuation line - add indent
                                this.WriteText(wrapIndent, this.currentTheme.TextEditor);
                            }

                            if (remainingText.Length > effectiveWidth)
                            {
                                // Line needs to wrap
                                lineText = remainingText.Substring(0, effectiveWidth);
                                needsWrapGlyph = true;
                                currentWrapOffset += effectiveWidth;
                            }
                            else
                            {
                                // Line fits or is the last segment - use full content width minus indent
                                var displayWidth = contentWidth - indentSpace;
                                lineText = remainingText.PadRight(displayWidth);
                                currentSourceLine++;
                                currentWrapOffset = 0;
                            }

                            // Render the line segment
                            this.RenderLineWithSelection(lineIndexForSelection, lineText, gutterWidth, contentWidth - indentSpace);

                            if (needsWrapGlyph)
                            {
                                this.WriteText(wrapGlyphText, this.currentTheme.Glyph);
                            }
                        }
                        else
                        {
                            // Past end of file
                            if (this.showLineNumbers)
                            {
                                this.WriteText(new string(' ', gutterWidth), this.CurrentTheme.Gutter);
                            }

                            this.WriteText("~".PadRight(contentWidth), this.CurrentTheme.TextEditor);
                        }
                    }
                    else
                    {
                        // Horizontal scroll mode (no word wrap)
                        var lineIndex = this.scrollOffsetY + screenRow;

                        // Draw gutter with line numbers
                        if (this.showLineNumbers)
                        {
                            if (lineIndex < this.lines.Count)
                            {
                                var lineNum = (lineIndex + 1).ToString().PadLeft(gutterWidth - 1);
                                this.WriteText($"{lineNum} ", this.currentTheme.Gutter);
                            }
                            else
                            {
                                this.WriteText(new string(' ', gutterWidth), this.currentTheme.Gutter);
                            }
                        }

                        if (lineIndex < this.lines.Count)
                        {
                            var lineText = this.lines[lineIndex].ToString();

                            // Horizontal scroll
                            if (this.scrollOffsetX < lineText.Length)
                            {
                                lineText = lineText.Substring(this.scrollOffsetX);
                            }
                            else
                            {
                                lineText = string.Empty;
                            }

                            var needsContinuationGlyph = false;

                            if (lineText.Length > contentWidth)
                            {
                                var length = contentWidth - 1;
                                lineText = lineText[..length];
                                needsContinuationGlyph = true;
                            }
                            else
                            {
                                lineText = lineText.PadRight(contentWidth);
                            }

                            // Render with selection highlighting
                            this.RenderLineWithSelection(lineIndex, lineText, gutterWidth, contentWidth);

                            if (needsContinuationGlyph)
                            {
                                this.WriteText(continueGlyphText, this.currentTheme.Glyph);
                            }
                        }
                        else
                        {
                            this.WriteText("~".PadRight(contentWidth), this.currentTheme.TextEditor);
                        }
                    }
                }

                // Status bar
                Console.SetCursorPosition(0, windowHeight - 2);
                var statusText = this.GetStatusMessage();

                var position = $"Ln {this.cursorRow + 1}, Col {this.cursorCol + 1}";
                if (this.detectedTabDepth > 0)
                {
                    position += $" Tab:{this.tabDepth}";
                }

                // Upload Status
                var uploadStatus = string.Empty;
                if (this.uploadProgress >= 0)
                {
                    uploadStatus = $" Upload: {this.uploadProgress,3}% ";
                }

                // Add status message or connection status
                if (!string.IsNullOrEmpty(statusText))
                {
                    this.WriteText(statusText, this.currentTheme.StatusBar);
                }

                // Calculate padding
                var statusPadding = windowWidth - statusText.Length - position.Length - uploadStatus.Length;
                if (statusPadding < 0)
                {
                    statusPadding = 0;
                }

                this.WriteText(new string(' ', statusPadding), this.CurrentTheme.StatusBar);
                this.WriteText(position, this.CurrentTheme.StatusBar);
                this.WriteText(uploadStatus, this.CurrentTheme.StatusBar);

                // Help bar
                Console.SetCursorPosition(0, windowHeight - 1);
                var help = this.keyBindings.GetShortcutHints();
                while (help.Length > windowWidth)
                {
                    var length = help.LastIndexOf(' ', help.LastIndexOf(' ') - 1) - 1;
                    help = $"{help[..length]}...";
                }

                this.WriteText(help.PadRight(windowWidth), this.currentTheme.HintBar);

                // Position cursor - account for word wrap
                int displayRow;
                int displayCol;

                if (this.currentFocus == SearchReplaceFocus.Search)
                {
                    // Position cursor in search box
                    var searchLabel = "Search:";
                    var searchLabelWidth = searchLabel.Length + 1;
                    int searchWidth;
                    if (this.replacePaneVisible)
                    {
                        var replaceLabel = "Replace:";
                        var totalLabelWidth = searchLabelWidth + replaceLabel.Length + 1 + 2;
                        var availableWidth = windowWidth - totalLabelWidth;
                        searchWidth = availableWidth / 2;
                    }
                    else
                    {
                        searchWidth = windowWidth - searchLabelWidth - 1;
                    }

                    var searchVisibleStart = Math.Max(0, (this.searchCursorPosition - searchWidth) + 1);
                    displayRow = 1;
                    displayCol = searchLabelWidth + (this.searchCursorPosition - searchVisibleStart);
                    displayCol = Math.Max(searchLabelWidth, Math.Min(displayCol, (searchLabelWidth + searchWidth) - 1));
                    Console.SetCursorPosition(displayCol, displayRow);
                    Console.CursorVisible = true;
                }
                else if (this.currentFocus == SearchReplaceFocus.Replace)
                {
                    // Position cursor in replace box
                    var searchLabel = "Search:";
                    var replaceLabel = "Replace:";
                    var searchLabelWidth = searchLabel.Length + 1;
                    var replaceLabelWidth = replaceLabel.Length + 1;
                    var totalLabelWidth = searchLabelWidth + replaceLabelWidth + 2;
                    var availableWidth = windowWidth - totalLabelWidth;
                    var searchWidth = availableWidth / 2;
                    var replaceWidth = availableWidth - searchWidth;

                    var replaceVisibleStart = Math.Max(0, (this.replaceCursorPosition - replaceWidth) + 1);
                    displayRow = 1;
                    displayCol = searchLabelWidth + searchWidth + 2 + replaceLabelWidth + (this.replaceCursorPosition - replaceVisibleStart);
                    displayCol = Math.Max(searchLabelWidth + searchWidth + 2 + replaceLabelWidth, Math.Min(displayCol, windowWidth - 1));
                    Console.SetCursorPosition(displayCol, displayRow);
                    Console.CursorVisible = true;
                }
                else if (this.currentFocus == SearchReplaceFocus.Editor)
                {
                    if (this.wordWrapEnabled)
                    {
                        // Calculate the screen row by counting how many wrapped rows are above the cursor
                        var screenRowCount = 0;
                        var wrapIndentLen = wrapIndent.Length;
                        displayCol = gutterWidth; // Default value

                        for (int lineIdx = this.scrollOffsetY; lineIdx <= this.cursorRow && screenRowCount <
                            editorHeight; lineIdx++)
                        {
                            if (lineIdx >= this.lines.Count)
                            {
                                break;
                            }

                            var lineText = this.lines[lineIdx].ToString();

                            if (lineIdx < this.cursorRow)
                            {
                                // Count all wrapped rows for lines before cursor
                                // Empty lines still take one row
                                if (lineText.Length == 0)
                                {
                                    screenRowCount++;
                                }
                                else
                                {
                                    var pos = 0;
                                    var firstSegment = true;
                                    while (pos < lineText.Length)
                                    {
                                        var segmentWidth = contentWidth - (firstSegment ? 0 : wrapIndentLen) - 1;
                                        if (segmentWidth <= 0)
                                        {
                                            segmentWidth = 1; // Safety: at least 1 character per segment
                                        }

                                        screenRowCount++;
                                        pos += segmentWidth;
                                        firstSegment = false;
                                    }
                                }
                            }
                            else
                            {
                                // This is the cursor's line - find which segment contains the cursor
                                var pos = 0;
                                var firstSegment = true;
                                while (pos <= this.cursorCol)
                                {
                                    var segmentWidth = contentWidth - (firstSegment ? 0 : wrapIndentLen) - 1;
                                    if (segmentWidth <= 0)
                                    {
                                        segmentWidth = 1; // Safety: at least 1 character per segment
                                    }

                                    if (this.cursorCol < pos + segmentWidth || pos + segmentWidth >= lineText.Length)
                                    {
                                        // Cursor is in this segment
                                        var colInSegment = this.cursorCol - pos;
                                        displayCol = gutterWidth + (firstSegment ? 0 : wrapIndentLen) + colInSegment;
                                        break;
                                    }

                                    screenRowCount++;
                                    pos += segmentWidth;
                                    firstSegment = false;
                                }
                            }
                        }

                        displayRow = screenRowCount + 1 + searchReplaceOffset; // +1 for header, + searchReplaceOffset for search/replace pane
                    }
                    else
                    {
                        displayRow = (this.cursorRow - this.scrollOffsetY) + 1 + searchReplaceOffset;
                        displayCol = (gutterWidth + this.cursorCol) - this.scrollOffsetX;
                    }

                    displayCol = Math.Max(gutterWidth, Math.Min(displayCol, windowWidth - 1));
                    displayRow = Math.Max(1 + searchReplaceOffset, Math.Min(displayRow, editorHeight + searchReplaceOffset));
                    Console.SetCursorPosition(displayCol, displayRow);
                }

                Console.CursorVisible = true;
            }
        }

        /// <summary>
        /// Renders the help screen, displaying available key bindings and usage instructions in the console window.
        /// </summary>
        /// <remarks>The help screen is centered within the current console window and supports scrolling
        /// if the content exceeds the visible area. The footer provides navigation hints and indicates the current
        /// scroll position when applicable. This method modifies the console's cursor visibility and position while
        /// rendering.</remarks>
        private void RenderHelp()
        {
            var helpLines = this.keyBindings.GetHelpText();
            var windowHeight = Console.WindowHeight;
            var windowWidth = Console.WindowWidth;
            var contentHeight = windowHeight - 2; // Header + Footer

            Console.CursorVisible = false;
            Console.SetCursorPosition(0, 0);

            // Header
            var header = "Editor Help";
            if (header.Length >= windowWidth)
            {
                header = header.Substring(0, windowWidth);
            }

            var paddingLeft = Math.Max(0, (windowWidth - header.Length) / 2);
            var paddingRight = Math.Max(0, windowWidth - paddingLeft - header.Length);
            var headerLine = $"{new string(' ', paddingLeft)}{header}{new string(' ', paddingRight)}";
            if (headerLine.Length > windowWidth)
            {
                headerLine = headerLine.Substring(0, windowWidth);
            }

            this.WriteText(headerLine, this.currentTheme.Header, true);

            // Content with scrolling
            for (var i = 0; i < contentHeight; i++)
            {
                Console.SetCursorPosition(0, i + 1);
                var lineIndex = this.helpScrollOffset + i;
                if (lineIndex < helpLines.Length)
                {
                    var line = helpLines[lineIndex];
                    if (line.Length > windowWidth)
                    {
                        line = line.Substring(0, windowWidth);
                    }

                    this.WriteText(line.PadRight(windowWidth), this.currentTheme.TextEditor);
                }
                else
                {
                    this.WriteText(new string(' ', windowWidth), this.currentTheme.TextEditor);
                }
            }

            // Footer - always at the last line
            Console.SetCursorPosition(0, windowHeight - 1);
            var footerText = helpLines.Length > contentHeight
                ? $" ^Q/ESC Return | Scroll ({this.helpScrollOffset + 1}-{Math.Min(this.helpScrollOffset + contentHeight, helpLines.Length)}/{helpLines.Length})"
                : " Press Ctrl+Q or ESC to return to editor";
            if (footerText.Length > windowWidth)
            {
                footerText = footerText.Substring(0, windowWidth);
            }

            this.WriteText(footerText.PadRight(windowWidth), this.currentTheme.HintBar);
            Console.CursorVisible = false;
        }

        /// <summary>
        /// Renders a single line of text with visual highlighting for search matches and text selection within the
        /// line.
        /// </summary>
        /// <remarks>This method applies visual highlights to regions of the line that correspond to
        /// active search matches and the current text selection. If both a search match and a selection overlap, the
        /// selection highlight takes precedence. The method does not modify the underlying text data.</remarks>
        /// <param name="lineIndex">The zero-based index of the line to render.</param>
        /// <param name="lineText">The text content of the line to be rendered.</param>
        /// <param name="gutterWidth">The width, in pixels or character units, of the gutter area to the left of the content. Used to align the
        /// rendered line.</param>
        /// <param name="contentWidth">The width, in pixels or character units, available for rendering the line's content.</param>
        private void RenderLineWithSelection(int lineIndex, string lineText, int gutterWidth, int contentWidth)
        {
            // Check for search matches in this line
            var matchesInLine = new List<(int Start, int End)>();
            if (this.searchPaneVisible && !string.IsNullOrEmpty(this.searchText))
            {
                foreach (var match in this.searchMatches)
                {
                    if (match.Row == lineIndex)
                    {
                        var startCol = match.Col - (this.wordWrapEnabled ? 0 : this.scrollOffsetX);
                        var endCol = startCol + this.searchText.Length;
                        if (startCol < lineText.Length && endCol > 0)
                        {
                            matchesInLine.Add((Math.Max(0, startCol), Math.Min(lineText.Length, endCol)));
                        }
                    }
                }
            }

            // Check for selection in this line
            bool hasSelectionInLine = this.hasSelection;
            var selLineStart = 0;
            var selLineEnd = 0;

            if (this.hasSelection)
            {
                var (startRow, startCol, endRow, endCol) = this.GetNormalizedSelection();

                if (lineIndex >= startRow && lineIndex <= endRow)
                {
                    selLineStart = lineIndex == startRow ? Math.Max(0, startCol - (this.wordWrapEnabled ? 0 : this.scrollOffsetX)) : 0;
                    selLineEnd = lineIndex == endRow ? Math.Min(lineText.Length, endCol - (this.wordWrapEnabled ? 0 : this.scrollOffsetX)) : lineText.Length;
                }
                else
                {
                    hasSelectionInLine = false;
                }
            }

            // Render the line with highlighting
            if (!hasSelectionInLine && matchesInLine.Count == 0)
            {
                this.WriteText(lineText, this.currentTheme.TextEditor);
                return;
            }

            // Build a list of all highlight regions with proper overlap handling
            var allHighlights = new List<(int Start, int End, bool IsSelection)>();

            if (hasSelectionInLine)
            {
                allHighlights.Add((selLineStart, selLineEnd, true));
            }

            foreach (var match in matchesInLine)
            {
                allHighlights.Add((match.Start, match.End, false));
            }

            // Sort by start position
            allHighlights.Sort((a, b) => a.Start.CompareTo(b.Start));

            // Merge overlapping regions - selection takes precedence
            var mergedHighlights = new List<(int Start, int End, bool IsSelection)>();
            for (var i = 0; i < allHighlights.Count; i++)
            {
                var current = allHighlights[i];

                // Check for overlaps with previous regions
                var merged = false;
                for (var j = 0; j < mergedHighlights.Count; j++)
                {
                    var prev = mergedHighlights[j];

                    // If current overlaps with previous
                    if (current.Start < prev.End && current.End > prev.Start)
                    {
                        // Selection always wins in overlaps
                        var isSelection = prev.IsSelection || current.IsSelection;
                        var newStart = Math.Min(prev.Start, current.Start);
                        var newEnd = Math.Max(prev.End, current.End);
                        mergedHighlights[j] = (newStart, newEnd, isSelection);
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    mergedHighlights.Add(current);
                }
            }

            // Re-sort after merging
            mergedHighlights.Sort((a, b) => a.Start.CompareTo(b.Start));

            // Render with highlights
            var pos = 0;
            foreach (var (start, end, isSelection) in mergedHighlights)
            {
                // Render text before highlight
                if (pos < start)
                {
                    this.WriteText(lineText.Substring(pos, start - pos), this.currentTheme.TextEditor);
                }

                // Render highlighted region
                if (start < end && start < lineText.Length)
                {
                    var highlightEnd = Math.Min(end, lineText.Length);
                    var style = isSelection ? this.currentTheme.TextEditor : this.currentTheme.Glyph;
                    this.WriteText(lineText.Substring(start, highlightEnd - start), style, isSelection);
                    pos = highlightEnd;
                }
            }

            // Render remaining text
            if (pos < lineText.Length)
            {
                this.WriteText(lineText.Substring(pos), this.currentTheme.TextEditor);
            }
        }

        /// <summary>
        /// Renders the search and replace input line in the console status bar, adjusting layout based on the specified
        /// window width and the visibility of the replace pane.
        /// </summary>
        /// <remarks>This method positions the cursor and draws the search and, if visible, replace fields
        /// in the status bar area. The layout dynamically splits available space between the fields based on the
        /// current window width and whether the replace pane is shown. If the replace pane is hidden, the search field
        /// uses the majority of the available width.</remarks>
        /// <param name="windowWidth">The total width of the console window, in characters, used to determine the layout of the search and replace
        /// fields.</param>
        private void RenderSearchReplaceLine(int windowWidth)
        {
            Console.SetCursorPosition(0, 1);

            var searchLabel = "Search:";
            var replaceLabel = "Replace:";
            var searchLabelWidth = searchLabel.Length + 1;
            var replaceLabelWidth = replaceLabel.Length + 1;

            // Calculate available widths
            int searchWidth, replaceWidth;
            if (this.replacePaneVisible)
            {
                // Both search and replace visible - split the width
                var totalLabelWidth = searchLabelWidth + replaceLabelWidth + 2; // +2 for spacing
                var availableWidth = windowWidth - totalLabelWidth;
                searchWidth = availableWidth / 2;
                replaceWidth = availableWidth - searchWidth;
            }
            else
            {
                // Only search visible - use most of the width
                searchWidth = windowWidth - searchLabelWidth - 1;
                replaceWidth = 0;
            }

            // Render search label
            this.WriteText($"{searchLabel} ", this.currentTheme.StatusBar);

            // Render search text
            var searchVisibleStart = Math.Max(0, (this.searchCursorPosition - searchWidth) + 1);
            var searchVisibleText = this.searchText.Length > searchVisibleStart
                ? this.searchText.Substring(searchVisibleStart, Math.Min(this.searchText.Length - searchVisibleStart, searchWidth))
                : string.Empty;

            var searchPadding = Math.Max(0, searchWidth - searchVisibleText.Length);
            this.WriteText($"{searchVisibleText}{new string(' ', searchPadding)}", this.currentTheme.StatusBar);

            if (this.replacePaneVisible)
            {
                // Render separator
                this.WriteText("  ", this.currentTheme.StatusBar);

                // Render replace label
                this.WriteText($"{replaceLabel} ", this.currentTheme.StatusBar);

                // Render replace text
                var replaceVisibleStart = Math.Max(0, (this.replaceCursorPosition - replaceWidth) + 1);
                var replaceVisibleText = this.replaceText.Length > replaceVisibleStart
                    ? this.replaceText.Substring(replaceVisibleStart, Math.Min(this.replaceText.Length - replaceVisibleStart, replaceWidth))
                    : string.Empty;

                var replacePadding = Math.Max(0, replaceWidth - replaceVisibleText.Length);
                this.WriteText($"{replaceVisibleText}{new string(' ', replacePadding)}", this.currentTheme.StatusBar);
            }
            else
            {
                // Fill rest of line
                var remainingWidth = windowWidth - searchLabelWidth - searchWidth;
                if (remainingWidth > 0)
                {
                    this.WriteText(new string(' ', remainingWidth), this.currentTheme.StatusBar);
                }
            }
        }

        /// <summary>
        /// Replaces all occurrences of the current search text with the specified replacement text in the document.
        /// </summary>
        /// <remarks>This method performs a bulk replacement of all found matches for the current search
        /// text. If no search text is specified or no matches are found, no changes are made. After replacement, the
        /// undo state is saved, the selection is cleared, and the list of search matches is reset. The method also
        /// updates the status message to indicate the number of replacements performed.</remarks>
        private void ReplaceAll()
        {
            if (string.IsNullOrEmpty(this.searchText))
            {
                this.SetStatusMessage("No search text");
                return;
            }

            if (this.searchMatches.Count == 0)
            {
                this.SetStatusMessage("No matches found");
                return;
            }

            this.SaveUndoState();

            var replaceCount = 0;

            // Create a copy of matches to avoid issues with modifications
            var matchesToReplace = new List<(int Row, int Col)>(this.searchMatches);

            // Group matches by row for proper column offset handling
            var matchesByRow = matchesToReplace.GroupBy(m => m.Row).OrderByDescending(g => g.Key);

            foreach (var rowGroup in matchesByRow)
            {
                var row = rowGroup.Key;
                var line = this.lines[row];

                // Sort matches in this row by column in descending order
                var sortedMatches = rowGroup.OrderByDescending(m => m.Col).ToList();

                foreach (var match in sortedMatches)
                {
                    // Replace the text
                    line.Remove(match.Col, this.searchText.Length);
                    if (!string.IsNullOrEmpty(this.replaceText))
                    {
                        line.Insert(match.Col, this.replaceText);
                    }

                    replaceCount++;
                }
            }

            this.modified = true;
            this.ClearSelection();

            // Clear matches instead of updating them - we just replaced everything
            this.searchMatches.Clear();
            this.currentSearchMatchIndex = -1;

            this.SetStatusMessage($"Replaced {replaceCount} occurrence(s)");
        }

        /// <summary>
        /// Replaces the currently selected occurrence of the search text with the specified replacement text, if a
        /// match is found.
        /// </summary>
        /// <remarks>If no search text is specified or no match is currently selected, the method attempts
        /// to find the next occurrence before performing the replacement. After replacing, the method updates the
        /// search matches and moves the selection to the next occurrence, if available. This method is intended to be
        /// used as part of a find-and-replace workflow in a text editing context.</remarks>
        private void ReplaceCurrent()
        {
            if (string.IsNullOrEmpty(this.searchText))
            {
                this.SetStatusMessage("No search text");
                return;
            }

            if (!this.hasSelection)
            {
                this.FindNext();
                if (!this.hasSelection)
                {
                    return;
                }
            }

            // Verify the selection matches the search text
            var selectedText = this.GetSelectedText();
            if (!string.Equals(selectedText, this.searchText, StringComparison.OrdinalIgnoreCase))
            {
                this.FindNext();
                return;
            }

            // Replace the selected text
            this.SaveUndoState();
            this.DeleteSelection();
            if (!string.IsNullOrEmpty(this.replaceText))
            {
                this.InsertText(this.replaceText);
            }

            // Update matches and find next
            this.UpdateSearchMatches();
            if (this.searchMatches.Count > 0)
            {
                // Adjust current index since we modified the document
                if (this.currentSearchMatchIndex >= this.searchMatches.Count)
                {
                    this.currentSearchMatchIndex = 0;
                }
                this.FindNext();
            }

            this.SetStatusMessage("Replaced 1 occurrence");
        }

        /// <summary>
        /// Saves the current content to the file specified by the file path, overwriting any existing file.
        /// </summary>
        /// <remarks>This method writes all lines to the file using UTF-8 encoding. After saving, the
        /// modified state is reset and a status message is updated. This method does not prompt for confirmation before
        /// overwriting the file.</remarks>
        private void SaveFile()
        {
            using var writer = new StreamWriter(this.filePath, false, Encoding.UTF8);
            for (var i = 0; i < this.lines.Count; i++)
            {
                writer.Write(this.lines[i].ToString());
                if (i < this.lines.Count - 1)
                {
                    writer.WriteLine();
                }
            }

            this.modified = false;
            this.SetStatusMessage("File saved -> File Uploading");
        }

        /// <summary>
        /// Saves the current editor state to the undo history stack, enabling the user to undo recent changes.
        /// </summary>
        /// <remarks>This method should be called after any operation that modifies the editor's content
        /// or state. Saving a new undo state clears the redo history and enforces the maximum undo history size limit.
        /// This method does not throw exceptions under normal usage.</remarks>
        private void SaveUndoState()
        {
            // Create a copy of the current state
            var linesCopy = new string[this.lines.Count];
            for (var i = 0; i < this.lines.Count; i++)
            {
                linesCopy[i] = this.lines[i].ToString();
            }

            var state = new UndoState(linesCopy, this.cursorRow, this.cursorCol, this.modified);
            this.undoStack.Push(state);

            // Clear redo stack when a new change is made
            this.redoStack.Clear();

            // Limit undo history size by removing oldest entries
            while (this.undoStack.Count > MaxUndoHistory)
            {
                // Remove oldest entry by converting to array and creating new stack
                var items = this.undoStack.ToArray();
                this.undoStack.Clear();

                // Push items back except the last one (oldest), in reverse order
                for (int i = items.Length - 2; i >= 0; i--)
                {
                    this.undoStack.Push(items[i]);
                }
            }
        }

        /// <summary>
        /// Scrolls the editor view to ensure that the current selection is visible within the viewport.
        /// </summary>
        /// <remarks>If no selection is present, this method does nothing. The method adjusts both
        /// vertical and horizontal scroll offsets as needed to bring the selection into view. Horizontal scrolling is
        /// only applied when word wrap is disabled.</remarks>
        private void ScrollSelectionIntoView()
        {
            if (!this.hasSelection)
            {
                return;
            }

            var (startRow, startCol, _, endCol) = this.GetNormalizedSelection();

            // Get editor height accounting for search/replace pane
            var windowHeight = Console.WindowHeight;
            var headerLines = 1;
            var searchReplaceLines = this.searchPaneVisible ? 1 : 0;
            var editorHeight = windowHeight - headerLines - searchReplaceLines - 2;

            // Vertical scrolling
            if (startRow < this.scrollOffsetY)
            {
                this.scrollOffsetY = startRow;
            }
            else if (startRow >= this.scrollOffsetY + editorHeight)
            {
                this.scrollOffsetY = (startRow - editorHeight) + 1;
            }

            // Horizontal scrolling (only in non-wrap mode)
            if (!this.wordWrapEnabled)
            {
                var windowWidth = Console.WindowWidth;
                var gutterWidth = this.showLineNumbers ? Math.Max(4, this.lines.Count.ToString().Length + 1) + 1 : 0;
                var contentWidth = windowWidth - gutterWidth;

                if (startCol < this.scrollOffsetX)
                {
                    this.scrollOffsetX = startCol;
                }
                else if (endCol >= this.scrollOffsetX + contentWidth)
                {
                    this.scrollOffsetX = (endCol - contentWidth) + 1;
                }
            }
        }

        /// <summary>
        /// Selects all content within the current document or text buffer.
        /// </summary>
        /// <remarks>After calling this method, the entire content is marked as selected, and the cursor
        /// is moved to the end of the selection. Any previous selection is replaced.</remarks>
        private void SelectAll()
        {
            this.selectionStartRow = 0;
            this.selectionStartCol = 0;
            this.selectionEndRow = this.lines.Count - 1;
            this.selectionEndCol = this.lines[this.selectionEndRow].Length;
            this.hasSelection = true;
            this.cursorRow = this.selectionEndRow;
            this.cursorCol = this.selectionEndCol;
        }

        /// <summary>
        /// Selects and applies the file text editor theme based on the current settings.
        /// </summary>
        private void SelectTheme()
        {
            var themeName = this.settings.ThemeName;
            FileTextEditorTheme theme;
            switch (themeName)
            {
                case "User":
                    theme = FileTextEditorTheme.User;
                    break;
                case "NordDark":
                    theme = FileTextEditorTheme.NordDark;
                    break;
                case "NordLight":
                    theme = FileTextEditorTheme.NordLight;
                    break;
                case "NordSemiDark":
                    theme = FileTextEditorTheme.NordSemiDark;
                    break;
                case "NordSemiLight":
                    theme = FileTextEditorTheme.NordSemiLight;
                    break;
                default:
                    theme = FileTextEditorTheme.User;
                    break;
            }

            theme.Header = this.headerStyle;
            this.currentTheme = theme;
            this.SetStatusMessage($"Theme: {themeName}");
        }

        /// <summary>
        /// Sets the current status message to display to the user.
        /// </summary>
        /// <param name="message">The status message text to display. Can be null or empty to clear the current message.</param>
        private void SetStatusMessage(string message)
        {
            this.statusMessage = message;
            this.statusMessageTime = DateTime.Now;
            this.needsRender = true;
        }

        /// <summary>
        /// Displays the help screen to the user.
        /// </summary>
        private void ShowHelp()
        {
            this.helpScreenVisible = true;
            this.helpScrollOffset = 0;
            this.needsRender = true;
        }

        /// <summary>
        /// Begins a new selection at the current cursor position.
        /// </summary>
        private void StartSelection()
        {
            this.selectionStartRow = this.cursorRow;
            this.selectionStartCol = this.cursorCol;
            this.selectionEndRow = this.cursorRow;
            this.selectionEndCol = this.cursorCol;
            this.hasSelection = true;
        }

        /// <summary>
        /// Attempts to retrieve the current selection as a normalized rectangular region.
        /// </summary>
        /// <param name="selection">When this method returns, contains a tuple representing the normalized selection as (startRow, startCol,
        /// endRow, endCol) if a valid selection exists; otherwise, all values are set to -1.</param>
        /// <returns>true if a valid, non-empty selection exists and was returned in selection; otherwise, false.</returns>
        private bool TryGetNormalizedSelection(out (int startRow, int startCol, int endRow, int endCol) selection)
        {
            if (!this.hasSelection)
            {
                selection = (-1, -1, -1, -1);
                return false;
            }

            selection = this.GetNormalizedSelection();
            if (selection.startRow == selection.endRow && selection.startCol == selection.endCol)
            {
                selection = (-1, -1, -1, -1);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reverses the most recent change to the document, restoring the previous state if available.
        /// </summary>
        /// <remarks>If there are no actions to undo, the method does nothing. The undone action can be
        /// redone using the corresponding redo functionality. Calling this method updates the document's content,
        /// cursor position, and modification state to match the previous state.</remarks>
        private void Undo()
        {
            if (this.undoStack.Count == 0)
            {
                this.SetStatusMessage("Nothing to undo.");
                return;
            }

            // Save current state to redo stack before undoing
            var currentLinesCopy = new string[this.lines.Count];
            for (var i = 0; i < this.lines.Count; i++)
            {
                currentLinesCopy[i] = this.lines[i].ToString();
            }
            var currentState = new UndoState(currentLinesCopy, this.cursorRow, this.cursorCol, this.modified);
            this.redoStack.Push(currentState);

            var state = this.undoStack.Pop();

            // Restore lines efficiently
            this.lines.Clear();
            this.lines.Capacity = state.Lines.Length;
            foreach (var line in state.Lines)
            {
                this.lines.Add(new StringBuilder(line));
            }

            this.cursorRow = Math.Min(state.CursorRow, this.lines.Count - 1);
            this.cursorCol = Math.Min(state.CursorCol, this.lines[this.cursorRow].Length);
            this.modified = state.Modified;
            this.ClearSelection();
            this.SetStatusMessage("Undo.");
        }

        /// <summary>
        /// Updates the collection of search matches based on the current search text and cursor position.
        /// </summary>
        /// <remarks>Clears any existing search matches and recalculates them using a case-insensitive
        /// search. If matches are found, sets the current match index to the first match at or after the cursor
        /// position, or wraps to the first match if none are found after the cursor. Updates the status message to
        /// reflect the number of matches found.</remarks>
        private void UpdateSearchMatches()
        {
            this.searchMatches.Clear();
            this.currentSearchMatchIndex = -1;

            if (string.IsNullOrEmpty(this.searchText))
            {
                return;
            }

            // Find all matches
            for (var row = 0; row < this.lines.Count; row++)
            {
                var line = this.lines[row].ToString();
                var startIndex = 0;

                while (startIndex < line.Length)
                {
                    var index = line.IndexOf(this.searchText, startIndex, StringComparison.OrdinalIgnoreCase);
                    if (index == -1)
                    {
                        break;
                    }

                    this.searchMatches.Add((row, index));
                    startIndex = index + 1;
                }
            }

            // Find current match based on cursor position
            if (this.searchMatches.Count > 0)
            {
                for (var i = 0; i < this.searchMatches.Count; i++)
                {
                    var match = this.searchMatches[i];
                    if (match.Row > this.cursorRow || match.Row == this.cursorRow && match.Col >= this.cursorCol)
                    {
                        this.currentSearchMatchIndex = i;
                        break;
                    }
                }

                // If no match found after cursor, wrap to first match
                if (this.currentSearchMatchIndex == -1)
                {
                    this.currentSearchMatchIndex = 0;
                }

                this.SetStatusMessage($"Found {this.searchMatches.Count} match{(this.searchMatches.Count > 1 ? "es" : string.Empty)}");
            }
            else
            {
                this.SetStatusMessage("No matches found");
            }
        }

        /// <summary>
        /// Updates the current selection state based on whether selection mode is active.
        /// </summary>
        /// <remarks>Call this method to update the selection endpoints when the selection state changes,
        /// such as during mouse or keyboard interactions. If selection mode is not active, any existing selection will
        /// be cleared.</remarks>
        /// <param name="isSelecting"><see langword="true"/> to continue or update the current selection; <see langword="false"/> to clear the selection.</param>
        private void UpdateSelection(bool isSelecting)
        {
            if (isSelecting && this.hasSelection)
            {
                this.selectionEndRow = this.cursorRow;
                this.selectionEndCol = this.cursorCol;
            }
            else if (!isSelecting)
            {
                this.ClearSelection();
            }
        }

        /// <summary>
        /// Writes the specified text to the console using the given style, with an option to invert foreground and
        /// background colors.
        /// </summary>
        /// <param name="content">The text content to write to the console. Can be null or empty, in which case no text is displayed.</param>
        /// <param name="style">The style to apply to the text, including foreground and background colors, decoration, and link formatting.</param>
        /// <param name="invert"><see langword="true"/> to swap the foreground and background colors of the specified style; otherwise, <see langword="false"/>.</param>
        private void WriteText(string content, Style style, bool invert = false)
        {
            if (invert)
            {
                style = new Style(style.Background, style.Foreground, style.Decoration, style.Link);
            }

            AnsiConsole.Write(new Text(content, style));
        }

        /// <summary>
        /// Represents a saved editor state for undo operations.
        /// </summary>
        internal sealed record UndoState(string[] Lines, int CursorRow, int CursorCol, bool Modified);
    }
}
