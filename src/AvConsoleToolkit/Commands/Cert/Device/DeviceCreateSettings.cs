// <copyright file="DeviceCreateSettings.cs">
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
    /// Settings for the <c>cert device create</c> command.
    /// </summary>
    public class DeviceCreateSettings : CertDatabaseSettings
    {
        /// <summary>
        /// Gets or sets the FQDN for the device certificate.
        /// </summary>
        [CommandArgument(0, "<FQDN>")]
        [Description("Fully qualified domain name or hostname for the certificate (e.g., 'server.example.com').")]
        public string Fqdn { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the Certificate Authority to sign with.
        /// </summary>
        [CommandOption("--ca <CA_NAME>")]
        [Description("Name of the Certificate Authority to use for signing.")]
        public string CaName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the comma-separated IP addresses to include in the SAN.
        /// </summary>
        [CommandOption("-i|--ip <IP_ADDRESSES>")]
        [Description("Comma-separated IP addresses to include in the certificate SAN (e.g., '192.168.1.1,10.0.0.1').")]
        public string? IpAddresses { get; set; }

        /// <summary>
        /// Gets or sets the password for the PFX export.
        /// </summary>
        [CommandOption("--password <PASSWORD>")]
        [Description("Password for the PFX certificate bundle.")]
        public string? PfxPassword { get; set; }

        /// <summary>
        /// Gets or sets the number of days the certificate is valid.
        /// </summary>
        [CommandOption("--days <DAYS>")]
        [Description("Number of days the certificate is valid (default 397).")]
        [DefaultValue(397)]
        public int ValidityDays { get; set; } = 397;

        /// <inheritdoc/>
        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Fqdn))
            {
                return ValidationResult.Error("FQDN is required.");
            }

            if (string.IsNullOrWhiteSpace(CaName))
            {
                return ValidationResult.Error("CA name is required. Use --ca to specify.");
            }

            if (ValidityDays <= 0)
            {
                return ValidationResult.Error("Validity days must be greater than 0.");
            }

            return ValidationResult.Success();
        }
    }
}
