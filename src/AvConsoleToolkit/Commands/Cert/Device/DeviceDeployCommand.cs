// <copyright file="DeviceDeployCommand.cs">
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.CertManager;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert.Device
{
    /// <summary>
    /// Command that deploys device certificates to their configured deployment targets.
    /// Supports deploying a single certificate or all configured targets as a group operation.
    /// Optionally regenerates the certificate before deployment.
    /// </summary>
    public sealed class DeviceDeployCommand : AsyncCommand<DeviceDeploySettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, DeviceDeploySettings settings, CancellationToken cancellationToken)
        {
            var dbPath = settings.ResolveDbPath();
            if (dbPath == null)
            {
                AnsiConsole.MarkupLine("[yellow]No certificate database found. Use --db to specify a path.[/]");
                return 1;
            }

            using var db = new CertDatabase(dbPath);

            List<DeploymentTargetRecord> targets;

            if (settings.All)
            {
                targets = db.ListTargets();
                if (targets.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No deployment targets configured. Use 'cert device target add' to configure targets.[/]");
                    return 1;
                }

                AnsiConsole.MarkupLine($"[cyan]Deploying to {targets.Count} target(s)...[/]");
            }
            else
            {
                var cert = DeviceCertResolver.Resolve(db, settings.NameOrId!);
                if (cert == null)
                {
                    return 1;
                }

                targets = db.ListTargets(cert.Id);
                if (targets.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[yellow]No deployment targets configured for certificate '{cert.Name.EscapeMarkup()}'. Use 'cert device target add' to configure targets.[/]");
                    return 1;
                }

                AnsiConsole.MarkupLine($"[cyan]Deploying certificate '{cert.Name.EscapeMarkup()}' to {targets.Count} target(s)...[/]");
            }

            var succeeded = 0;
            var failed = 0;

            foreach (var target in targets)
            {
                var cert = db.GetCert(target.CertId);
                if (cert == null)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] Certificate ID {target.CertId} not found for target '{target.ConnectionAddress.EscapeMarkup()}'.");
                    failed++;
                    continue;
                }

                // Optionally regenerate the certificate
                if (settings.Regenerate)
                {
                    cert = RegenerateCertificate(db, cert);
                    if (cert == null)
                    {
                        failed++;
                        continue;
                    }
                }

                // Get CA cert for deployments that need it
                var ca = db.GetCa(cert.CaId);
                var caCertPem = ca?.CertificatePem ?? [];

                AnsiConsole.MarkupLine($"[cyan]→ Deploying[/] [bold]{cert.Name.EscapeMarkup()}[/] [cyan]to[/] {target.ConnectionAddress.EscapeMarkup()}:{target.Port} [dim]({target.DeployType})[/]");

                var success = await CertDeployer.DeployAsync(target, cert, caCertPem, cancellationToken);
                if (success)
                {
                    db.UpdateTargetLastDeployed(target.Id, DateTime.UtcNow);
                    succeeded++;
                    AnsiConsole.MarkupLine($"  [green]✓ Success[/]");
                }
                else
                {
                    failed++;
                    AnsiConsole.MarkupLine($"  [red]✗ Failed[/]");
                }
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]Succeeded: {succeeded}[/]  [red]Failed: {failed}[/]  Total: {targets.Count}");

            return failed > 0 ? 1 : 0;
        }

        private static DeviceCertificateRecord? RegenerateCertificate(CertDatabase db, DeviceCertificateRecord cert)
        {
            var ca = db.GetCa(cert.CaId);
            if (ca == null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] CA (ID {cert.CaId}) not found for certificate '{cert.Name.EscapeMarkup()}'. Cannot regenerate.");
                return null;
            }

            AnsiConsole.MarkupLine($"  [dim]Regenerating certificate '{cert.Name.EscapeMarkup()}'...[/]");

            var dnsNames = cert.DnsNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var ips = cert.IpAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var caCert = CertificateGenerator.LoadCaCertificate(ca.CertificatePem, ca.PrivateKeyPem);
            var newCert = CertificateGenerator.CreateDeviceCertificate(caCert, dnsNames, ips, ca.Country, ca.Organization);

            var certPem = CertificateGenerator.ExportCertificatePem(newCert);
            var keyPem = CertificateGenerator.ExportPrivateKeyPem(newCert);
            var pfx = CertificateGenerator.ExportPfx(newCert);
            var fullChain = CertificateGenerator.BuildFullChainPem(certPem, ca.CertificatePem);

            // Delete old cert and insert new one
            db.DeleteCert(cert.Id);

            var record = new DeviceCertificateRecord
            {
                CaId = ca.Id,
                Name = cert.Name,
                DnsNames = cert.DnsNames,
                IpAddresses = cert.IpAddresses,
                CertificatePem = certPem,
                PrivateKeyPem = keyPem,
                Pfx = pfx,
                FullChainPem = fullChain,
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = newCert.NotAfter.ToUniversalTime(),
            };

            var newId = db.InsertCert(record);
            record.Id = newId;

            AnsiConsole.MarkupLine($"  [dim]Regenerated (new ID {newId}, expires {record.ExpiresUtc:yyyy-MM-dd}).[/]");

            return record;
        }
    }
}
