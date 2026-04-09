// <copyright file="DeviceCertResolver.cs">
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
using AvConsoleToolkit.CertManager;
using Spectre.Console;

namespace AvConsoleToolkit.Commands.Cert.Device
{
    /// <summary>
    /// Resolves a device certificate from a name or ID string.
    /// If the input is an integer, looks up by ID. Otherwise, looks up by name.
    /// If multiple certificates match the name, prompts the user to select one.
    /// </summary>
    internal static class DeviceCertResolver
    {
        /// <summary>
        /// Resolves a single device certificate from a name or ID string.
        /// </summary>
        /// <param name="db">The certificate database.</param>
        /// <param name="nameOrId">The name or ID to look up.</param>
        /// <returns>The resolved record, or <see langword="null"/> if not found or cancelled.</returns>
        public static DeviceCertificateRecord? Resolve(CertDatabase db, string nameOrId)
        {
            // Try ID first
            if (int.TryParse(nameOrId, out var id))
            {
                var byId = db.GetCert(id);
                if (byId != null)
                {
                    return byId;
                }
            }

            // Try name lookup
            var matches = db.GetCertsByName(nameOrId);
            if (matches.Count == 0)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] No certificate found matching '{nameOrId.EscapeMarkup()}'.");
                return null;
            }

            if (matches.Count == 1)
            {
                return matches[0];
            }

            // Multiple matches — prompt user to select
            AnsiConsole.MarkupLine($"[yellow]Multiple certificates found matching '{nameOrId.EscapeMarkup()}':[/]");
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<DeviceCertificateRecord>()
                    .Title("Select a certificate:")
                    .AddChoices(matches)
                    .UseConverter(c => $"[cyan]ID {c.Id}[/] — {c.Name.EscapeMarkup()} (DNS: {c.DnsNames.EscapeMarkup()}, Expires: {c.ExpiresUtc:yyyy-MM-dd})"));

            return selected;
        }
    }
}
