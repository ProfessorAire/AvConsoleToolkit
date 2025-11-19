// <copyright file="AddressBookLookupCommand.cs">
// The MIT License
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.AddressBook
{
    /// <summary>
    /// Command that looks up a Crestron address book entry by IP address or device name and displays the result.
    /// </summary>
    public class AddressBookLookupCommand : AsyncCommand<AddressBookLookupSettings>
    {
        /// <summary>
        /// Executes the lookup operation and renders the entry as a table to the console.
        /// </summary>
        /// <param name="context">The command execution context provided by Spectre.Console.Cli.</param>
        /// <param name="settings">Command-line settings containing the identifier to look up.</param>
        /// <param name="cancellationToken">Token used to cancel the lookup operation.</param>
        /// <returns>Exit code 0 on success; 1 when no matching entry is found or an error occurs.</returns>
        /// <exception cref="OperationCanceledException">If <paramref name="cancellationToken"/> is cancelled during lookup.</exception>
        /// <exception cref="System.IO.IOException">I/O errors that occur while reading address book files during lookup.</exception>
        public override async Task<int> ExecuteAsync(CommandContext context, AddressBookLookupSettings settings, CancellationToken cancellationToken)
        {
            var entry = await AvConsoleToolkit.Crestron.ToolboxAddressBook.LookupEntryAsync(settings.Identifier);

            if (entry == null)
            {
                AnsiConsole.MarkupLine($"[red]No address book entry found for '{settings.Identifier.EscapeMarkup()}'[/]");
                return 1;
            }

            // Display the entry information
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[yellow]Property[/]");
            table.AddColumn("[green]Value[/]");

            table.AddRow("Device Name", !string.IsNullOrWhiteSpace(entry.DeviceName) ? entry.DeviceName.EscapeMarkup() : string.Empty);
            table.AddRow("IP Address", !string.IsNullOrWhiteSpace(entry.HostAddress) ? entry.HostAddress : string.Empty);
            table.AddRow("Username", !string.IsNullOrEmpty(entry.Username) ? entry.Username.EscapeMarkup() : string.Empty);
            table.AddRow("Password", !string.IsNullOrEmpty(entry.Password) ? "[dim]***hidden***[/]" : "[dim]<none>[/]");

            AnsiConsole.Write(table);

            return 0;
        }
    }
}
