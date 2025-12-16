// <copyright file="IEditorSettings.cs">
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

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace AvConsoleToolkit.Configuration
{
    /// <summary>
    /// Defines settings for file editors used in remote file editing.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public interface IEditorSettings
    {
        /// <summary>
        /// Gets or sets the editor mappings by file extension.
        /// Format: ".ext=editor_path;.ext2=editor_path2"
        /// Example: ".txt=notepad.exe;.cs=code;.xml=notepad++"
        /// If no mapping is found for a file type, the built-in editor will be used.
        /// </summary>
        [DefaultValue("")]
        string EditorMappings { get; set; }

        /// <summary>
        /// Gets or sets the default external editor to use when no specific mapping is found.
        /// If empty, the built-in nano-like editor will be used.
        /// Example: "code", "notepad", "vim"
        /// </summary>
        [DefaultValue("")]
        string DefaultEditor { get; set; }
    }
}
