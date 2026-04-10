// <copyright file="DeviceDeploySettings.cs">
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

namespace AvConsoleToolkit.Commands.Cert.Device
{
    /// <summary>
    /// Settings for the <c>cert device deploy</c> command.
    /// </summary>
    public class DeviceDeploySettings : CertDatabaseSettings
    {
        /// <summary>
        /// Gets or sets the name or ID of the device certificate to deploy.
        /// If omitted and --all is not set, displays an error.
        /// </summary>
        [CommandArgument(0, "[NAME_OR_ID]")]
        [Description("Name or ID of the device certificate to deploy. Omit and use --all to deploy all.")]
        public string? NameOrId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to deploy all configured targets.
        /// </summary>
        [CommandOption("--all")]
        [Description("Deploy certificates to all configured targets.")]
        public bool All { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to regenerate the certificate before deploying.
        /// </summary>
        [CommandOption("--regenerate")]
        [Description("Regenerate the device certificate before deploying.")]
        public bool Regenerate { get; set; }

        /// <inheritdoc/>
        public override ValidationResult Validate()
        {
            if (!All && string.IsNullOrWhiteSpace(NameOrId))
            {
                return ValidationResult.Error("Specify a certificate name/ID or use --all to deploy all targets.");
            }

            return ValidationResult.Success();
        }
    }
}
