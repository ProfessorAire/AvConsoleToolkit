// <copyright file="IBuiltInEditorSettings.cs">
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
    /// Defines settings for the built-in text editor.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public interface IBuiltInEditorSettings
    {
        /// <summary>
        /// Gets or sets the default header background color in hexadecimal format.
        /// </summary>
        [DefaultValue("#D08770")]
        string HeaderBackgroundColor { get; set; }

        /// <summary>
        /// Gets or sets the default header foreground color in hexadecimal format.
        /// </summary>
        [DefaultValue("#2E3440")]
        string HeaderForegroundColor { get; set; }

        /// <summary>
        /// Gets or sets the header color mappings by file extension.
        /// Format: "ext=foreground,background;ext2=foreground2,background2"
        /// Example: "json=#3B4252,#B48EAD;txt=#2E3440,#EBCB8B"
        /// </summary>
        [DefaultValue("json=#3B4252,#B48EAD;txt=#2E3440,#EBCB8B")]
        string HeaderColorMappings { get; set; }

        /// <summary>
        /// Gets or sets the gutter background color in hexadecimal format.
        /// </summary>
        [DefaultValue("#3B4252")]
        string GutterBackgroundColor { get; set; }

        /// <summary>
        /// Gets or sets the gutter foreground color in hexadecimal format.
        /// </summary>
        [DefaultValue("#E5E9F0")]
        string GutterForegroundColor { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to show line numbers by default.
        /// </summary>
        [DefaultValue(true)]
        bool ShowLineNumbers { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether word wrap is enabled by default.
        /// </summary>
        [DefaultValue(false)]
        bool WordWrapEnabled { get; set; }

        /// <summary>
        /// Gets or sets the default tab depth (number of spaces per tab).
        /// </summary>
        [DefaultValue(2)]
        int TabDepth { get; set; }

        /// <summary>
        /// Gets or sets the glyph to display at the end of wrapped lines.
        /// Default is the NerdFonts wrap icon (U+EBEA).
        /// </summary>
        [DefaultValue("\uEBEA")]
        string WordWrapGlyph { get; set; }
    }
}
