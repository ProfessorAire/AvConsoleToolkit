// <copyright file="AddressBookListCommand.cs">
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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.Crestron;
using IniParser;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.AddressBook
{
    /// <summary>
    /// Command that lists all entries from configured Crestron address books.
    /// </summary>
    public class AddressBookListCommand : AsyncCommand<AddressBookListSettings>
    {
        /// <summary>
        /// Executes the list operation and renders all address book entries as a table to the console.
        /// </summary>
        /// <param name="context">The command execution context provided by Spectre.Console.Cli.</param>
        /// <param name="settings">Command-line settings for the list operation.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>Exit code 0 on success; 1 when no address books are configured or an error occurs.</returns>
        public override async Task<int> ExecuteAsync(CommandContext context, AddressBookListSettings settings, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var config = Configuration.AppConfig.Settings;
                var addressBookLocations = config.Connection.AddressBooksLocation;

                if (string.IsNullOrWhiteSpace(addressBookLocations))
                {
                    AnsiConsole.MarkupLine("[red]No address book locations configured. Use 'config set Connection AddressBooksLocation <path>' to configure.[/]");
                    return 1;
                }

                // Split multiple locations
                var locations = addressBookLocations
                    .Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
                    .Select(loc => loc.Trim())
                    .Where(loc => !string.IsNullOrWhiteSpace(loc));

                var allEntries = new List<(ToolboxAddressBook.Entry Entry, string SourceFile)>();

                foreach (var location in locations)
                {
                    // Check if location is a directory
                    if (Directory.Exists(location))
                    {
                        // Search for .xadr files
                        var xadrFiles = Directory.GetFiles(location, "*.xadr", SearchOption.AllDirectories);
                        foreach (var file in xadrFiles)
                        {
                            var entries = ReadAllEntriesFromFile(file);
                            allEntries.AddRange(entries.Select(e => (e, file)));
                        }
                    }
                    else if (File.Exists(location) && location.EndsWith(".xadr", StringComparison.OrdinalIgnoreCase))
                    {
                        var entries = ReadAllEntriesFromFile(location);
                        allEntries.AddRange(entries.Select(e => (e, location)));
                    }
                }

                if (allEntries.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No address book entries found.[/]");
                    return 0;
                }

                // Display entries
                var table = new Table();
                table.Border(TableBorder.Rounded);
                table.AddColumn("[yellow]Device Name[/]");
                table.AddColumn("[green]Host Address[/]");
                table.AddColumn("[cyan]Username[/]");
                table.AddColumn("[blue]Password[/]");

                if (settings.Detailed)
                {
                    table.AddColumn("[dim]Source File[/]");
                }

                foreach (var (entry, sourceFile) in allEntries.OrderBy(e => e.Entry.DeviceName))
                {
                    var deviceName = !string.IsNullOrWhiteSpace(entry.DeviceName) ? entry.DeviceName.EscapeMarkup() : "[dim]<unknown>[/]";
                    var ipAddress = !string.IsNullOrWhiteSpace(entry.HostAddress) ? entry.HostAddress : "[dim]<none>[/]";
                    var username = !string.IsNullOrEmpty(entry.Username) ? entry.Username.EscapeMarkup() : "[dim]<none>[/]";
                    var password = !string.IsNullOrEmpty(entry.Password) ? "[dim]***hidden***[/]" : "[dim]<none>[/]";

                    if (settings.Detailed)
                    {
                        table.AddRow(deviceName, ipAddress, username, password, Path.GetFileName(sourceFile).EscapeMarkup());
                    }
                    else
                    {
                        table.AddRow(deviceName, ipAddress, username, password);
                    }
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"\n[dim]Total entries: {allEntries.Count}[/]");

                return 0;
            }, cancellationToken);
        }

        /// <summary>
        /// Reads all entries from a Crestron address book file.
        /// </summary>
        /// <param name="filePath">Path to the address book file.</param>
        /// <returns>List of entries found in the file.</returns>
        private static List<ToolboxAddressBook.Entry> ReadAllEntriesFromFile(string filePath)
        {
            var entries = new List<ToolboxAddressBook.Entry>();

            try
            {
                var parser = new FileIniDataParser();
                var data = parser.ReadFile(filePath);

                if (!data.Sections.ContainsSection("ComSpecs"))
                {
                    return entries;
                }

                foreach (var entryData in data["ComSpecs"])
                {
                    var entry = ParseComSpecEntry(entryData.KeyName, entryData.Value);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error reading address book file '{Path.GetFileName(filePath).EscapeMarkup()}': {ex.Message.EscapeMarkup()}[/]");
            }

            return entries;
        }

        /// <summary>
        /// Parses a ComSpec entry value.
        /// Format: "auto 10.20.0.22;username user;password pass;console secondary"
        /// </summary>
        private static ToolboxAddressBook.Entry? ParseComSpecEntry(string deviceName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var entry = new ToolboxAddressBook.Entry { DeviceName = deviceName };

            // Split by semicolon
            var parts = value.Split(';');

            foreach (var part in parts)
            {
                var trimmed = part.Trim();

                if (trimmed.StartsWith("auto ", StringComparison.OrdinalIgnoreCase))
                {
                    entry.HostAddress = trimmed[5..].Trim();
                }
                else if (trimmed.StartsWith("ssh ", StringComparison.OrdinalIgnoreCase))
                {
                    entry.HostAddress = trimmed[4..].Trim();
                }
                else if (trimmed.StartsWith("username ", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Username = trimmed[9..].Trim();
                }
                else if (trimmed.StartsWith("password ", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Password = trimmed[9..].Trim();
                }
            }

            return entry;
        }
    }
}
