// <copyright file="CaCreateCommand.cs">
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
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.CertManager;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert.Ca
{
    /// <summary>
    /// Command that creates a new Certificate Authority and stores it in the database.
    /// </summary>
    public sealed class CaCreateCommand : AsyncCommand<CaCreateSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, CaCreateSettings settings, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;

            var dbPath = CertDatabase.ResolveDatabasePath(settings.DatabasePath)
                ?? Path.Combine(Environment.CurrentDirectory, CertDatabase.DefaultFileName);

            using var db = new CertDatabase(dbPath);

            // Check if CA already exists
            var existing = db.GetCaByName(settings.Name);
            if (existing != null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] A Certificate Authority named '{settings.Name.EscapeMarkup()}' already exists (ID {existing.Id}).");
                return 1;
            }

            AnsiConsole.MarkupLine($"[cyan]Creating Root CA certificate for organization: {settings.Organization.EscapeMarkup()}[/]");

            var cert = CertificateGenerator.CreateCaCertificate(
                settings.Country,
                settings.Organization,
                settings.Name,
                settings.ValidityDays);

            var record = new CertificateAuthorityRecord
            {
                Name = settings.Name,
                Country = settings.Country,
                Organization = settings.Organization,
                CertificatePem = CertificateGenerator.ExportCertificatePem(cert),
                PrivateKeyPem = CertificateGenerator.ExportPrivateKeyPem(cert),
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = cert.NotAfter.ToUniversalTime(),
            };

            var id = db.InsertCa(record);

            AnsiConsole.MarkupLine($"[green]Root CA '{settings.Name.EscapeMarkup()}' created successfully (ID {id}).[/]");
            AnsiConsole.MarkupLine($"  Organization: {settings.Organization.EscapeMarkup()}");
            AnsiConsole.MarkupLine($"  Expires: {record.ExpiresUtc:yyyy-MM-dd}");
            AnsiConsole.MarkupLine($"  Database: {dbPath.EscapeMarkup()}");

            return 0;
        }
    }
}
