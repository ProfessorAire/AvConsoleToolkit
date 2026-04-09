// <copyright file="DeviceListCommand.cs">
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
    /// Command that lists all device certificates stored in the database, including expiration status.
    /// </summary>
    public sealed class DeviceListCommand : AsyncCommand<DeviceListSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, DeviceListSettings settings, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;

            var dbPath = CertDatabase.ResolveDatabasePath(settings.DatabasePath);
            if (dbPath == null)
            {
                AnsiConsole.MarkupLine("[yellow]No certificate database found. Use --db to specify a path.[/]");
                return 1;
            }

            using var db = new CertDatabase(dbPath);

            int? caId = null;
            if (!string.IsNullOrWhiteSpace(settings.CaName))
            {
                var ca = db.GetCaByName(settings.CaName);
                if (ca == null)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] Certificate Authority '{settings.CaName.EscapeMarkup()}' not found.");
                    return 1;
                }

                caId = ca.Id;
            }

            var certs = db.ListCerts(caId);

            if (certs.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No device certificates found.[/]");
                return 0;
            }

            // Build a CA name lookup
            var cas = db.ListCas().ToDictionary(c => c.Id, c => c.Name);

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[cyan]ID[/]")
                .AddColumn("[cyan]Name[/]")
                .AddColumn("[cyan]DNS Names[/]")
                .AddColumn("[cyan]IP Addresses[/]")
                .AddColumn("[cyan]CA[/]")
                .AddColumn("[cyan]Created[/]")
                .AddColumn("[cyan]Expires[/]")
                .AddColumn("[cyan]Status[/]");

            foreach (var cert in certs)
            {
                var remaining = cert.ExpiresUtc - DateTime.UtcNow;
                var status = remaining.TotalDays <= 0
                    ? "[red]Expired[/]"
                    : remaining.TotalDays <= 30
                        ? $"[yellow]Expires in {(int)remaining.TotalDays} days[/]"
                        : $"[green]Valid ({(int)remaining.TotalDays} days)[/]";

                var caName = cas.TryGetValue(cert.CaId, out var name) ? name : "Unknown";

                table.AddRow(
                    cert.Id.ToString(),
                    cert.Name.EscapeMarkup(),
                    cert.DnsNames.EscapeMarkup(),
                    cert.IpAddresses.EscapeMarkup(),
                    caName.EscapeMarkup(),
                    cert.CreatedUtc.ToString("yyyy-MM-dd"),
                    cert.ExpiresUtc.ToString("yyyy-MM-dd"),
                    status);
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
