// <copyright file="DeviceDeleteCommand.cs">
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

using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.CertManager;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert.Device
{
    /// <summary>
    /// Command that deletes a device certificate from the database.
    /// </summary>
    public sealed class DeviceDeleteCommand : AsyncCommand<DeviceDeleteSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, DeviceDeleteSettings settings, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;

            var dbPath = CertDatabase.ResolveDatabasePath(settings.DatabasePath);
            if (dbPath == null)
            {
                AnsiConsole.MarkupLine("[yellow]No certificate database found. Use --db to specify a path.[/]");
                return 1;
            }

            using var db = new CertDatabase(dbPath);
            var cert = db.GetCert(settings.CertId);

            if (cert == null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Certificate with ID {settings.CertId} not found.");
                return 1;
            }

            if (!settings.Yes)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] This will delete the certificate for '{cert.Fqdn.EscapeMarkup()}' (ID {cert.Id}).");
                if (!AnsiConsole.Confirm("Are you sure you want to proceed?", false))
                {
                    AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
                    return 0;
                }
            }

            db.DeleteCert(cert.Id);
            AnsiConsole.MarkupLine($"[green]Certificate for '{cert.Fqdn.EscapeMarkup()}' (ID {cert.Id}) deleted.[/]");
            return 0;
        }
    }
}
