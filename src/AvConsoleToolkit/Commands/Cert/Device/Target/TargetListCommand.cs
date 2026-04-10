// <copyright file="TargetListCommand.cs">
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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.CertManager;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert.Device.Target
{
    /// <summary>
    /// Command that lists all deployment targets configured for device certificates.
    /// </summary>
    public sealed class TargetListCommand : AsyncCommand<CertDatabaseSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, CertDatabaseSettings settings, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;

            var dbPath = settings.ResolveDbPath();
            if (dbPath == null)
            {
                AnsiConsole.MarkupLine("[yellow]No certificate database found. Use --db to specify a path.[/]");
                return 1;
            }

            using var db = new CertDatabase(dbPath);
            var targets = db.ListTargets();

            if (targets.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No deployment targets configured.[/]");
                return 0;
            }

            // Build cert name lookup
            var certs = db.ListCerts().ToDictionary(c => c.Id, c => c.Name);

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[cyan]ID[/]")
                .AddColumn("[cyan]Certificate[/]")
                .AddColumn("[cyan]Host[/]")
                .AddColumn("[cyan]Type[/]")
                .AddColumn("[cyan]User[/]")
                .AddColumn("[cyan]Last Deployed[/]");

            foreach (var target in targets)
            {
                var certName = certs.TryGetValue(target.CertId, out var name) ? name : $"ID {target.CertId}";
                var lastDeployed = target.LastDeployedUtc.HasValue
                    ? target.LastDeployedUtc.Value.ToString("yyyy-MM-dd HH:mm")
                    : "[dim]Never[/]";

                table.AddRow(
                    target.Id.ToString(),
                    certName.EscapeMarkup(),
                    $"{target.ConnectionAddress.EscapeMarkup()}:{target.Port}",
                    target.DeployType.ToString(),
                    target.Username.EscapeMarkup(),
                    lastDeployed);
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
