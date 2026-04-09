// <copyright file="CaExportCommand.cs">
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
    /// Command that exports a root CA certificate from the database to files on disk.
    /// Can export the certificate PEM, private key PEM, and/or a PFX bundle.
    /// </summary>
    public sealed class CaExportCommand : AsyncCommand<CaExportSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, CaExportSettings settings, CancellationToken cancellationToken)
        {
            var dbPath = CertDatabase.ResolveDatabasePath(settings.DatabasePath);
            if (dbPath == null)
            {
                AnsiConsole.MarkupLine("[yellow]No certificate database found. Use --db to specify a path.[/]");
                return 1;
            }

            using var db = new CertDatabase(dbPath);
            var ca = db.GetCaByName(settings.Name);

            if (ca == null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Certificate Authority '{settings.Name.EscapeMarkup()}' not found.");
                return 1;
            }

            var outputDir = settings.OutputDirectory ?? Path.Combine(Environment.CurrentDirectory, ca.Name);
            Directory.CreateDirectory(outputDir);

            if (settings.PfxOnly)
            {
                // Export as PFX only
                var caCert = CertificateGenerator.LoadCaCertificate(ca.CertificatePem, ca.PrivateKeyPem);
                var pfxBytes = CertificateGenerator.ExportPfx(caCert, settings.PfxPassword);
                var pfxPath = Path.Combine(outputDir, $"{ca.Name}.pfx");
                await File.WriteAllBytesAsync(pfxPath, pfxBytes, cancellationToken);
                AnsiConsole.MarkupLine($"[green]Exported PFX:[/] {pfxPath.EscapeMarkup()}");
            }
            else if (settings.CertOnly)
            {
                // Export certificate PEM only (no private key)
                var certPath = Path.Combine(outputDir, $"{ca.Name}.crt");
                await File.WriteAllBytesAsync(certPath, ca.CertificatePem, cancellationToken);
                AnsiConsole.MarkupLine($"[green]Exported CA certificate:[/] {certPath.EscapeMarkup()}");
            }
            else
            {
                // Export all files
                var certPath = Path.Combine(outputDir, $"{ca.Name}.crt");
                var keyPath = Path.Combine(outputDir, $"{ca.Name}.key");

                await File.WriteAllBytesAsync(certPath, ca.CertificatePem, cancellationToken);
                await File.WriteAllBytesAsync(keyPath, ca.PrivateKeyPem, cancellationToken);

                // Also generate and export PFX
                var caCert = CertificateGenerator.LoadCaCertificate(ca.CertificatePem, ca.PrivateKeyPem);
                var pfxBytes = CertificateGenerator.ExportPfx(caCert, settings.PfxPassword);
                var pfxPath = Path.Combine(outputDir, $"{ca.Name}.pfx");
                await File.WriteAllBytesAsync(pfxPath, pfxBytes, cancellationToken);

                AnsiConsole.MarkupLine($"[green]Exported CA certificate files to:[/] {outputDir.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"  Certificate: {certPath.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"  Private Key: {keyPath.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"  PFX Bundle:  {pfxPath.EscapeMarkup()}");
            }

            return 0;
        }
    }
}
