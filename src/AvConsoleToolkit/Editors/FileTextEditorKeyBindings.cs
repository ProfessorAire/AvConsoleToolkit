// <copyright file="FileTextEditorKeyBindings.cs">
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

namespace AvConsoleToolkit.Editors
{
    /// <summary>
    /// Provides default key bindings for the built-in editor.
    /// This class can be extended to support customizable key bindings in the future.
    /// </summary>
    public class FileTextEditorKeyBindings
    {
        /// <summary>
        /// Default key bindings for the editor.
        /// </summary>
        public static readonly FileTextEditorKeyBindings Default = new();

        /// <summary>
        /// Gets the editor action for the given key combination.
        /// </summary>
        /// <param name="key">The console key info.</param>
        /// <returns>The editor action to perform.</returns>
        public virtual FileTextEditorAction GetAction(ConsoleKeyInfo key)
        {
            if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                return key.Key switch
                {
                    ConsoleKey.Q => FileTextEditorAction.Exit,
                    ConsoleKey.O => FileTextEditorAction.Save, // Ctrl+O for save (Ctrl+S is intercepted by PowerShell)
                    ConsoleKey.C => FileTextEditorAction.Copy,
                    ConsoleKey.X => FileTextEditorAction.Cut,
                    ConsoleKey.U => FileTextEditorAction.Paste, // Ctrl+V is intercepted by many terminals, so we use Ctrl+U
                    ConsoleKey.K => FileTextEditorAction.CutLine,
                    ConsoleKey.G => FileTextEditorAction.Help,
                    ConsoleKey.W => FileTextEditorAction.ToggleWordWrap,
                    ConsoleKey.Z => FileTextEditorAction.Undo,
                    ConsoleKey.Y => FileTextEditorAction.Redo,
                    ConsoleKey.F2 => FileTextEditorAction.CycleTheme,
                    ConsoleKey.Oem3 => FileTextEditorAction.ToggleLineNumbers, // Backtick ` toggles line numbers.
                    ConsoleKey.F => FileTextEditorAction.Search,
                    ConsoleKey.H => FileTextEditorAction.Replace,
                    _ => FileTextEditorAction.None
                };
            }

            if (key.Modifiers.HasFlag(ConsoleModifiers.Alt))
            {
                return key.Key switch
                {
                    ConsoleKey.N => FileTextEditorAction.FindNext,
                    ConsoleKey.R => FileTextEditorAction.ReplaceCurrent,
                    ConsoleKey.A => FileTextEditorAction.ReplaceAll,
                    _ => FileTextEditorAction.None
                };
            }

            return FileTextEditorAction.None;
        }

        /// <summary>
        /// Gets the help text describing the key bindings.
        /// </summary>
        /// <returns>Array of help text lines.</returns>
        public virtual string[] GetHelpText()
        {
            return
            [
                "Built-in Editor Help",
string.Empty,
                "Navigation:",
                "  Arrow keys         Move cursor",
                "  Home/End           Go to start/end of line",
                "  Page Up/Down       Scroll page up/down",
                "  Ctrl+Home          Go to start of document",
                "  Ctrl+End           Go to end of document",
string.Empty,
                "Selection:",
                "  Shift+Arrow        Select text",
                "  Ctrl+Shift+Arrow   Select word",
                "  Ctrl+A             Select all",
string.Empty,
                "Editing:",
                "  Ctrl+O              Save file",
                "  Ctrl+Z              Undo",
                "  Ctrl+Y              Redo",
                "  Ctrl+C              Copy selection",
                "  Ctrl+X              Cut selection",
                "  Ctrl+U              Paste",
                "  Ctrl+K              Cut line",
string.Empty,
                "Search/Replace:",
                "  Ctrl+F              Search for text",
                "  Ctrl+H              Replace text",
                "  Alt+N               Find next occurrence",
                "  Alt+R               Replace current occurrence",
                "  Alt+A               Replace all occurrences",
                "  Escape              Close search/replace",
string.Empty,
                "View:",
                "  Ctrl+`              Toggle line numbers",
                "  Ctrl+W              Toggle word wrap",
                "  Ctrl+F2             Cycle theme",
string.Empty,
                "Other:",
                "  Ctrl+G              Show this help",
                "  Ctrl+Q              Exit editor",
            ];
        }

        /// <summary>
        /// Gets the shortcut hint text for display in the editor footer.
        /// </summary>
        /// <returns>The shortcut hint text.</returns>
        public virtual string GetShortcutHints()
        {
            return " ^Q Exit  ^O Save  ^G Help";
        }
    }
}
