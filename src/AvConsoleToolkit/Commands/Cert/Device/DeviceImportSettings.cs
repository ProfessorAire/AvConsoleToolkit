// <copyright file="DeviceImportSettings.cs">
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

namespace AvConsoleToolkit.Commands.Cert.Device
{
    /// <summary>
    /// Settings for the <c>cert device import</c> command.
    /// </summary>
    public class DeviceImportSettings : CertDatabaseSettings
    {
        /// <summary>
        /// Gets or sets the friendly name for the imported certificate.
        /// If not specified, the CN from the certificate subject is used.
        /// </summary>
        [CommandOption("--name <NAME>")]
        [Description("Friendly name for the certificate. Defaults to the CN from the certificate subject.")]
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the name of the Certificate Authority that signed this certificate.
        /// </summary>
        [CommandOption("--ca <CA_NAME>")]
        [Description("Name of the Certificate Authority that signed this certificate.")]
        public string CaName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the path to the PEM-encoded certificate file.
        /// </summary>
        [CommandOption("--cert <CERT_FILE>")]
        [Description("Path to the PEM-encoded certificate file (.crt or .pem).")]
        public string? CertFile { get; set; }

        /// <summary>
        /// Gets or sets the path to the PEM-encoded private key file.
        /// </summary>
        [CommandOption("--key <KEY_FILE>")]
        [Description("Path to the PEM-encoded private key file (.key or .pem).")]
        public string? KeyFile { get; set; }

        /// <summary>
        /// Gets or sets the path to a PFX/PKCS#12 file.
        /// This is an alternative to specifying --cert and --key separately.
        /// </summary>
        [CommandOption("--pfx <PFX_FILE>")]
        [Description("Path to a PFX/PKCS#12 file (alternative to --cert and --key).")]
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
            if (string.IsNullOrWhiteSpace(CaName))
            {
                return ValidationResult.Error("CA name is required. Use --ca to specify which CA signed this certificate.");
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

            if (!File.Exists(CertFile))
            {
                return ValidationResult.Error($"Certificate file not found: {CertFile}");
            }

            if (!string.IsNullOrWhiteSpace(KeyFile) && !File.Exists(KeyFile))
            {
                return ValidationResult.Error($"Private key file not found: {KeyFile}");
            }

            return ValidationResult.Success();
        }
    }
}
