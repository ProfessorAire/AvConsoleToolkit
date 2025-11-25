// <copyright file="CommandHistory.cs">
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
using System.Threading.Tasks;

namespace AvConsoleToolkit.Commands
{
    /// <summary>
    /// Manages command history for pass-through sessions on a per-device basis.
    /// Handles persistence to disk, navigation, and deduplication.
    /// </summary>
    internal class CommandHistory
    {
        private readonly List<string> commands = [];

        private readonly string historyFilePath;

        private readonly int maxHistorySize;

        private int currentPosition = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandHistory"/> class.
        /// </summary>
        /// <param name="deviceIdentifier">Unique identifier for the device (hostname or IP).</param>
        /// <param name="maxHistorySize">Maximum number of commands to keep in history (default: 50).</param>
        public CommandHistory(string deviceIdentifier, int maxHistorySize = 50)
        {
            this.maxHistorySize = maxHistorySize;

            // Create a safe filename from the device identifier
            var safeDeviceName = string.Join("_", deviceIdentifier.Split(Path.GetInvalidFileNameChars()));
            var historyDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AvConsoleToolkit",
                "History");

            Directory.CreateDirectory(historyDirectory);
            this.historyFilePath = Path.Combine(historyDirectory, $"{safeDeviceName}.history");
        }

        /// <summary>
        /// Gets the total count of commands in the history.
        /// </summary>
        public int Count => this.commands.Count;

        /// <summary>
        /// Adds a command to the history, avoiding sequential duplicates.
        /// </summary>
        /// <param name="command">The command to add.</param>
        public void AddCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            // Don't add if it's the same as the last command (sequential deduplication)
            if (this.commands.Count > 0 && this.commands[^1].Equals(command, StringComparison.Ordinal))
            {
                this.currentPosition = this.commands.Count;
                return;
            }

            this.commands.Add(command);

            // Trim to max size if exceeded
            if (this.commands.Count > this.maxHistorySize)
            {
                this.commands.RemoveAt(0);
            }

            this.currentPosition = this.commands.Count;
        }

        /// <summary>
        /// Gets the next command in history (moves forward in time).
        /// </summary>
        /// <returns>The next command, or null if at the end of history.</returns>
        public string? GetNext()
        {
            if (this.commands.Count == 0)
            {
                return null;
            }

            if (this.currentPosition < this.commands.Count - 1)
            {
                this.currentPosition++;
                return this.commands[this.currentPosition];
            }

            // At the end, return null to represent "empty" (current input)
            if (this.currentPosition == this.commands.Count - 1)
            {
                this.currentPosition = this.commands.Count;
            }

            return null;
        }

        /// <summary>
        /// Gets the previous command in history (moves backward in time).
        /// </summary>
        /// <returns>The previous command, or null if at the beginning of history.</returns>
        public string? GetPrevious()
        {
            if (this.commands.Count == 0)
            {
                return null;
            }

            // If at the end (past all commands), move to the last command
            if (this.currentPosition >= this.commands.Count)
            {
                this.currentPosition = this.commands.Count - 1;
                return this.commands[this.currentPosition];
            }

            // If already at the beginning, can't go back further
            if (this.currentPosition <= 0)
            {
                return null;
            }

            // Move backwards in history
            this.currentPosition--;
            return this.commands[this.currentPosition];
        }

        /// <summary>
        /// Loads command history from disk asynchronously.
        /// </summary>
        /// <returns>A task representing the asynchronous load operation.</returns>
        public async Task LoadAsync()
        {
            if (!File.Exists(this.historyFilePath))
            {
                return;
            }

            try
            {
                var lines = await File.ReadAllLinesAsync(this.historyFilePath, Encoding.UTF8);
                this.commands.Clear();
                this.commands.AddRange(lines.Where(line => !string.IsNullOrWhiteSpace(line)));

                // Trim to max size if loaded file exceeds limit
                if (this.commands.Count > this.maxHistorySize)
                {
                    var excess = this.commands.Count - this.maxHistorySize;
                    this.commands.RemoveRange(0, excess);
                }

                this.currentPosition = this.commands.Count;
            }
            catch (Exception)
            {
                // Silently ignore errors loading history
                this.commands.Clear();
                this.currentPosition = -1;
            }
        }

        /// <summary>
        /// Resets the navigation position to the end of the history.
        /// </summary>
        public void ResetPosition()
        {
            this.currentPosition = this.commands.Count;
        }

        /// <summary>
        /// Saves command history to disk asynchronously.
        /// </summary>
        /// <returns>A task representing the asynchronous save operation.</returns>
        public async Task SaveAsync()
        {
            try
            {
                await File.WriteAllLinesAsync(this.historyFilePath, this.commands, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Silently ignore errors saving history
            }
        }
    }
}
