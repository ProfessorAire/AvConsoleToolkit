// <copyright file="DeviceCertificateRecord.cs">
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
    /// Represents a device certificate stored in the database.
    /// </summary>
    public sealed class DeviceCertificateRecord
    {
        /// <summary>
        /// Gets or sets the unique identifier for the certificate.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the ID of the Certificate Authority that signed this certificate.
        /// </summary>
        public int CaId { get; set; }

        /// <summary>
        /// Gets or sets the fully qualified domain name for the certificate.
        /// </summary>
        public string Fqdn { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the comma-separated list of IP addresses included in the SAN.
        /// </summary>
        public string IpAddresses { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the PEM-encoded certificate.
        /// </summary>
        public byte[] CertificatePem { get; set; } = [];

        /// <summary>
        /// Gets or sets the PEM-encoded private key.
        /// </summary>
        public byte[] PrivateKeyPem { get; set; } = [];

        /// <summary>
        /// Gets or sets the PFX/PKCS12 bundle (may be empty if no password was set).
        /// </summary>
        public byte[] Pfx { get; set; } = [];

        /// <summary>
        /// Gets or sets the PEM-encoded full chain (certificate + CA certificate).
        /// </summary>
        public byte[] FullChainPem { get; set; } = [];

        /// <summary>
        /// Gets or sets the date and time the certificate was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// Gets or sets the date and time the certificate expires (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; }
    }
}
