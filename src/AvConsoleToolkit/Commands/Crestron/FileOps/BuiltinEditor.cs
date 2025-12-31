// <copyright file="BuiltinEditor.cs">
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

// Some System.Console operations are required for cursor positioning, key reading, and visibility
// that AnsiConsole doesn't provide equivalents for.
#pragma warning disable Spectre1000 // Use AnsiConsole instead of System.Console

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.Configuration;
using Spectre.Console;

namespace AvConsoleToolkit.Commands.Crestron.FileOps
{
    /// <summary>
    /// A built-in nano-like text editor that operates in an alternate screen buffer.
    /// Provides text editing capabilities with keyboard navigation, selection, and configurable display options.
    /// </summary>
    public sealed class BuiltinEditor
    {
        private readonly string filePath;
        private readonly string displayName;
        private readonly Func<Task> onSaveCallback;
        private readonly List<StringBuilder> lines = new();
        private readonly EditorKeyBindings keyBindings;
        private readonly IBuiltInEditorSettings settings;

        private int cursorRow;
        private int cursorCol;
        private int scrollOffsetY;
        private int scrollOffsetX;
        private bool modified;
        private bool running;
        private string statusMessage = string.Empty;
        private DateTime statusMessageTime = DateTime.MinValue;
        private int uploadProgress = -1;

        // Selection state
        private int selectionStartRow = -1;
        private int selectionStartCol = -1;
        private int selectionEndRow = -1;
        private int selectionEndCol = -1;
        private bool hasSelection;

        // Clipboard
        private string? clipboard;

        // Display settings
        private bool showLineNumbers;
        private bool wordWrapEnabled;
        private int tabDepth;
        private int detectedTabDepth = -1;

        // Colors
        private Color headerBgColor;
        private Color headerFgColor;
        private Color gutterBgColor;
        private Color gutterFgColor;

        /// <summary>
        /// Initializes a new instance of the <see cref="BuiltinEditor"/> class.
        /// </summary>
        /// <param name="filePath">Path to the local file to edit.</param>
        /// <param name="displayName">Display name shown in the editor header.</param>
        /// <param name="onSaveCallback">Callback invoked when the file is saved.</param>
        /// <param name="keyBindings">Optional custom key bindings.</param>
        public BuiltinEditor(string filePath, string displayName, Func<Task> onSaveCallback, EditorKeyBindings? keyBindings = null)
        {
            this.filePath = filePath;
            this.displayName = displayName;
            this.onSaveCallback = onSaveCallback;
            this.keyBindings = keyBindings ?? EditorKeyBindings.Default;
            this.settings = AppConfig.Settings.BuiltInEditor;

            // Load settings
            this.showLineNumbers = this.settings.ShowLineNumbers;
            this.wordWrapEnabled = this.settings.WordWrapEnabled;
            this.tabDepth = this.settings.TabDepth;

            // Parse colors
            this.LoadColors();
        }

        /// <summary>
        /// Gets or sets the current upload progress (0-100). Set to -1 to hide the progress bar.
        /// </summary>
        public int UploadProgress
        {
            get => this.uploadProgress;
            set => this.uploadProgress = value;
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
            var needsRender = true;
            var lastWindowWidth = System.Console.WindowWidth;
            var lastWindowHeight = System.Console.WindowHeight;

            // Save original console state and enable Ctrl+C as input
            var originalTreatControlCAsInput = System.Console.TreatControlCAsInput;
            System.Console.TreatControlCAsInput = true;

            try
            {
                AnsiConsole.AlternateScreen(() =>
                {
                    // Initial render
                    this.Render();

                    while (this.running && !cancellationToken.IsCancellationRequested)
                    {
                        // Check for window resize
                        var currentWidth = System.Console.WindowWidth;
                        var currentHeight = System.Console.WindowHeight;
                        if (currentWidth != lastWindowWidth || currentHeight != lastWindowHeight)
                        {
                            lastWindowWidth = currentWidth;
                            lastWindowHeight = currentHeight;
                            needsRender = true;
                        }

                        if (System.Console.KeyAvailable)
                        {
                            var key = System.Console.ReadKey(true);
                            this.HandleKeySync(key, cancellationToken);
                            needsRender = true;
                        }
                        else
                        {
                            Thread.Sleep(50);
                        }

                        if (needsRender)
                        {
                            this.Render();
                            needsRender = false;
                        }
                    }
                });
            }
            finally
            {
                // Restore original console state
                System.Console.TreatControlCAsInput = originalTreatControlCAsInput;
            }

            return !this.modified;
        }

        private void LoadColors()
        {
            var extension = Path.GetExtension(this.filePath)?.TrimStart('.').ToLowerInvariant() ?? string.Empty;

            // Default colors
            this.headerBgColor = ParseHexColor(this.settings.HeaderBackgroundColor, new Color(208, 135, 112));
            this.headerFgColor = ParseHexColor(this.settings.HeaderForegroundColor, new Color(46, 52, 64));
            this.gutterBgColor = ParseHexColor(this.settings.GutterBackgroundColor, new Color(59, 66, 82));
            this.gutterFgColor = ParseHexColor(this.settings.GutterForegroundColor, new Color(229, 233, 240));

            // Check for extension-specific header colors
            if (!string.IsNullOrWhiteSpace(this.settings.HeaderColorMappings))
            {
                var mappings = this.settings.HeaderColorMappings.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var mapping in mappings)
                {
                    var parts = mapping.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && parts[0].Equals(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        var colors = parts[1].Split(',', StringSplitOptions.TrimEntries);
                        if (colors.Length >= 1)
                        {
                            this.headerFgColor = ParseHexColor(colors[0], this.headerFgColor);
                        }

                        if (colors.Length >= 2)
                        {
                            this.headerBgColor = ParseHexColor(colors[1], this.headerBgColor);
                        }

                        break;
                    }
                }
            }
        }

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

        private void LoadFile()
        {
            this.lines.Clear();
            if (System.IO.File.Exists(this.filePath))
            {
                var content = System.IO.File.ReadAllLines(this.filePath);
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

        private void SaveFile()
        {
            using var writer = new StreamWriter(this.filePath, false, Encoding.UTF8);
            for (int i = 0; i < this.lines.Count; i++)
            {
                writer.Write(this.lines[i].ToString());
                if (i < this.lines.Count - 1)
                {
                    writer.WriteLine();
                }
            }

            this.modified = false;
            this.SetStatusMessage("File saved.");
        }

        private void Render()
        {
            var windowWidth = System.Console.WindowWidth;
            var windowHeight = System.Console.WindowHeight;
            var editorHeight = windowHeight - 3; // Header, status bar, help bar

            // Calculate gutter width
            var gutterWidth = this.showLineNumbers ? Math.Max(4, this.lines.Count.ToString().Length + 1) + 1 : 0;
            var contentWidth = windowWidth - gutterWidth;

            System.Console.CursorVisible = false;
            System.Console.SetCursorPosition(0, 0);

            // Header bar - centered text with file name
            var headerText = this.displayName;
            if (this.modified)
            {
                headerText += " [Modified]";
            }

            var paddingLeft = Math.Max(0, (windowWidth - headerText.Length) / 2);
            var paddingRight = Math.Max(0, windowWidth - paddingLeft - headerText.Length);
            var header = new string(' ', paddingLeft) + headerText + new string(' ', paddingRight);

            AnsiConsole.Markup($"[{this.headerFgColor.ToMarkup()} on {this.headerBgColor.ToMarkup()}]{header.EscapeMarkup()}[/]");

            // Adjust scroll offset
            if (this.cursorRow < this.scrollOffsetY)
            {
                this.scrollOffsetY = this.cursorRow;
            }
            else if (this.cursorRow >= this.scrollOffsetY + editorHeight)
            {
                this.scrollOffsetY = this.cursorRow - editorHeight + 1;
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
                    this.scrollOffsetX = this.cursorCol - contentWidth + 1;
                }
            }

            // Editor content - handle word wrap properly
            var wrapGlyph = this.settings.WordWrapGlyph ?? "\uEBEA";
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

            for (int screenRow = 0; screenRow < editorHeight; screenRow++)
            {
                System.Console.SetCursorPosition(0, screenRow + 1);

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
                                AnsiConsole.Markup($"[{this.gutterFgColor.ToMarkup()} on {this.gutterBgColor.ToMarkup()}]{lineNum} [/]");
                            }
                            else
                            {
                                // Continuation - blank gutter
                                AnsiConsole.Markup($"[{this.gutterFgColor.ToMarkup()} on {this.gutterBgColor.ToMarkup()}]{new string(' ', gutterWidth)}[/]");
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
                            System.Console.Write(wrapIndent);
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
                            AnsiConsole.Markup($"[dim]{wrapGlyph.EscapeMarkup()}[/]");
                        }
                    }
                    else
                    {
                        // Past end of file
                        if (this.showLineNumbers)
                        {
                            AnsiConsole.Markup($"[{this.gutterFgColor.ToMarkup()} on {this.gutterBgColor.ToMarkup()}]{new string(' ', gutterWidth)}[/]");
                        }

                        AnsiConsole.Markup($"[dim]~[/]{new string(' ', contentWidth - 1)}");
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
                            AnsiConsole.Markup($"[{this.gutterFgColor.ToMarkup()} on {this.gutterBgColor.ToMarkup()}]{lineNum} [/]");
                        }
                        else
                        {
                            AnsiConsole.Markup($"[{this.gutterFgColor.ToMarkup()} on {this.gutterBgColor.ToMarkup()}]{new string(' ', gutterWidth)}[/]");
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

                        if (lineText.Length > contentWidth)
                        {
                            lineText = lineText.Substring(0, contentWidth - 1) + ">";
                        }
                        else
                        {
                            lineText = lineText.PadRight(contentWidth);
                        }

                        // Render with selection highlighting
                        this.RenderLineWithSelection(lineIndex, lineText, gutterWidth, contentWidth);
                    }
                    else
                    {
                        AnsiConsole.Markup($"[dim]~[/]{new string(' ', contentWidth - 1)}");
                    }
                }
            }

            // Status bar
            System.Console.SetCursorPosition(0, windowHeight - 2);
            var status = this.GetStatusMessage();
            var position = $"Ln {this.cursorRow + 1}, Col {this.cursorCol + 1}";
            if (this.detectedTabDepth > 0)
            {
                position += $" Tab:{this.tabDepth}";
            }

            // Progress bar
            var progressBarWidth = 20;
            var progressBarSpace = string.Empty;
            if (this.uploadProgress >= 0)
            {
                var filled = (int)((this.uploadProgress / 100.0) * progressBarWidth);
                var empty = progressBarWidth - filled;
                progressBarSpace = $" [{new string('█', filled)}{new string('░', empty)}] {this.uploadProgress,3}%";
            }

            var statusPadding = windowWidth - status.Length - position.Length - progressBarSpace.Length;
            if (statusPadding < 0)
            {
                statusPadding = 0;
            }

            AnsiConsole.Markup($"[white on grey]{status.EscapeMarkup()}{new string(' ', statusPadding)}{position}{progressBarSpace}[/]");

            // Help bar
            System.Console.SetCursorPosition(0, windowHeight - 1);
            var help = this.keyBindings.GetShortcutHints();
            AnsiConsole.Markup($"[cyan on black]{help.PadRight(windowWidth).EscapeMarkup()}[/]");

            // Position cursor
            var displayRow = this.cursorRow - this.scrollOffsetY + 1;
            var displayCol = gutterWidth + (this.wordWrapEnabled ? this.cursorCol : this.cursorCol - this.scrollOffsetX);
            displayCol = Math.Max(gutterWidth, Math.Min(displayCol, windowWidth - 1));
            System.Console.SetCursorPosition(displayCol, displayRow);
            System.Console.CursorVisible = true;
        }

        private void RenderLineWithSelection(int lineIndex, string lineText, int gutterWidth, int contentWidth)
        {
            if (!this.hasSelection)
            {
                System.Console.Write(lineText);
                return;
            }

            // Normalize selection range
            var (startRow, startCol, endRow, endCol) = this.GetNormalizedSelection();

            // Check if this line is within selection
            if (lineIndex < startRow || lineIndex > endRow)
            {
                System.Console.Write(lineText);
                return;
            }

            var lineStart = 0;
            var lineEnd = lineText.Length;

            if (lineIndex == startRow)
            {
                lineStart = Math.Max(0, startCol - (this.wordWrapEnabled ? 0 : this.scrollOffsetX));
            }

            if (lineIndex == endRow)
            {
                lineEnd = Math.Min(lineText.Length, endCol - (this.wordWrapEnabled ? 0 : this.scrollOffsetX));
            }

            // Render before selection
            if (lineStart > 0)
            {
                System.Console.Write(lineText.Substring(0, lineStart));
            }

            // Render selection
            if (lineEnd > lineStart)
            {
                AnsiConsole.Markup($"[black on white]{lineText.Substring(lineStart, lineEnd - lineStart).EscapeMarkup()}[/]");
            }

            // Render after selection
            if (lineEnd < lineText.Length)
            {
                System.Console.Write(lineText.Substring(lineEnd));
            }
        }

        private (int startRow, int startCol, int endRow, int endCol) GetNormalizedSelection()
        {
            if (this.selectionStartRow < this.selectionEndRow ||
                (this.selectionStartRow == this.selectionEndRow && this.selectionStartCol <= this.selectionEndCol))
            {
                return (this.selectionStartRow, this.selectionStartCol, this.selectionEndRow, this.selectionEndCol);
            }

            return (this.selectionEndRow, this.selectionEndCol, this.selectionStartRow, this.selectionStartCol);
        }

        private string GetStatusMessage()
        {
            if (DateTime.Now - this.statusMessageTime < TimeSpan.FromSeconds(3))
            {
                return this.statusMessage;
            }

            return string.Empty;
        }

        private void SetStatusMessage(string message)
        {
            this.statusMessage = message;
            this.statusMessageTime = DateTime.Now;
        }

        private void HandleKeySync(ConsoleKeyInfo key, CancellationToken cancellationToken)
        {
            // Check for editor actions first
            var action = this.keyBindings.GetAction(key);
            switch (action)
            {
                case EditorAction.Exit:
                    this.HandleExitSync(cancellationToken);
                    return;
                case EditorAction.Save:
                    this.HandleSaveSync(cancellationToken);
                    return;
                case EditorAction.Copy:
                    this.HandleCopy();
                    return;
                case EditorAction.Cut:
                    this.HandleCut();
                    return;
                case EditorAction.Paste:
                    this.HandlePaste();
                    return;
                case EditorAction.CutLine:
                    this.HandleCutLine();
                    return;
                case EditorAction.Help:
                    this.ShowHelp();
                    return;
                case EditorAction.ToggleLineNumbers:
                    this.showLineNumbers = !this.showLineNumbers;
                    this.SetStatusMessage($"Line numbers: {(this.showLineNumbers ? "on" : "off")}");
                    return;
                case EditorAction.ToggleWordWrap:
                    this.wordWrapEnabled = !this.wordWrapEnabled;
                    this.scrollOffsetX = 0;
                    this.SetStatusMessage($"Word wrap: {(this.wordWrapEnabled ? "on" : "off")}");
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
                    var pageUp = System.Console.WindowHeight - 3;
                    this.cursorRow = Math.Max(0, this.cursorRow - pageUp);
                    this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
                    this.UpdateSelection(isSelecting);
                    break;

                case ConsoleKey.PageDown:
                    var pageDown = System.Console.WindowHeight - 3;
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

                case ConsoleKey.Tab:
                    this.DeleteSelection();
                    this.InsertText(new string(' ', this.tabDepth));
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

        private void StartSelection()
        {
            this.selectionStartRow = this.cursorRow;
            this.selectionStartCol = this.cursorCol;
            this.selectionEndRow = this.cursorRow;
            this.selectionEndCol = this.cursorCol;
            this.hasSelection = true;
        }

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

        private void ClearSelection()
        {
            this.hasSelection = false;
            this.selectionStartRow = -1;
            this.selectionStartCol = -1;
            this.selectionEndRow = -1;
            this.selectionEndCol = -1;
        }

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

        private void DeleteSelection()
        {
            if (!this.hasSelection)
            {
                return;
            }

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

        private void MoveCursorUp(bool isSelecting)
        {
            if (this.cursorRow > 0)
            {
                this.cursorRow--;
                this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
            }

            this.UpdateSelection(isSelecting);
        }

        private void MoveCursorDown(bool isSelecting)
        {
            if (this.cursorRow < this.lines.Count - 1)
            {
                this.cursorRow++;
                this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
            }

            this.UpdateSelection(isSelecting);
        }

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

        private void InsertChar(char c)
        {
            this.lines[this.cursorRow].Insert(this.cursorCol, c);
            this.cursorCol++;
            this.modified = true;
        }

        private void InsertText(string text)
        {
            this.lines[this.cursorRow].Insert(this.cursorCol, text);
            this.cursorCol += text.Length;
            this.modified = true;
        }

        private void HandleEnter()
        {
            var currentLine = this.lines[this.cursorRow];
            var afterCursor = currentLine.ToString(this.cursorCol, currentLine.Length - this.cursorCol);
            currentLine.Remove(this.cursorCol, currentLine.Length - this.cursorCol);

            this.cursorRow++;
            this.lines.Insert(this.cursorRow, new StringBuilder(afterCursor));
            this.cursorCol = 0;
            this.modified = true;
        }

        private void HandleBackspace()
        {
            if (this.cursorCol > 0)
            {
                this.lines[this.cursorRow].Remove(this.cursorCol - 1, 1);
                this.cursorCol--;
                this.modified = true;
            }
            else if (this.cursorRow > 0)
            {
                var currentLine = this.lines[this.cursorRow].ToString();
                this.lines.RemoveAt(this.cursorRow);
                this.cursorRow--;
                this.cursorCol = this.lines[this.cursorRow].Length;
                this.lines[this.cursorRow].Append(currentLine);
                this.modified = true;
            }
        }

        private void HandleDelete()
        {
            if (this.cursorCol < this.lines[this.cursorRow].Length)
            {
                this.lines[this.cursorRow].Remove(this.cursorCol, 1);
                this.modified = true;
            }
            else if (this.cursorRow < this.lines.Count - 1)
            {
                var nextLine = this.lines[this.cursorRow + 1].ToString();
                this.lines.RemoveAt(this.cursorRow + 1);
                this.lines[this.cursorRow].Append(nextLine);
                this.modified = true;
            }
        }

        private void HandleCopy()
        {
            if (this.hasSelection)
            {
                this.clipboard = this.GetSelectedText();
                this.SetStatusMessage("Text copied.");
            }
            else
            {
                this.SetStatusMessage("No selection to copy.");
            }
        }

        private void HandleCut()
        {
            if (this.hasSelection)
            {
                this.clipboard = this.GetSelectedText();
                this.DeleteSelection();
                this.SetStatusMessage("Text cut.");
            }
            else
            {
                this.SetStatusMessage("No selection to cut.");
            }
        }

        private void HandlePaste()
        {
            if (!string.IsNullOrEmpty(this.clipboard))
            {
                this.DeleteSelection();

                var clipboardLines = this.clipboard.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                for (int i = 0; i < clipboardLines.Length; i++)
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

        private void HandleCutLine()
        {
            this.clipboard = this.lines[this.cursorRow].ToString() + Environment.NewLine;
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

        private void HandleSaveSync(CancellationToken cancellationToken)
        {
            this.SaveFile();

            try
            {
                this.onSaveCallback().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                this.SetStatusMessage($"Upload failed: {ex.Message}");
            }
        }

        private void HandleExitSync(CancellationToken cancellationToken)
        {
            if (this.modified)
            {
                this.SetStatusMessage("Save changes before exit? (Y)es/(N)o/(C)ancel");
                this.Render();

                while (true)
                {
                    if (System.Console.KeyAvailable)
                    {
                        var key = System.Console.ReadKey(true);
                        switch (char.ToUpperInvariant(key.KeyChar))
                        {
                            case 'Y':
                                this.HandleSaveSync(cancellationToken);
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

                    Thread.Sleep(50);
                }
            }
            else
            {
                this.running = false;
            }
        }

        private void ShowHelp()
        {
            AnsiConsole.AlternateScreen(() =>
            {
                var helpLines = this.keyBindings.GetHelpText();
                var windowHeight = System.Console.WindowHeight;
                var windowWidth = System.Console.WindowWidth;

                System.Console.CursorVisible = false;

                // Header
                System.Console.SetCursorPosition(0, 0);
                var header = "Editor Help";
                var paddingLeft = Math.Max(0, (windowWidth - header.Length) / 2);
                AnsiConsole.Markup($"[white on blue]{new string(' ', paddingLeft)}{header}{new string(' ', windowWidth - paddingLeft - header.Length)}[/]");

                // Content
                for (int i = 0; i < Math.Min(helpLines.Length, windowHeight - 3); i++)
                {
                    System.Console.SetCursorPosition(0, i + 1);
                    System.Console.Write(helpLines[i].PadRight(windowWidth));
                }

                // Footer
                System.Console.SetCursorPosition(0, windowHeight - 1);
                AnsiConsole.Markup("[cyan on black] Press Ctrl+Q to return to editor [/]".PadRight(windowWidth));

                // Wait for exit
                while (true)
                {
                    if (System.Console.KeyAvailable)
                    {
                        var key = System.Console.ReadKey(true);
                        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.Q)
                        {
                            break;
                        }
                    }

                    Thread.Sleep(50);
                }
            });
        }
    }
}
