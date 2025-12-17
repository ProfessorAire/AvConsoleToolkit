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
        /// Gets the settings for the built-in text editor.
        /// </summary>
        IBuiltInEditorSettings BuiltIn { get; }

        /// <summary>
        /// Gets the editor mappings by file extension as a nested settings section.
        /// Each property in this section maps a file extension (without the dot) to an editor path.
        /// Example: Set "json" to "code" to open .json files with VS Code.
        /// If no mapping is found for a file type, the built-in editor will be used.
        /// </summary>
        IEditorMappings Mappings { get; }
    }

    /// <summary>
    /// Defines editor mappings by file extension.
    /// This interface allows dynamic property access for file extension to editor mappings.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public interface IEditorMappings
    {
        /// <summary>
        /// Gets or sets the indexer for accessing editor mappings by extension.
        /// </summary>
        /// <param name="extension">The file extension without the leading dot (e.g., "json", "xml").</param>
        /// <returns>The editor path for the given extension, or null if not mapped.</returns>
        string? this[string extension] { get; set; }
    }
}
