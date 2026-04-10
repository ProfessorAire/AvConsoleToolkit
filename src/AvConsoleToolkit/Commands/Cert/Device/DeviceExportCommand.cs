// <copyright file="DeviceExportCommand.cs">
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

namespace AvConsoleToolkit.Commands.Cert.Device
{
    /// <summary>
    /// Command that exports a device certificate from the database to files on disk.
    /// Certificates are stored in the database and only written to disk when explicitly exported.
    /// </summary>
    public sealed class DeviceExportCommand : AsyncCommand<DeviceExportSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, DeviceExportSettings settings, CancellationToken cancellationToken)
        {
            var dbPath = settings.ResolveDbPath();
            if (dbPath == null)
            {
                AnsiConsole.MarkupLine("[yellow]No certificate database found. Use --db to specify a path.[/]");
                return 1;
            }

            using var db = new CertDatabase(dbPath);

            // Resolver displays appropriate error messages when no match is found
            var cert = DeviceCertResolver.Resolve(db, settings.NameOrId);

            if (cert == null)
            {
                return 1;
            }

            var outputDir = settings.OutputDirectory ?? Path.Combine(Environment.CurrentDirectory, cert.Name);
            Directory.CreateDirectory(outputDir);

            if (settings.PfxOnly)
            {
                var pfxPath = Path.Combine(outputDir, $"{cert.Name}.pfx");
                await File.WriteAllBytesAsync(pfxPath, cert.Pfx, cancellationToken);
                AnsiConsole.MarkupLine($"[green]Exported PFX:[/] {pfxPath.EscapeMarkup()}");
            }
            else
            {
                var certPath = Path.Combine(outputDir, $"{cert.Name}.crt");
                var keyPath = Path.Combine(outputDir, $"{cert.Name}.key");
                var pfxPath = Path.Combine(outputDir, $"{cert.Name}.pfx");
                var chainPath = Path.Combine(outputDir, $"{cert.Name}-fullchain.crt");

                await File.WriteAllBytesAsync(certPath, cert.CertificatePem, cancellationToken);
                await File.WriteAllBytesAsync(keyPath, cert.PrivateKeyPem, cancellationToken);
                await File.WriteAllBytesAsync(pfxPath, cert.Pfx, cancellationToken);
                await File.WriteAllBytesAsync(chainPath, cert.FullChainPem, cancellationToken);

                AnsiConsole.MarkupLine($"[green]Exported certificate files to:[/] {outputDir.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"  Certificate: {certPath.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"  Private Key: {keyPath.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"  PFX Bundle:  {pfxPath.EscapeMarkup()}");
                AnsiConsole.MarkupLine($"  Full Chain:  {chainPath.EscapeMarkup()}");
            }

            return 0;
        }
    }
}
