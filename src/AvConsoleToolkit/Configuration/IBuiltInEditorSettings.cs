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

        /// <summary>
        /// Gets or sets the editor background color in hexadecimal format.
        /// Default is Nord Polar Night 0 (#2E3440).
        /// </summary>
        [DefaultValue("#2E3440")]
        string EditorBackgroundColor { get; set; }

        /// <summary>
        /// Gets or sets the editor foreground color in hexadecimal format.
        /// Default is Nord Snow Storm 2 (#ECEFF4).
        /// </summary>
        [DefaultValue("#ECEFF4")]
        string EditorForegroundColor { get; set; }

        /// <summary>
        /// Gets or sets the status bar background color in hexadecimal format.
        /// Default is Nord Polar Night 1 (#3B4252).
        /// </summary>
        [DefaultValue("#3B4252")]
        string StatusBarBackgroundColor { get; set; }

        /// <summary>
        /// Gets or sets the status bar foreground color in hexadecimal format.
        /// Default is Nord Snow Storm 2 (#ECEFF4).
        /// </summary>
        [DefaultValue("#ECEFF4")]
        string StatusBarForegroundColor { get; set; }

        /// <summary>
        /// Gets or sets the hint bar background color in hexadecimal format.
        /// Default is Nord Polar Night 2 (#434C5E).
        /// </summary>
        [DefaultValue("#434C5E")]
        string HintBarBackgroundColor { get; set; }

        /// <summary>
        /// Gets or sets the hint bar foreground color in hexadecimal format.
        /// Default is Nord Frost 1 (#88C0D0).
        /// </summary>
        [DefaultValue("#88C0D0")]
        string HintBarForegroundColor { get; set; }

        /// <summary>
        /// Gets or sets the color used for glyphs (word wrap indicator, overflow indicator).
        /// Default is Nord Polar Night 3 (#4C566A).
        /// </summary>
        [DefaultValue("#4C566A")]
        string GlyphColor { get; set; }

        /// <summary>
        /// Gets or sets the current theme name ("Dark" or "Bright").
        /// </summary>
        [DefaultValue("Dark")]
        string Theme { get; set; }
    }
}
