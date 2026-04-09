// <copyright file="CertificateAuthorityRecord.cs">
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
    /// Represents a Certificate Authority stored in the database.
    /// </summary>
    public sealed class CertificateAuthorityRecord
    {
        /// <summary>
        /// Gets or sets the unique identifier for the CA.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the name of the CA (e.g., "my-ca").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the country code (e.g., "US").
        /// </summary>
        public string Country { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the organization/unit name.
        /// </summary>
        public string Organization { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the PEM-encoded CA certificate.
        /// </summary>
        public byte[] CertificatePem { get; set; } = [];

        /// <summary>
        /// Gets or sets the PEM-encoded CA private key.
        /// </summary>
        public byte[] PrivateKeyPem { get; set; } = [];

        /// <summary>
        /// Gets or sets the date and time the CA was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// Gets or sets the date and time the CA certificate expires (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; }
    }
}
