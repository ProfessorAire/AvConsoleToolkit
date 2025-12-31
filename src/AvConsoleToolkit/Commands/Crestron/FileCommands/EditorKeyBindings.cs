// <copyright file="EditorKeyBindings.cs">
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

namespace AvConsoleToolkit.Commands.Crestron.FileCommands
{
    /// <summary>
    /// Defines editor actions that can be bound to key combinations.
    /// </summary>
    public enum EditorAction
    {
        /// <summary>No action.</summary>
        None,

        /// <summary>Exit the editor.</summary>
        Exit,

        /// <summary>Save the file.</summary>
        Save,

        /// <summary>Copy selected text.</summary>
        Copy,

        /// <summary>Cut selected text.</summary>
        Cut,

        /// <summary>Paste from clipboard.</summary>
        Paste,

        /// <summary>Cut the current line.</summary>
        CutLine,

        /// <summary>Show help screen.</summary>
        Help,

        /// <summary>Toggle line numbers.</summary>
        ToggleLineNumbers,

        /// <summary>Toggle word wrap.</summary>
        ToggleWordWrap,

        /// <summary>Undo the last action.</summary>
        Undo,
    }

    /// <summary>
    /// Provides default key bindings for the built-in editor.
    /// This class can be extended to support customizable key bindings in the future.
    /// </summary>
    public class EditorKeyBindings
    {
        /// <summary>
        /// Gets the default key bindings instance.
        /// </summary>
        public static EditorKeyBindings Default { get; } = new EditorKeyBindings();

        /// <summary>
        /// Gets the editor action for the given key combination.
        /// </summary>
        /// <param name="key">The console key info.</param>
        /// <returns>The editor action to perform.</returns>
        public virtual EditorAction GetAction(ConsoleKeyInfo key)
        {
            // F2 for save (works in all terminals, Ctrl+S is often intercepted by PowerShell)
            if (key.Key == ConsoleKey.F2 && key.Modifiers == 0)
            {
                return EditorAction.Save;
            }

            if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                return key.Key switch
                {
                    ConsoleKey.Q => EditorAction.Exit,
                    ConsoleKey.S => EditorAction.Save,
                    ConsoleKey.C => EditorAction.Copy,
                    ConsoleKey.X => EditorAction.Cut,
                    ConsoleKey.U => EditorAction.Paste, // Ctrl+V is intercepted by many terminals, so we use Ctrl+U
                    ConsoleKey.K => EditorAction.CutLine,
                    ConsoleKey.G => EditorAction.Help,
                    ConsoleKey.W => EditorAction.ToggleWordWrap,
                    ConsoleKey.Z => EditorAction.Undo,
                    _ => EditorAction.None
                };
            }

            // Ctrl+` (backtick) for toggling line numbers
            if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.KeyChar == '`')
            {
                return EditorAction.ToggleLineNumbers;
            }

            return EditorAction.None;
        }

        /// <summary>
        /// Gets the help text describing the key bindings.
        /// </summary>
        /// <returns>Array of help text lines.</returns>
        public virtual string[] GetHelpText()
        {
            return new[]
            {
                "Built-in Editor Help",
                "",
                "Navigation:",
                "  Arrow keys    Move cursor",
                "  Home/End      Go to start/end of line",
                "  Page Up/Down  Scroll page up/down",
                "  Ctrl+Home     Go to start of document",
                "  Ctrl+End      Go to end of document",
                "",
                "Selection:",
                "  Shift+Arrow        Select text",
                "  Ctrl+Shift+Arrow   Select word",
                "  Ctrl+A             Select all",
                "",
                "Editing:",
                "  F2        Save file (or Ctrl+S)",
                "  Ctrl+Z    Undo",
                "  Ctrl+C    Copy selection",
                "  Ctrl+X    Cut selection",
                "  Ctrl+U    Paste",
                "  Ctrl+K    Cut line",
                "",
                "View:",
                "  Ctrl+`    Toggle line numbers",
                "  Ctrl+W    Toggle word wrap",
                "",
                "Other:",
                "  Ctrl+G    Show this help",
                "  Ctrl+Q    Exit editor",
            };
        }

        /// <summary>
        /// Gets the shortcut hint text for display in the editor footer.
        /// </summary>
        /// <returns>The shortcut hint text.</returns>
        public virtual string GetShortcutHints()
        {
            return " ^Q Exit  F2 Save  ^Z Undo  ^G Help  ^C Copy  ^U Paste";
        }
    }
}
