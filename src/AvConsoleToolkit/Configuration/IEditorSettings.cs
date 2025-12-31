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

using System.Collections.Generic;
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
        /// Gets the editor mappings by file extension.
        /// <para>In the pattern <c>txt=notepad.exe;json=code</c></para>
        /// </summary>
        [DefaultValue("")]
        string Mappings { get; set; }

        /// <summary>
        /// Gets or sets the name or path of the default editor application used to open files instead of the built-in editor.
        /// </summary>
        [Description("Name or path of the default editor application used to open files instead of the built-in editor.")]
        [DefaultValue("")]
        string DefaultEditor { get; set; }
    }
}
