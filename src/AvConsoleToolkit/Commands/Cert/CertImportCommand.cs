// <copyright file="CertImportCommand.cs">
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
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.CertManager;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert
{
    /// <summary>
    /// Command that imports an existing device certificate into the database.
    /// Supports importing from PEM certificate + key files, or from a PFX/PKCS#12 bundle.
    /// </summary>
    public sealed class CertImportCommand : AsyncCommand<CertImportSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, CertImportSettings settings, CancellationToken cancellationToken)
        {
            var dbPath = CertDatabase.ResolveDatabasePath(settings.DatabasePath)
                ?? Path.Combine(Environment.CurrentDirectory, CertDatabase.DefaultFileName);

            using var db = new CertDatabase(dbPath);

            var ca = db.GetCaByName(settings.CaName);
            if (ca == null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Certificate Authority '{settings.CaName.EscapeMarkup()}' not found. Import or create the CA first.");
                return 1;
            }

            X509Certificate2 cert;

            if (!string.IsNullOrWhiteSpace(settings.PfxFile))
            {
                // Import from PFX
                var pfxBytes = await File.ReadAllBytesAsync(settings.PfxFile, cancellationToken);
                cert = X509CertificateLoader.LoadPkcs12(pfxBytes, settings.PfxPassword, X509KeyStorageFlags.Exportable);
            }
            else if (!string.IsNullOrWhiteSpace(settings.KeyFile))
            {
                // Import from PEM cert + key files
                var certText = await File.ReadAllTextAsync(settings.CertFile!, cancellationToken);
                var keyText = await File.ReadAllTextAsync(settings.KeyFile, cancellationToken);
                cert = X509Certificate2.CreateFromPem(certText, keyText);
            }
            else
            {
                // Import certificate only (no private key)
                var certText = await File.ReadAllTextAsync(settings.CertFile!, cancellationToken);
                cert = X509Certificate2.CreateFromPem(certText);
            }

            using (cert)
            {
                // Extract FQDN from the CN
                var fqdn = ExtractDnField(cert.Subject, "CN") ?? "unknown";

                // Extract IPs from SAN if available
                var ipAddresses = string.Empty;
                var sanExt = cert.Extensions["2.5.29.17"];
                if (sanExt != null)
                {
                    var sanFormatted = sanExt.Format(true);
                    var ips = sanFormatted
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Where(l => l.Contains("IP Address", StringComparison.OrdinalIgnoreCase))
                        .Select(l => l.Split('=', ':').LastOrDefault()?.Trim())
                        .Where(ip => !string.IsNullOrEmpty(ip));
                    ipAddresses = string.Join(",", ips);
                }

                var certPem = CertificateGenerator.ExportCertificatePem(cert);
                var keyPem = cert.HasPrivateKey ? CertificateGenerator.ExportPrivateKeyPem(cert) : [];
                var pfx = cert.HasPrivateKey ? CertificateGenerator.ExportPfx(cert) : [];
                var fullChain = CertificateGenerator.BuildFullChainPem(certPem, ca.CertificatePem);

                var record = new DeviceCertificateRecord
                {
                    CaId = ca.Id,
                    Fqdn = fqdn,
                    IpAddresses = ipAddresses,
                    CertificatePem = certPem,
                    PrivateKeyPem = keyPem,
                    Pfx = pfx,
                    FullChainPem = fullChain,
                    CreatedUtc = cert.NotBefore.ToUniversalTime(),
                    ExpiresUtc = cert.NotAfter.ToUniversalTime(),
                };

                var id = db.InsertCert(record);

                AnsiConsole.MarkupLine($"[green]Device certificate for '{fqdn.EscapeMarkup()}' imported successfully (ID {id}).[/]");
                AnsiConsole.MarkupLine($"  CA: {ca.Name.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"  Expires: {record.ExpiresUtc:yyyy-MM-dd}");
                if (cert.HasPrivateKey)
                {
                    AnsiConsole.MarkupLine("  Private key: [green]included[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("  Private key: [yellow]not included[/] (export will not produce key/PFX files)");
                }

                AnsiConsole.MarkupLine($"  Database: {dbPath.EscapeMarkup()}");
            }

            return 0;
        }

        private static string? ExtractDnField(string distinguishedName, string field)
        {
            var prefix = $"{field}=";
            foreach (var part in distinguishedName.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed[prefix.Length..].Trim();
                }
            }

            return null;
        }
    }
}
