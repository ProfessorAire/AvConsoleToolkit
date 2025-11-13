// <copyright file="UpdateSettings.cs">
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

namespace ConsoleToolkit.Commands
{
    /// <summary>
    /// Settings for the update command, controlling confirmation and verbosity.
    /// </summary>
    public class UpdateSettings : CommandSettings
    {
        /// <summary>
        /// Automatically confirm all prompts during update.
        /// </summary>
        [CommandOption("-y|--yes")]
        [Description("Automatically confirm all prompts")]
        public bool AutoConfirm { get; set; }

        /// <summary>
        /// Show detailed error information if an error occurs.
        /// </summary>
        [CommandOption("-v|--verbose")]
        [Description("Show detailed error information")]
        public bool Verbose { get; set; }
    }
}
