// <copyright file="CertInstallRootCommand.cs">
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
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.CertManager;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert
{
    /// <summary>
    /// Command that installs a root CA certificate on the current machine.
    /// Uses the appropriate mechanism based on the operating system (Windows or Linux).
    /// </summary>
    public sealed class CertInstallRootCommand : AsyncCommand<CertInstallRootSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, CertInstallRootSettings settings, CancellationToken cancellationToken)
        {
            var dbPath = settings.ResolveDbPath();
            if (dbPath == null)
            {
                AnsiConsole.MarkupLine("[yellow]No certificate database found. Use --db to specify a path.[/]");
                return 1;
            }

            using var db = new CertDatabase(dbPath);
            var ca = db.GetCaByName(settings.CaName);

            if (ca == null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Certificate Authority '{settings.CaName.EscapeMarkup()}' not found.");
                return 1;
            }

            AnsiConsole.MarkupLine($"[cyan]Installing root CA '{ca.Name.EscapeMarkup()}' on the local machine...[/]");

            if (OperatingSystem.IsWindows())
            {
                return InstallOnWindows(ca);
            }
            else if (OperatingSystem.IsLinux())
            {
                return await InstallOnLinuxAsync(ca, cancellationToken);
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Unsupported operating system. Only Windows and Linux are supported.");
                return 1;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static int InstallOnWindows(CertificateAuthorityRecord ca)
        {
            try
            {
                var certPem = Encoding.UTF8.GetString(ca.CertificatePem);
                var cert = X509Certificate2.CreateFromPem(certPem);

                using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                store.Add(cert);
                store.Close();

                AnsiConsole.MarkupLine("[green]Root CA certificate installed in the Windows Trusted Root store.[/]");
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error installing certificate:[/] {ex.Message.EscapeMarkup()}");
                AnsiConsole.MarkupLine("[yellow]Hint: This command may require administrator privileges on Windows.[/]");
                return 1;
            }
        }

        private static async Task<int> InstallOnLinuxAsync(CertificateAuthorityRecord ca, CancellationToken cancellationToken)
        {
            try
            {
                var certFileName = $"{ca.Name}.crt";

                // Determine the cert directory based on available paths
                string certDir;
                string updateCommand;

                if (Directory.Exists("/usr/local/share/ca-certificates"))
                {
                    // Debian/Ubuntu
                    certDir = "/usr/local/share/ca-certificates";
                    updateCommand = "update-ca-certificates";
                }
                else if (Directory.Exists("/etc/pki/ca-trust/source/anchors"))
                {
                    // RHEL/CentOS/Fedora
                    certDir = "/etc/pki/ca-trust/source/anchors";
                    updateCommand = "update-ca-trust";
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Could not find a supported CA certificates directory on this Linux system.");
                    return 1;
                }

                var certPath = Path.Combine(certDir, certFileName);

                AnsiConsole.MarkupLine($"[dim]Writing certificate to: {certPath.EscapeMarkup()}[/]");
                await File.WriteAllBytesAsync(certPath, ca.CertificatePem, cancellationToken);

                AnsiConsole.MarkupLine($"[dim]Running: {updateCommand}[/]");
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = updateCommand,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });

                if (process != null)
                {
                    await process.WaitForExitAsync(cancellationToken);
                    var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        AnsiConsole.MarkupLine($"[dim]{output.EscapeMarkup()}[/]");
                    }

                    if (process.ExitCode != 0)
                    {
                        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                        AnsiConsole.MarkupLine($"[red]{error.EscapeMarkup()}[/]");
                        AnsiConsole.MarkupLine("[yellow]Hint: This command may require root/sudo privileges on Linux.[/]");
                        return 1;
                    }
                }

                AnsiConsole.MarkupLine("[green]Root CA certificate installed in the system trust store.[/]");
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error installing certificate:[/] {ex.Message.EscapeMarkup()}");
                AnsiConsole.MarkupLine("[yellow]Hint: This command may require root/sudo privileges on Linux.[/]");
                return 1;
            }
        }
    }
}
