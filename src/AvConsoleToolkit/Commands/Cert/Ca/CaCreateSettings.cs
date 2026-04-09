// <copyright file="CaCreateSettings.cs">
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
    /// Settings for the <c>cert ca create</c> command.
    /// </summary>
    public class CaCreateSettings : CertDatabaseSettings
    {
        /// <summary>
        /// Gets or sets the name of the Certificate Authority (e.g., "my-ca").
        /// </summary>
        [CommandArgument(0, "<NAME>")]
        [Description("Name for the Certificate Authority (e.g., 'my-ca').")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the country code (e.g., "US").
        /// </summary>
        [CommandOption("-c|--country <COUNTRY>")]
        [Description("Two-letter country code (e.g., 'US').")]
        public string Country { get; set; } = "US";

        /// <summary>
        /// Gets or sets the organization name.
        /// </summary>
        [CommandOption("-o|--org <ORGANIZATION>")]
        [Description("Organization name for the CA certificate.")]
        public string Organization { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the organizational unit name (optional).
        /// </summary>
        [CommandOption("--ou <ORG_UNIT>")]
        [Description("Organizational unit name (optional).")]
        public string? OrgUnit { get; set; }

        /// <summary>
        /// Gets or sets the state or province name (optional).
        /// </summary>
        [CommandOption("-s|--state <STATE>")]
        [Description("State or province name (optional).")]
        public string? State { get; set; }

        /// <summary>
        /// Gets or sets the locality/city name (optional).
        /// </summary>
        [CommandOption("-l|--locality <LOCALITY>")]
        [Description("Locality or city name (optional).")]
        public string? Locality { get; set; }

        /// <summary>
        /// Gets or sets the number of days the CA certificate is valid.
        /// </summary>
        [CommandOption("--days <DAYS>")]
        [Description("Number of days the CA certificate is valid (default 3650).")]
        [DefaultValue(3650)]
        public int ValidityDays { get; set; } = 3650;

        /// <inheritdoc/>
        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return ValidationResult.Error("CA name is required.");
            }

            if (string.IsNullOrWhiteSpace(Organization))
            {
                return ValidationResult.Error("Organization is required. Use -o or --org to specify.");
            }

            if (ValidityDays <= 0)
            {
                return ValidationResult.Error("Validity days must be greater than 0.");
            }

            return ValidationResult.Success();
        }
    }
}
