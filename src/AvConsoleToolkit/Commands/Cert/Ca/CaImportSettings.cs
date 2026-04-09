// <copyright file="CaImportSettings.cs">
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
using System.IO;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert.Ca
{
    /// <summary>
    /// Settings for the <c>cert ca import</c> command.
    /// </summary>
    public class CaImportSettings : CertDatabaseSettings
    {
        /// <summary>
        /// Gets or sets the name to assign to the imported Certificate Authority.
        /// </summary>
        [CommandArgument(0, "<NAME>")]
        [Description("Name to assign to the imported Certificate Authority.")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the path to the PEM-encoded CA certificate file.
        /// </summary>
        [CommandOption("--cert <CERT_FILE>")]
        [Description("Path to the PEM-encoded CA certificate file (.crt or .pem).")]
        public string CertFile { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the path to the PEM-encoded CA private key file.
        /// </summary>
        [CommandOption("--key <KEY_FILE>")]
        [Description("Path to the PEM-encoded CA private key file (.key or .pem).")]
        public string KeyFile { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the path to a PFX/PKCS#12 file containing the CA certificate and key.
        /// This is an alternative to specifying --cert and --key separately.
        /// </summary>
        [CommandOption("--pfx <PFX_FILE>")]
        [Description("Path to a PFX/PKCS#12 file containing the CA certificate and key (alternative to --cert and --key).")]
        public string? PfxFile { get; set; }

        /// <summary>
        /// Gets or sets the password for the PFX file.
        /// </summary>
        [CommandOption("--password <PASSWORD>")]
        [Description("Password for the PFX file (if using --pfx).")]
        public string? PfxPassword { get; set; }

        /// <inheritdoc/>
        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return ValidationResult.Error("CA name is required.");
            }

            if (!string.IsNullOrWhiteSpace(PfxFile))
            {
                if (!File.Exists(PfxFile))
                {
                    return ValidationResult.Error($"PFX file not found: {PfxFile}");
                }

                return ValidationResult.Success();
            }

            if (string.IsNullOrWhiteSpace(CertFile))
            {
                return ValidationResult.Error("Certificate file is required. Use --cert or --pfx.");
            }

            if (string.IsNullOrWhiteSpace(KeyFile))
            {
                return ValidationResult.Error("Private key file is required. Use --key or --pfx.");
            }

            if (!File.Exists(CertFile))
            {
                return ValidationResult.Error($"Certificate file not found: {CertFile}");
            }

            if (!File.Exists(KeyFile))
            {
                return ValidationResult.Error($"Private key file not found: {KeyFile}");
            }

            return ValidationResult.Success();
        }
    }
}
