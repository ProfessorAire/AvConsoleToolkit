// <copyright file="TargetAddSettings.cs">
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
using AvConsoleToolkit.CertManager;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert.Device.Target
{
    /// <summary>
    /// Settings for the <c>cert device target add</c> command.
    /// </summary>
    public class TargetAddSettings : CertDatabaseSettings
    {
        /// <summary>
        /// Gets or sets the name or ID of the device certificate to associate with.
        /// </summary>
        [CommandArgument(0, "<CERT_NAME_OR_ID>")]
        [Description("Name or ID of the device certificate to associate with this deployment target.")]
        public string CertNameOrId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the connection address for deployment.
        /// </summary>
        [CommandOption("--host <HOST>")]
        [Description("Address to connect to for deployment (IP or hostname). Defaults to the first DNS name from the certificate.")]
        public string? ConnectionAddress { get; set; }

        /// <summary>
        /// Gets or sets the port for deployment connections.
        /// </summary>
        [CommandOption("--port <PORT>")]
        [Description("Port for deployment connection (default 22).")]
        [DefaultValue(22)]
        public int Port { get; set; } = 22;

        /// <summary>
        /// Gets or sets the username for authentication.
        /// </summary>
        [CommandOption("-u|--user <USERNAME>")]
        [Description("Username for authentication during deployment.")]
        public string? Username { get; set; }

        /// <summary>
        /// Gets or sets the password for authentication.
        /// </summary>
        [CommandOption("-p|--password <PASSWORD>")]
        [Description("Password for authentication during deployment.")]
        public string? Password { get; set; }

        /// <summary>
        /// Gets or sets the deployment type.
        /// </summary>
        [CommandOption("-t|--type <DEPLOY_TYPE>")]
        [Description("Deployment type: Crestron3, Crestron4, CrestronTP60Series, CrestronTP70Series, TrueNas, UniFi, or Scp.")]
        [DefaultValue(DeployType.Scp)]
        public DeployType DeployType { get; set; } = DeployType.Scp;

        /// <summary>
        /// Gets or sets the path to an SSH private key file.
        /// </summary>
        [CommandOption("--ssh-key <PATH>")]
        [Description("Path to SSH private key file for key-based authentication.")]
        public string? SshKeyPath { get; set; }

        /// <summary>
        /// Gets or sets the API key for API-based deployments.
        /// </summary>
        [CommandOption("--api-key <KEY>")]
        [Description("API key for API-based deployments (e.g., TrueNAS).")]
        public string? ApiKey { get; set; }

        /// <inheritdoc/>
        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(CertNameOrId))
            {
                return ValidationResult.Error("Certificate name or ID is required.");
            }

            return ValidationResult.Success();
        }
    }
}
