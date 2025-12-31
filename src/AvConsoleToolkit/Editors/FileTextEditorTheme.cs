// <copyright file="FileTextEditorTheme.cs">
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
using System.IO;
using AvConsoleToolkit.Configuration;
using Spectre.Console;

namespace AvConsoleToolkit.Editors
{
    internal record FileTextEditorTheme()
    {
        /// <summary>
        /// Initializes the built-in file text editor themes, including Nord variants and a user-customizable theme.
        /// </summary>
        /// <remarks>This static constructor sets up several predefined themes based on the Nord color
        /// palette, providing dark, light, semi-dark, and semi-light options. It also creates a user theme using
        /// settings from the application configuration, allowing customization of editor appearance. These themes are
        /// available as static properties for use throughout the application.</remarks>
        static FileTextEditorTheme()
        {
            // Nord Dark - Dark ambiance design using Polar Night palette
            NordDark = new FileTextEditorTheme
            {
                Header = new Style(foreground: Color.FromHex("ECEFF4"), background: Color.FromHex("3B4252")),
                TextEditor = new Style(foreground: Color.FromHex("ECEFF4"), background: Color.FromHex("2E3440")),
                Gutter = new Style(foreground: Color.FromHex("4C566A"), background: Color.FromHex("3B4252")),
                Glyph = new Style(foreground: Color.FromHex("88C0D0"), background: Color.FromHex("3B4252")),
                TabGlyph = new Style(foreground: Color.FromHex("5E81AC"), background: Color.FromHex("2E3440")),
                StatusBar = new Style(foreground: Color.FromHex("ECEFF4"), background: Color.FromHex("3B4252")),
                HintBar = new Style(foreground: Color.FromHex("D8DEE9"), background: Color.FromHex("434C5E"))
            };

            // Nord Light - Bright ambiance design using Snow Storm palette
            NordLight = new FileTextEditorTheme
            {
                Header = new Style(foreground: Color.FromHex("2E3440"), background: Color.FromHex("E5E9F0")),
                TextEditor = new Style(foreground: Color.FromHex("2E3440"), background: Color.FromHex("ECEFF4")),
                Gutter = new Style(foreground: Color.FromHex("4C566A"), background: Color.FromHex("E5E9F0")),
                Glyph = new Style(foreground: Color.FromHex("5E81AC"), background: Color.FromHex("E5E9F0")),
                TabGlyph = new Style(foreground: Color.FromHex("5E81AC"), background: Color.FromHex("ECEFF4")),
                StatusBar = new Style(foreground: Color.FromHex("2E3440"), background: Color.FromHex("E5E9F0")),
                HintBar = new Style(foreground: Color.FromHex("3B4252"), background: Color.FromHex("D8DEE9"))
            };

            // Nord Semi-Light - Dark to bright style (uses nord4 as base)
            NordSemiLight = new FileTextEditorTheme
            {
                Header = new Style(foreground: Color.FromHex("2E3440"), background: Color.FromHex("E5E9F0")),
                TextEditor = new Style(foreground: Color.FromHex("2E3440"), background: Color.FromHex("D8DEE9")),
                Gutter = new Style(foreground: Color.FromHex("4C566A"), background: Color.FromHex("E5E9F0")),
                Glyph = new Style(foreground: Color.FromHex("5E81AC"), background: Color.FromHex("E5E9F0")),
                TabGlyph = new Style(foreground: Color.FromHex("5E81AC"), background: Color.FromHex("D8DEE9")),
                StatusBar = new Style(foreground: Color.FromHex("2E3440"), background: Color.FromHex("E5E9F0")),
                HintBar = new Style(foreground: Color.FromHex("3B4252"), background: Color.FromHex("ECEFF4"))
            };

            // Nord Semi-Dark - Semi-dark variant with elevated UI elements
            NordSemiDark = new FileTextEditorTheme
            {
                Header = new Style(foreground: Color.FromHex("D8DEE9"), background: Color.FromHex("434C5E")),
                TextEditor = new Style(foreground: Color.FromHex("ECEFF4"), background: Color.FromHex("3B4252")),
                Gutter = new Style(foreground: Color.FromHex("4C566A"), background: Color.FromHex("3B4252")),
                Glyph = new Style(foreground: Color.FromHex("88C0D0"), background: Color.FromHex("434C5E")),
                TabGlyph = new Style(foreground: Color.FromHex("5E81AC"), background: Color.FromHex("3B4252")),
                StatusBar = new Style(foreground: Color.FromHex("D8DEE9"), background: Color.FromHex("434C5E")),
                HintBar = new Style(foreground: Color.FromHex("E5E9F0"), background: Color.FromHex("3B4252"))
            };

            // User - Theme generated from AppConfig.Settings.BuiltInEditor
            var settings = AppConfig.Settings.BuiltInEditor;
            User = new FileTextEditorTheme
            {
                Header = new Style(
                    foreground: ParseHexColor(settings.HeaderForegroundColor, NordDark.Header.Foreground),
                    background: ParseHexColor(settings.HeaderBackgroundColor, NordDark.Header.Background)),
                TextEditor = new Style(
                    foreground: ParseHexColor(settings.EditorForegroundColor, NordDark.TextEditor.Foreground),
                    background: ParseHexColor(settings.EditorBackgroundColor, NordDark.TextEditor.Background)),
                Gutter = new Style(
                    foreground: ParseHexColor(settings.GutterForegroundColor, NordDark.TextEditor.Foreground),
                    background: ParseHexColor(settings.GutterBackgroundColor, NordDark.TextEditor.Background)),
                Glyph = new Style(
                    foreground: ParseHexColor(settings.GlyphForegroundColor, NordDark.Glyph.Foreground),
                    background: ParseHexColor(settings.GlyphBackgroundColor, NordDark.Glyph.Background)),
                TabGlyph = new Style(
                    foreground: ParseHexColor(settings.TabGlyphForegroundColor, NordDark.TabGlyph.Foreground),
                    background: ParseHexColor(settings.TabGlyphBackgroundColor, NordDark.TabGlyph.Background)),
                StatusBar = new Style(
                    foreground: ParseHexColor(settings.StatusBarForegroundColor, NordDark.StatusBar.Foreground),
                    background: ParseHexColor(settings.StatusBarBackgroundColor, NordDark.StatusBar.Background)),
                HintBar = new Style(
                    foreground: ParseHexColor(settings.HintBarForegroundColor, NordDark.HintBar.Foreground),
                    background: ParseHexColor(settings.HintBarBackgroundColor, NordDark.HintBar.Background))
            };
        }

        /// <summary>
        /// Gets the Nord Dark theme for the file text editor.
        /// </summary>
        /// <remarks>The Nord Dark theme provides a dark color scheme inspired by the Nord palette,
        /// designed for comfortable code editing in low-light environments.</remarks>
        public static FileTextEditorTheme NordDark { get; }

        /// <summary>
        /// Gets the Nord Light theme for the file text editor.
        /// </summary>
        /// <remarks>Use this property to apply a light variant of the Nord color scheme to the editor.
        /// The theme provides a distinct palette designed for readability and aesthetic consistency in light
        /// environments.</remarks>
        public static FileTextEditorTheme NordLight { get; }

        /// <summary>
        /// Gets the Nord Semi-Dark theme for the file text editor.
        /// </summary>
        /// <remarks>This theme provides a color scheme inspired by the Nord palette with a semi-dark
        /// background, designed for comfortable code editing in low-light environments.</remarks>
        public static FileTextEditorTheme NordSemiDark { get; }

        /// <summary>
        /// Gets the Nord Semi Light theme for the file text editor.
        /// </summary>
        /// <remarks>This theme provides a light color scheme inspired by the Nord palette, offering
        /// enhanced readability and a modern appearance for code editing. Use this theme to apply consistent styling
        /// across supported editors.</remarks>
        public static FileTextEditorTheme NordSemiLight { get; }

        /// <summary>
        /// Gets the user-defined theme settings for the file text editor.
        /// </summary>
        /// <remarks>Use this property to access custom theme preferences configured by the current user.
        /// The returned theme reflects the user's choices and may differ from default or system themes.</remarks>
        public static FileTextEditorTheme User { get; }

        /// <summary>
        /// Gets or sets the style information for glyphs associated with this element.
        /// </summary>
        public required Style Glyph { get; set; }

        /// <summary>
        /// Gets or sets the style applied to the gutter area of the control.
        /// </summary>
        public required Style Gutter { get; set; }

        /// <summary>
        /// Gets or sets the style applied to the header section.
        /// </summary>
        public required Style Header { get; set; }

        /// <summary>
        /// Gets or sets the style applied to the hint bar element.
        /// </summary>
        public required Style HintBar { get; set; }

        /// <summary>
        /// Gets or sets the style applied to the status bar.
        /// </summary>
        public required Style StatusBar { get; set; }

        /// <summary>
        /// Gets or sets the style applied to the tab glyph displayed in the control.
        /// </summary>
        public required Style TabGlyph { get; set; }

        /// <summary>
        /// Gets or sets the style applied to the text editor component.
        /// </summary>
        public required Style TextEditor { get; set; }

        /// <summary>
        /// Returns a header style for the specified file based on its extension and configured color mappings.
        /// </summary>
        /// <remarks>Header color mappings are defined in the application settings as a
        /// semicolon-separated list of extension-to-color pairs. If a mapping exists for the file's extension, the
        /// corresponding colors are applied to the header style.</remarks>
        /// <param name="fileNameAndPath">The full path or name of the file for which to retrieve the header style. The file extension is used to
        /// determine the style.</param>
        /// <returns>A Style object representing the foreground and background colors to use for the file's header. If no
        /// specific color mapping is found for the file extension, the default header style is returned.</returns>
        public Style GetHeaderForFile(string fileNameAndPath)
        {
            var extension = Path.GetExtension(fileNameAndPath)?.TrimStart('.').ToLowerInvariant() ?? string.Empty;
            var headerFgColor = this.Header.Foreground;
            var headerBgColor = this.Header.Background;

            var settings = AppConfig.Settings.BuiltInEditor;
            if (!string.IsNullOrWhiteSpace(settings.HeaderColorMappings))
            {
                var mappings = settings.HeaderColorMappings.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var mapping in mappings)
                {
                    var parts = mapping.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && parts[0].Equals(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        var colors = parts[1].Split(',', StringSplitOptions.TrimEntries);
                        if (colors.Length >= 1)
                        {
                            headerFgColor = ParseHexColor(colors[0], headerFgColor);
                        }

                        if (colors.Length >= 2)
                        {
                            headerBgColor = ParseHexColor(colors[1], headerBgColor);
                        }

                        break;
                    }
                }
            }

            return new Style(headerFgColor, headerBgColor);
        }

        /// <summary>
        /// Parses a hexadecimal color string and returns the corresponding <see cref="Color"/> value. If the input is
        /// invalid, returns the specified default color.
        /// </summary>
        /// <remarks>The method ignores a leading '#' character in the input. Only 6-digit hexadecimal
        /// color codes are supported; alpha components are not parsed.</remarks>
        /// <param name="hex">A string containing a hexadecimal color code in the format "RRGGBB", optionally prefixed with '#'.</param>
        /// <param name="defaultColor">The <see cref="Color"/> value to return if <paramref name="hex"/> is null, empty, or not a valid hexadecimal
        /// color code.</param>
        /// <returns>A <see cref="Color"/> representing the parsed color if <paramref name="hex"/> is valid; otherwise, <paramref
        /// name="defaultColor"/>.</returns>
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
    }
}
