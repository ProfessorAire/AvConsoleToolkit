// <copyright file="EditorAction.cs">
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

namespace AvConsoleToolkit.Editors
{
    /// <summary>
    /// Defines editor actions that can be bound to key combinations.
    /// </summary>
    public enum FileTextEditorAction
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

        /// <summary>Toggle between Dark and Bright themes.</summary>
        ToggleTheme,
    }
}
