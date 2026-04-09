// <copyright file="CaExportSettings.cs">
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

namespace AvConsoleToolkit.Commands.Cert.Ca
{
    /// <summary>
    /// Settings for the <c>cert ca export</c> command.
    /// </summary>
    public class CaExportSettings : CertDatabaseSettings
    {
        /// <summary>
        /// Gets or sets the name of the Certificate Authority to export.
        /// </summary>
        [CommandArgument(0, "<NAME>")]
        [Description("Name of the Certificate Authority to export.")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the output directory for the exported files.
        /// </summary>
        [CommandOption("-o|--output <DIRECTORY>")]
        [Description("Output directory for exported certificate files (defaults to a folder named after the CA in the working directory).")]
        public string? OutputDirectory { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to export only the PFX bundle.
        /// </summary>
        [CommandOption("--pfx-only")]
        [Description("Export only the PFX bundle.")]
        [DefaultValue(false)]
        public bool PfxOnly { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to export only the certificate PEM (no private key).
        /// </summary>
        [CommandOption("--cert-only")]
        [Description("Export only the certificate PEM (no private key). Useful for distributing to clients.")]
        [DefaultValue(false)]
        public bool CertOnly { get; set; }

        /// <summary>
        /// Gets or sets the password for the PFX export.
        /// </summary>
        [CommandOption("--password <PASSWORD>")]
        [Description("Password for the exported PFX bundle.")]
        public string? PfxPassword { get; set; }

        /// <inheritdoc/>
        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return ValidationResult.Error("CA name is required.");
            }

            if (PfxOnly && CertOnly)
            {
                return ValidationResult.Error("Cannot specify both --pfx-only and --cert-only.");
            }

            return ValidationResult.Success();
        }
    }
}
