// <copyright file="DeploymentTargetRecord.cs">
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

using System;

namespace AvConsoleToolkit.CertManager
{
    /// <summary>
    /// Represents a deployment target configuration stored in the database.
    /// Associates a device certificate with a remote device and deployment method.
    /// </summary>
    public sealed class DeploymentTargetRecord
    {
        /// <summary>
        /// Gets or sets the unique identifier for the deployment target.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the ID of the associated device certificate.
        /// </summary>
        public int CertId { get; set; }

        /// <summary>
        /// Gets or sets the address used to connect to the device for deployment.
        /// This can be an IP address or hostname. If empty, the first DNS name from the certificate is used.
        /// </summary>
        public string ConnectionAddress { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the port used for deployment connections (default varies by deploy type).
        /// </summary>
        public int Port { get; set; } = 22;

        /// <summary>
        /// Gets or sets the username for authentication during deployment.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the password for authentication during deployment.
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the deployment type/method.
        /// </summary>
        public DeployType DeployType { get; set; } = DeployType.Scp;

        /// <summary>
        /// Gets or sets the path to an SSH private key file for key-based authentication.
        /// </summary>
        public string SshKeyPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the API key for API-based deployments (e.g., TrueNAS).
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the JSON-encoded file mappings for SCP deployments.
        /// Maps cert file types (RootCA, PFX, PEM, CRT, KEY) to remote paths.
        /// </summary>
        public string FileMappings { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the date and time of the last successful deployment (UTC).
        /// </summary>
        public DateTime? LastDeployedUtc { get; set; }
    }
}
