// <copyright file="TargetAddCommand.cs">
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

namespace AvConsoleToolkit.Commands.Cert.Device.Target
{
    /// <summary>
    /// Command that adds a deployment target for a device certificate.
    /// Associates a certificate with a remote device and deployment method.
    /// </summary>
    public sealed class TargetAddCommand : AsyncCommand<TargetAddSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, TargetAddSettings settings, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;

            var dbPath = settings.ResolveDbPath()
                ?? Path.Combine(Environment.CurrentDirectory, CertDatabase.DefaultFileName);

            using var db = new CertDatabase(dbPath);

            var cert = DeviceCertResolver.Resolve(db, settings.CertNameOrId);
            if (cert == null)
            {
                return 1;
            }

            var connectionAddress = settings.ConnectionAddress;
            if (string.IsNullOrWhiteSpace(connectionAddress))
            {
                // Default to first DNS name from cert
                var dnsNames = cert.DnsNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                connectionAddress = dnsNames.Length > 0 ? dnsNames[0] : cert.IpAddresses.Split(',').FirstOrDefault() ?? "localhost";
            }

            var record = new DeploymentTargetRecord
            {
                CertId = cert.Id,
                ConnectionAddress = connectionAddress,
                Port = settings.Port,
                Username = settings.Username ?? string.Empty,
                Password = settings.Password ?? string.Empty,
                DeployType = settings.DeployType,
                SshKeyPath = settings.SshKeyPath ?? string.Empty,
                ApiKey = settings.ApiKey ?? string.Empty,
            };

            var id = db.InsertTarget(record);

            AnsiConsole.MarkupLine($"[green]Deployment target added (ID {id}).[/]");
            AnsiConsole.MarkupLine($"  Certificate: {cert.Name.EscapeMarkup()} (ID {cert.Id})");
            AnsiConsole.MarkupLine($"  Host: {connectionAddress.EscapeMarkup()}:{settings.Port}");
            AnsiConsole.MarkupLine($"  Type: {settings.DeployType}");
            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                AnsiConsole.MarkupLine($"  User: {settings.Username.EscapeMarkup()}");
            }

            return 0;
        }
    }
}
