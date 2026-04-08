// <copyright file="CertInstallRootSettings.cs">
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

namespace AvConsoleToolkit.Commands.Cert
{
    /// <summary>
    /// Settings for the <c>cert install-root</c> command.
    /// </summary>
    public class CertInstallRootSettings : CertDatabaseSettings
    {
        /// <summary>
        /// Gets or sets the name of the Certificate Authority to install.
        /// </summary>
        [CommandArgument(0, "<CA_NAME>")]
        [Description("Name of the Certificate Authority to install on the local machine.")]
        public string CaName { get; set; } = string.Empty;

        /// <inheritdoc/>
        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(CaName))
            {
                return ValidationResult.Error("CA name is required.");
            }

            return ValidationResult.Success();
        }
    }
}
