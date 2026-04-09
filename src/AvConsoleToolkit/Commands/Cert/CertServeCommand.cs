// <copyright file="CertServeCommand.cs">
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
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.CertManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert
{
    /// <summary>
    /// Command that starts a web server hosting a Blazor application for managing certificates.
    /// Exposes all certificate operations (CAs, device certs, deployment) through a reactive GUI.
    /// </summary>
    public sealed class CertServeCommand : AsyncCommand<CertServeSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, CertServeSettings settings, CancellationToken cancellationToken)
        {
            var dbPath = settings.ResolveDbPath()
                ?? Path.Combine(Environment.CurrentDirectory, CertDatabase.DefaultFileName);

            var url = $"http://localhost:{settings.Port}";

            AnsiConsole.MarkupLine($"[cyan]Starting Certificate Manager web UI...[/]");
            AnsiConsole.MarkupLine($"  Database: {dbPath.EscapeMarkup()}");
            AnsiConsole.MarkupLine($"  URL: [link]{url}[/]");
            AnsiConsole.MarkupLine("[dim]Press Ctrl+C to stop the server.[/]");

            var builder = WebApplication.CreateBuilder();
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Register the database as a scoped service so pages can inject it
            builder.Services.AddScoped<CertDatabase>(_ => new CertDatabase(dbPath));

            var app = builder.Build();

            app.UseStaticFiles();
            app.UseAntiforgery();

            app.MapRazorComponents<AvConsoleToolkit.Web.App>()
                .AddInteractiveServerRenderMode()
                .AddAdditionalAssemblies(typeof(AvConsoleToolkit.Web.App).Assembly);

            app.Urls.Clear();
            app.Urls.Add(url);

            if (settings.OpenBrowser)
            {
                _ = Task.Run(() =>
                {
                    Thread.Sleep(1500);
                    try
                    {
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch
                    {
                        // Best-effort; don't fail if browser can't be opened
                    }
                }, cancellationToken);
            }

            await app.RunAsync(url);

            return 0;
        }
    }
}
