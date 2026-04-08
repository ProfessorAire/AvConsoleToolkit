// <copyright file="CaListCommand.cs">
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
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.CertManager;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert.Ca
{
    /// <summary>
    /// Command that lists all Certificate Authorities stored in the database.
    /// </summary>
    public sealed class CaListCommand : AsyncCommand<CertDatabaseSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, CertDatabaseSettings settings, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;

            var dbPath = CertDatabase.ResolveDatabasePath(settings.DatabasePath);
            if (dbPath == null)
            {
                AnsiConsole.MarkupLine("[yellow]No certificate database found. Use --db to specify a path, or create a CA first.[/]");
                return 1;
            }

            using var db = new CertDatabase(dbPath);
            var cas = db.ListCas();

            if (cas.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No Certificate Authorities found in the database.[/]");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[cyan]ID[/]")
                .AddColumn("[cyan]Name[/]")
                .AddColumn("[cyan]Organization[/]")
                .AddColumn("[cyan]Country[/]")
                .AddColumn("[cyan]Created[/]")
                .AddColumn("[cyan]Expires[/]")
                .AddColumn("[cyan]Status[/]");

            foreach (var ca in cas)
            {
                var remaining = ca.ExpiresUtc - DateTime.UtcNow;
                var status = remaining.TotalDays <= 0
                    ? "[red]Expired[/]"
                    : remaining.TotalDays <= 30
                        ? $"[yellow]Expires in {(int)remaining.TotalDays} days[/]"
                        : $"[green]Valid ({(int)remaining.TotalDays} days)[/]";

                table.AddRow(
                    ca.Id.ToString(),
                    ca.Name.EscapeMarkup(),
                    ca.Organization.EscapeMarkup(),
                    ca.Country.EscapeMarkup(),
                    ca.CreatedUtc.ToString("yyyy-MM-dd"),
                    ca.ExpiresUtc.ToString("yyyy-MM-dd"),
                    status);
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
