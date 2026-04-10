// <copyright file="TargetRemoveSettings.cs">
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
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert.Device.Target
{
    /// <summary>
    /// Settings for the <c>cert device target remove</c> command.
    /// </summary>
    public class TargetRemoveSettings : CertDatabaseSettings
    {
        /// <summary>
        /// Gets or sets the ID of the deployment target to remove.
        /// </summary>
        [CommandArgument(0, "<TARGET_ID>")]
        [Description("ID of the deployment target to remove.")]
        public int TargetId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to skip the confirmation prompt.
        /// </summary>
        [CommandOption("-y|--yes")]
        [Description("Skip confirmation prompt.")]
        public bool Yes { get; set; }

        /// <inheritdoc/>
        public override ValidationResult Validate()
        {
            if (TargetId <= 0)
            {
                return ValidationResult.Error("Target ID must be a positive number.");
            }

            return ValidationResult.Success();
        }
    }
}
