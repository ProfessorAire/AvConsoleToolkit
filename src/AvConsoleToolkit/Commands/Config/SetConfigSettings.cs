// <copyright file="SetConfigSettings.cs">
// The MIT License
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using System.ComponentModel;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Config
{
    /// <summary>
    /// Settings for the <see cref="SetConfigCommand"/>.
    /// </summary>
    public class SetConfigSettings : CommandSettings
    {
        /// <summary>
        /// The config key to set. For example: <c>Connection.AddressBooksLocation</c> or an INI key name when used with <see cref="Section"/>.
        /// </summary>
        [CommandArgument(0, "<key>")]
        [Description("The config key to set")]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// When specified, write to the local config located under the working directory.
        /// Otherwise the config in the current user's AppData folder is used.
        /// </summary>
        [CommandOption("--local|-l")]
        [Description("Write to the local config location in the working directory.")]
        public bool Local { get; set; }

        /// <summary>
        /// Optional INI section name where the key belongs. If omitted the key is written to the global section.
        /// </summary>
        [CommandOption("--section|-s")]
        [Description("The config section the key belongs to (optional)")]
        public string? Section { get; set; }

        /// <summary>
        /// The new value to assign to the specified configuration key.
        /// </summary>
        [CommandArgument(1, "<value>")]
        [Description("The new config value")]
        public string Value { get; set; } = string.Empty;
    }
}