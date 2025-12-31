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

// The built-in editor requires low-level console control for alternate screen buffer,
// cursor positioning, and color management that Spectre.Console doesn't provide.
#pragma warning disable Spectre1000 // Use AnsiConsole instead of System.Console

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace AvConsoleToolkit.Commands.Crestron.File
{
    /// <summary>
    /// A built-in nano-like text editor that operates in an alternate screen buffer.
    /// Provides basic text editing capabilities with keyboard navigation.
    /// </summary>
    public sealed class BuiltinEditor
    {
        private readonly string filePath;
        private readonly string displayName;
        private readonly Func<Task> onSaveCallback;
        private readonly List<StringBuilder> lines = new();

        private int cursorRow;
        private int cursorCol;
        private int scrollOffset;
        private bool modified;
        private bool running;
        private string statusMessage = string.Empty;
        private DateTime statusMessageTime = DateTime.MinValue;
        private int uploadProgress = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="BuiltinEditor"/> class.
        /// </summary>
        /// <param name="filePath">Path to the local file to edit.</param>
        /// <param name="displayName">Display name shown in the editor header.</param>
        /// <param name="onSaveCallback">Callback invoked when the file is saved.</param>
        public BuiltinEditor(string filePath, string displayName, Func<Task> onSaveCallback)
        {
            this.filePath = filePath;
            this.displayName = displayName;
            this.onSaveCallback = onSaveCallback;
        }

        /// <summary>
        /// Gets or sets the current upload progress (0-100). Set to -1 to hide the progress bar.
        /// </summary>
        public int UploadProgress
        {
            get => this.uploadProgress;
            set
            {
                this.uploadProgress = value;
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

            // Enter alternate screen buffer
            Console.Write("\x1b[?1049h");
            Console.CursorVisible = false;

            try
            {
                while (this.running && !cancellationToken.IsCancellationRequested)
                {
                    this.Render();

                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        await this.HandleKeyAsync(key, cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(50, cancellationToken);
                    }
                }

                return !this.modified;
            }
            finally
            {
                // Leave alternate screen buffer
                Console.Write("\x1b[?1049l");
                Console.CursorVisible = true;
            }
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
            }

            if (this.lines.Count == 0)
            {
                this.lines.Add(new StringBuilder());
            }

            this.cursorRow = 0;
            this.cursorCol = 0;
            this.scrollOffset = 0;
            this.modified = false;
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
            var windowWidth = Console.WindowWidth;
            var windowHeight = Console.WindowHeight;
            var editorHeight = windowHeight - 3; // Header, status bar, help bar

            // Clear screen and move cursor to top
            Console.SetCursorPosition(0, 0);

            // Header bar
            var header = $" ACT Editor - {this.displayName}";
            if (this.modified)
            {
                header += " [Modified]";
            }

            header = header.PadRight(windowWidth);
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(header);
            Console.ResetColor();

            // Adjust scroll offset
            if (this.cursorRow < this.scrollOffset)
            {
                this.scrollOffset = this.cursorRow;
            }
            else if (this.cursorRow >= this.scrollOffset + editorHeight)
            {
                this.scrollOffset = this.cursorRow - editorHeight + 1;
            }

            // Editor content
            for (int i = 0; i < editorHeight; i++)
            {
                var lineIndex = this.scrollOffset + i;
                Console.SetCursorPosition(0, i + 1);

                if (lineIndex < this.lines.Count)
                {
                    var lineText = this.lines[lineIndex].ToString();
                    if (lineText.Length > windowWidth)
                    {
                        lineText = lineText.Substring(0, windowWidth - 1) + ">";
                    }
                    else
                    {
                        lineText = lineText.PadRight(windowWidth);
                    }

                    Console.Write(lineText);
                }
                else
                {
                    Console.Write("~".PadRight(windowWidth));
                }
            }

            // Status bar
            Console.SetCursorPosition(0, windowHeight - 2);
            var status = this.GetStatusMessage();
            var position = $"Ln {this.cursorRow + 1}, Col {this.cursorCol + 1}";

            // Calculate available space for progress bar
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

            Console.BackgroundColor = ConsoleColor.DarkGray;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(status + new string(' ', statusPadding) + position + progressBarSpace);
            Console.ResetColor();

            // Help bar
            Console.SetCursorPosition(0, windowHeight - 1);
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.Cyan;
            var help = " ^X Exit  ^O Save  ^G Help  ^K Cut Line  ^U Paste";
            Console.Write(help.PadRight(windowWidth));
            Console.ResetColor();

            // Position cursor
            var displayRow = this.cursorRow - this.scrollOffset + 1;
            var displayCol = Math.Min(this.cursorCol, windowWidth - 1);
            Console.SetCursorPosition(displayCol, displayRow);
            Console.CursorVisible = true;
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

        private StringBuilder? cutBuffer;

        private async Task HandleKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
        {
            // Handle Ctrl combinations
            if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                switch (key.Key)
                {
                    case ConsoleKey.X:
                        await this.HandleExitAsync(cancellationToken);
                        return;

                    case ConsoleKey.O:
                        await this.HandleSaveAsync(cancellationToken);
                        return;

                    case ConsoleKey.K:
                        this.HandleCutLine();
                        return;

                    case ConsoleKey.U:
                        this.HandlePaste();
                        return;

                    case ConsoleKey.G:
                        this.SetStatusMessage("Help: Use arrow keys to navigate. Ctrl+X to exit, Ctrl+O to save.");
                        return;
                }
            }

            // Handle navigation and editing keys
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (this.cursorRow > 0)
                    {
                        this.cursorRow--;
                        this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
                    }

                    break;

                case ConsoleKey.DownArrow:
                    if (this.cursorRow < this.lines.Count - 1)
                    {
                        this.cursorRow++;
                        this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
                    }

                    break;

                case ConsoleKey.LeftArrow:
                    if (this.cursorCol > 0)
                    {
                        this.cursorCol--;
                    }
                    else if (this.cursorRow > 0)
                    {
                        this.cursorRow--;
                        this.cursorCol = this.lines[this.cursorRow].Length;
                    }

                    break;

                case ConsoleKey.RightArrow:
                    if (this.cursorCol < this.lines[this.cursorRow].Length)
                    {
                        this.cursorCol++;
                    }
                    else if (this.cursorRow < this.lines.Count - 1)
                    {
                        this.cursorRow++;
                        this.cursorCol = 0;
                    }

                    break;

                case ConsoleKey.Home:
                    this.cursorCol = 0;
                    break;

                case ConsoleKey.End:
                    this.cursorCol = this.lines[this.cursorRow].Length;
                    break;

                case ConsoleKey.PageUp:
                    var pageUp = Console.WindowHeight - 3;
                    this.cursorRow = Math.Max(0, this.cursorRow - pageUp);
                    this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
                    break;

                case ConsoleKey.PageDown:
                    var pageDown = Console.WindowHeight - 3;
                    this.cursorRow = Math.Min(this.lines.Count - 1, this.cursorRow + pageDown);
                    this.cursorCol = Math.Min(this.cursorCol, this.lines[this.cursorRow].Length);
                    break;

                case ConsoleKey.Enter:
                    this.HandleEnter();
                    break;

                case ConsoleKey.Backspace:
                    this.HandleBackspace();
                    break;

                case ConsoleKey.Delete:
                    this.HandleDelete();
                    break;

                case ConsoleKey.Tab:
                    this.InsertText("    ");
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        this.InsertChar(key.KeyChar);
                    }

                    break;
            }
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

        private void HandleCutLine()
        {
            this.cutBuffer = this.lines[this.cursorRow];
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
            this.SetStatusMessage("Line cut to buffer.");
        }

        private void HandlePaste()
        {
            if (this.cutBuffer != null)
            {
                this.lines.Insert(this.cursorRow, new StringBuilder(this.cutBuffer.ToString()));
                this.modified = true;
                this.SetStatusMessage("Line pasted.");
            }
            else
            {
                this.SetStatusMessage("Buffer is empty.");
            }
        }

        private async Task HandleSaveAsync(CancellationToken cancellationToken)
        {
            this.SaveFile();

            // Invoke the upload callback with progress updates
            try
            {
                await this.onSaveCallback();
            }
            catch (Exception ex)
            {
                this.SetStatusMessage($"Upload failed: {ex.Message}");
            }
        }

        private async Task HandleExitAsync(CancellationToken cancellationToken)
        {
            if (this.modified)
            {
                this.SetStatusMessage("Save changes before exit? (Y)es/(N)o/(C)ancel");
                this.Render();

                while (true)
                {
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
            }
            else
            {
                this.running = false;
            }
        }
    }
}
