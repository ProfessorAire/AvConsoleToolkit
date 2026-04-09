// <copyright file="DeviceCreateCommand.cs">
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
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.CertManager;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert.Device
{
    /// <summary>
    /// Command that creates a new device certificate signed by an existing Certificate Authority.
    /// The certificate is stored in the database and not written to disk unless explicitly exported.
    /// </summary>
    public sealed class DeviceCreateCommand : AsyncCommand<DeviceCreateSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, DeviceCreateSettings settings, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;

            var dbPath = settings.ResolveDbPath()
                ?? Path.Combine(Environment.CurrentDirectory, CertDatabase.DefaultFileName);

            using var db = new CertDatabase(dbPath);

            var ca = db.GetCaByName(settings.CaName);
            if (ca == null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Certificate Authority '{settings.CaName.EscapeMarkup()}' not found. Create one first with 'cert ca create'.");
                return 1;
            }

            var dnsNames = settings.DnsNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var ips = settings.IpAddresses?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

            AnsiConsole.MarkupLine($"[cyan]Creating device certificate '{settings.Name.EscapeMarkup()}'[/]");
            AnsiConsole.MarkupLine($"  DNS: {string.Join(", ", dnsNames).EscapeMarkup()}");
            if (ips.Length > 0)
            {
                AnsiConsole.MarkupLine($"  IPs: {string.Join(", ", ips).EscapeMarkup()}");
            }

            // Load CA certificate from database
            var caCert = CertificateGenerator.LoadCaCertificate(ca.CertificatePem, ca.PrivateKeyPem);

            var deviceCert = CertificateGenerator.CreateDeviceCertificate(
                caCert,
                dnsNames,
                ips,
                ca.Country,
                ca.Organization,
                settings.ValidityDays);

            var certPem = CertificateGenerator.ExportCertificatePem(deviceCert);
            var keyPem = CertificateGenerator.ExportPrivateKeyPem(deviceCert);
            var pfx = CertificateGenerator.ExportPfx(deviceCert, settings.PfxPassword);
            var fullChain = CertificateGenerator.BuildFullChainPem(certPem, ca.CertificatePem);

            var record = new DeviceCertificateRecord
            {
                CaId = ca.Id,
                Name = settings.Name,
                DnsNames = string.Join(",", dnsNames),
                IpAddresses = string.Join(",", ips),
                CertificatePem = certPem,
                PrivateKeyPem = keyPem,
                Pfx = pfx,
                FullChainPem = fullChain,
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = deviceCert.NotAfter.ToUniversalTime(),
            };

            var id = db.InsertCert(record);

            AnsiConsole.MarkupLine($"[green]Device certificate '{settings.Name.EscapeMarkup()}' created successfully (ID {id}).[/]");
            AnsiConsole.MarkupLine($"  Signed by: {ca.Name.EscapeMarkup()}");
            AnsiConsole.MarkupLine($"  Expires: {record.ExpiresUtc:yyyy-MM-dd}");
            AnsiConsole.MarkupLine($"  Database: {dbPath.EscapeMarkup()}");

            return 0;
        }
    }
}
