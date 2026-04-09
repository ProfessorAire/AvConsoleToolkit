// <copyright file="CaImportCommand.cs">
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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.CertManager;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert.Ca
{
    /// <summary>
    /// Command that imports an existing root CA certificate into the database.
    /// Supports importing from PEM certificate + key files, or from a PFX/PKCS#12 bundle.
    /// </summary>
    public sealed class CaImportCommand : AsyncCommand<CaImportSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, CaImportSettings settings, CancellationToken cancellationToken)
        {
            var dbPath = settings.ResolveDbPath()
                ?? Path.Combine(Environment.CurrentDirectory, CertDatabase.DefaultFileName);

            using var db = new CertDatabase(dbPath);

            // Check if CA already exists
            var existing = db.GetCaByName(settings.Name);
            if (existing != null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] A Certificate Authority named '{settings.Name.EscapeMarkup()}' already exists (ID {existing.Id}).");
                return 1;
            }

            X509Certificate2 cert;

            if (!string.IsNullOrWhiteSpace(settings.PfxFile))
            {
                // Import from PFX
                var pfxBytes = await File.ReadAllBytesAsync(settings.PfxFile, cancellationToken);
                cert = X509CertificateLoader.LoadPkcs12(pfxBytes, settings.PfxPassword, X509KeyStorageFlags.Exportable);
            }
            else
            {
                // Import from PEM cert + key files
                var certText = await File.ReadAllTextAsync(settings.CertFile, cancellationToken);
                var keyText = await File.ReadAllTextAsync(settings.KeyFile, cancellationToken);
                cert = X509Certificate2.CreateFromPem(certText, keyText);
            }

            using (cert)
            {
                if (!cert.HasPrivateKey)
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] The imported certificate does not contain a private key. A CA certificate requires a private key for signing.");
                    return 1;
                }

                // Extract subject info
                var country = ExtractDnField(cert.Subject, "C") ?? "US";
                var organization = ExtractDnField(cert.Subject, "O") ?? settings.Name;

                var certPem = CertificateGenerator.ExportCertificatePem(cert);
                var keyPem = CertificateGenerator.ExportPrivateKeyPem(cert);

                var record = new CertificateAuthorityRecord
                {
                    Name = settings.Name,
                    Country = country,
                    Organization = organization,
                    CertificatePem = certPem,
                    PrivateKeyPem = keyPem,
                    CreatedUtc = cert.NotBefore.ToUniversalTime(),
                    ExpiresUtc = cert.NotAfter.ToUniversalTime(),
                };

                var id = db.InsertCa(record);

                AnsiConsole.MarkupLine($"[green]Root CA '{settings.Name.EscapeMarkup()}' imported successfully (ID {id}).[/]");
                AnsiConsole.MarkupLine($"  Subject: {cert.Subject.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"  Expires: {record.ExpiresUtc:yyyy-MM-dd}");
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
