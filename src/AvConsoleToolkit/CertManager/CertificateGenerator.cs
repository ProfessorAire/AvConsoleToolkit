// <copyright file="CertificateGenerator.cs">
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
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AvConsoleToolkit.CertManager
{
    /// <summary>
    /// Generates X.509 certificates using the .NET cryptography APIs.
    /// Follows the same paradigm as the CertManager PowerShell scripts.
    /// </summary>
    internal static class CertificateGenerator
    {
        /// <summary>
        /// Creates a self-signed Root CA certificate.
        /// </summary>
        /// <param name="country">Country code (e.g., "US").</param>
        /// <param name="organization">Organization name.</param>
        /// <param name="caName">The CA name used as part of the CN.</param>
        /// <param name="validityDays">Number of days the CA certificate is valid (default 3650).</param>
        /// <param name="orgUnit">Organizational unit name (optional).</param>
        /// <param name="state">State or province name (optional).</param>
        /// <param name="locality">Locality or city name (optional).</param>
        /// <returns>The generated CA certificate with its private key.</returns>
        public static X509Certificate2 CreateCaCertificate(string country, string organization, string caName, int validityDays = 3650, string? orgUnit = null, string? state = null, string? locality = null)
        {
            using var rsa = RSA.Create(4096);
            var dnParts = $"C={country}, O={organization}";
            if (!string.IsNullOrWhiteSpace(orgUnit))
            {
                dnParts += $", OU={orgUnit}";
            }

            if (!string.IsNullOrWhiteSpace(state))
            {
                dnParts += $", ST={state}";
            }

            if (!string.IsNullOrWhiteSpace(locality))
            {
                dnParts += $", L={locality}";
            }

            dnParts += $", CN={organization} Private Root CA";

            var subject = new X500DistinguishedName(dnParts);

            var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: true,
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: true));

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                    critical: true));

            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

            var notBefore = DateTimeOffset.UtcNow;
            var notAfter = notBefore.AddDays(validityDays);

            var cert = request.CreateSelfSigned(notBefore, notAfter);

            return cert;
        }

        /// <summary>
        /// Creates a device/server certificate signed by a Certificate Authority.
        /// </summary>
        /// <param name="caCert">The CA certificate used to sign the device cert (must contain private key).</param>
        /// <param name="fqdn">The fully qualified domain name for the certificate.</param>
        /// <param name="ipAddresses">Array of IP addresses to include in the SAN.</param>
        /// <param name="country">Country code.</param>
        /// <param name="organization">Organization/unit name.</param>
        /// <param name="validityDays">Number of days the certificate is valid (default 397).</param>
        /// <returns>The generated device certificate with its private key.</returns>
        public static X509Certificate2 CreateDeviceCertificate(
            X509Certificate2 caCert,
            string fqdn,
            string[] ipAddresses,
            string country,
            string organization,
            int validityDays = 397)
        {
            using var rsa = RSA.Create(2048);
            var subject = new X500DistinguishedName($"C={country}, O={organization}, CN={fqdn}");

            var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: false,
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: false));

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    critical: true));

            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

            // Build Subject Alternative Names
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(fqdn);

            foreach (var ip in ipAddresses)
            {
                if (IPAddress.TryParse(ip.Trim(), out var parsedIp))
                {
                    sanBuilder.AddIpAddress(parsedIp);
                }
            }

            request.CertificateExtensions.Add(sanBuilder.Build());

            // Sign with CA
            var serialNumber = new byte[16];
            RandomNumberGenerator.Fill(serialNumber);
            serialNumber[0] &= 0x7F; // Ensure positive serial number

            var notBefore = DateTimeOffset.UtcNow;
            var notAfter = notBefore.AddDays(validityDays);

            using var caPrivateKey = caCert.GetRSAPrivateKey();
            if (caPrivateKey == null)
            {
                throw new InvalidOperationException("CA certificate does not contain a private key.");
            }

            var deviceCert = request.Create(caCert, notBefore, notAfter, serialNumber);

            // Combine the signed cert with the device's private key
            var certWithKey = deviceCert.CopyWithPrivateKey(rsa);

            return certWithKey;
        }

        /// <summary>
        /// Exports a certificate as PEM-encoded text bytes (certificate only, no private key).
        /// </summary>
        /// <param name="cert">The certificate to export.</param>
        /// <returns>PEM-encoded certificate bytes.</returns>
        public static byte[] ExportCertificatePem(X509Certificate2 cert)
        {
            var pem = cert.ExportCertificatePem();
            return System.Text.Encoding.UTF8.GetBytes(pem);
        }

        /// <summary>
        /// Exports the private key from a certificate as PEM-encoded text bytes.
        /// </summary>
        /// <param name="cert">The certificate containing the private key.</param>
        /// <returns>PEM-encoded private key bytes.</returns>
        public static byte[] ExportPrivateKeyPem(X509Certificate2 cert)
        {
            using var rsa = cert.GetRSAPrivateKey();
            if (rsa == null)
            {
                throw new InvalidOperationException("Certificate does not contain an RSA private key.");
            }

            var keyPem = rsa.ExportRSAPrivateKeyPem();
            return System.Text.Encoding.UTF8.GetBytes(keyPem);
        }

        /// <summary>
        /// Exports a certificate as a PFX/PKCS#12 bundle.
        /// </summary>
        /// <param name="cert">The certificate to export.</param>
        /// <param name="password">Optional password for the PFX.</param>
        /// <returns>PFX bytes.</returns>
        public static byte[] ExportPfx(X509Certificate2 cert, string? password = null)
        {
            return cert.Export(X509ContentType.Pfx, password ?? string.Empty);
        }

        /// <summary>
        /// Loads a CA certificate from PEM-encoded certificate and private key bytes.
        /// </summary>
        /// <param name="certPem">PEM-encoded certificate bytes.</param>
        /// <param name="keyPem">PEM-encoded private key bytes.</param>
        /// <returns>The loaded certificate with private key.</returns>
        public static X509Certificate2 LoadCaCertificate(byte[] certPem, byte[] keyPem)
        {
            var certText = System.Text.Encoding.UTF8.GetString(certPem);
            var keyText = System.Text.Encoding.UTF8.GetString(keyPem);

            var cert = X509Certificate2.CreateFromPem(certText, keyText);

            return cert;
        }

        /// <summary>
        /// Builds a full chain PEM by concatenating the device certificate PEM and the CA certificate PEM.
        /// </summary>
        /// <param name="certPem">PEM-encoded device certificate bytes.</param>
        /// <param name="caCertPem">PEM-encoded CA certificate bytes.</param>
        /// <returns>Full chain PEM bytes.</returns>
        public static byte[] BuildFullChainPem(byte[] certPem, byte[] caCertPem)
        {
            var combined = certPem.Concat(caCertPem).ToArray();
            return combined;
        }
    }
}
