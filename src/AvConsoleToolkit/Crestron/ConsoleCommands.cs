// <copyright file="ConsoleCommands.cs">
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
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace AvConsoleToolkit.Crestron
{
    /// <summary>
    /// Provides static command handler methods for interacting with Crestron programs via SSH shell stream.
    /// </summary>
    internal static class ConsoleCommands
    {
        /// <summary>
        /// Adds an entry to the IP table for a Crestron program.
        /// </summary>
        /// <param name="stream">The shell stream to send commands through.</param>
        /// <param name="slot">The program slot (1-10).</param>
        /// <param name="ipId">The IPID for the device (as byte, e.g., 0x03, 0x0F).</param>
        /// <param name="address">The IP address or hostname.</param>
        /// <param name="deviceId">Optional device ID (as byte).</param>
        /// <param name="roomId">Optional room ID.</param>
        /// <param name="port">Optional port number.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><see langword="true"/> if the entry was added successfully; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> or <paramref name="address"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is less than 1 or greater than 10.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
        public static async Task<bool> AddIpTableEntryAsync(
            Connections.IShellConnection stream,
            int slot,
            IpTable.Entry entry,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);

            // Build the ADDPeer command with hex-formatted IPID
            var commandBuilder = new System.Text.StringBuilder();
            commandBuilder.Append($"ADDPeer {entry.IpId:X2} {entry.Address}");

            if (entry.DeviceId.HasValue)
            {
                commandBuilder.Append($" -D:{entry.DeviceId.Value:X2}");
            }

            if (entry.Port.HasValue)
            {
                commandBuilder.Append($" -C:{entry.Port.Value}");
            }

            commandBuilder.Append($" -P:{slot}");

            if (!string.IsNullOrWhiteSpace(entry.RoomId))
            {
                commandBuilder.Append($" -U:{entry.RoomId}");
            }

            var addPeerCommand = commandBuilder.ToString();
            await stream.WriteLineAsync(addPeerCommand);
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string> successPatterns = ["Master List set.  Restart program to take effect"];
            var result = await stream.WaitForCommandCompletionAsync(successPatterns, [], cancellationToken, 3000, writeReceivedData: false);

            return result;
        }

        /// <summary>
        /// Clears the IP table for a Crestron program in the specified slot.
        /// </summary>
        /// <param name="stream">The shell stream to send commands through.</param>
        /// <param name="slot">The program slot whose IP table should be cleared (1-10).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><see langword="true"/> if the IP table was cleared successfully; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is less than 1 or greater than 10.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
        public static async Task<bool> ClearIpTableAsync(Connections.IShellConnection stream, int slot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);

            var clearCommand = $"ipt -p:{slot} -C";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {clearCommand}");
            await stream.WriteLineAsync(clearCommand);
            cancellationToken.ThrowIfCancellationRequested();

            AnsiConsole.MarkupLine("[cyan]Clearing IP table...[/]");

            IEnumerable<string> successPatterns = [$"Cleared IP Table for program  {slot}", $"Unable to clear IP Table for program {slot}"];
            var result = await stream.WaitForCommandCompletionAsync(successPatterns, [], cancellationToken, 3000, false);

            if (result)
            {
                AnsiConsole.MarkupLine("[green]IP table cleared successfully.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Warning: IP table clear may have failed or timed out.[/]");
            }

            return result;
        }

        /// <summary>
        /// Sends a command to kill a Crestron program in the specified slot.
        /// </summary>
        /// <param name="stream">The shell stream to send commands through.</param>
        /// <param name="slot">The program slot to kill (1-10).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><see langword="true"/> if the program was killed or did not exist; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is less than 1 or greater than 10.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
        public static async Task<bool> KillProgramAsync(Connections.IShellConnection stream, int slot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);

            var killCommand = $"killprog -p:{slot}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {killCommand}");

            await stream.WriteLineAsync(killCommand);
            cancellationToken.ThrowIfCancellationRequested();

            AnsiConsole.MarkupLine("[cyan]Waiting for killprog to complete.[/]");

            IEnumerable<string> successPatterns = ["Program Stopped", "** Specified App does not exist **", $"Specified program {1} successfully deleted"];
            return await stream.WaitForCommandCompletionAsync(successPatterns, [], cancellationToken, 30000);
        }

        /// <summary>
        /// Loads a Crestron program in the specified slot from an <c>lpz</c> or <c>cpz</c>.
        /// </summary>
        /// <param name="stream">The shell stream to send commands through.</param>
        /// <param name="slot">The program slot to stop (1-10).</param>
        /// <param name="doNotStart">A value indicating whether the program should not be started.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><see langword="true"/> if the program was stopped or did not exist; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is less than 1 or greater than 10.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
        public static async Task<bool> ProgramLoadAsync(Connections.IShellConnection stream, int slot, bool doNotStart, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);

            var progLoadCommand = $"progload -p:{slot}{(doNotStart ? " -D" : string.Empty)}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {progLoadCommand}");
            await stream.WriteLineAsync(progLoadCommand);
            AnsiConsole.MarkupLine("[cyan]Waiting for program to load...[/]");
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string> successPatterns = doNotStart ? [$"Program Registered successfully for App {slot}"] : ["Program(s) Started...", "Program Start successfully sent for App"];
            return await stream.WaitForCommandCompletionAsync(successPatterns, ["ERROR:Invalid Program Identifier specified.", $"Specified program({slot}) not registered.", $"Error:Failure during program upload for App {slot}"], cancellationToken, 45000);
        }

        /// <summary>
        /// Registers a Crestron program in the specified slot.
        /// </summary>
        /// <param name="stream">The shell stream to send commands through.</param>
        /// <param name="slot">The program slot to register (1-10).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><see langword="true"/> if registration succeeded; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is less than 1 or greater than 10.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
        public static async Task<bool> RegisterProgramAsync(Connections.IShellConnection stream, int slot, CancellationToken cancellationToken)
        {
            return await RegisterProgramAsync(stream, slot, null, cancellationToken);
        }

        /// <summary>
        /// Registers a Crestron program in the specified slot, optionally specifying an entry point.
        /// </summary>
        /// <param name="stream">The shell stream to send commands through.</param>
        /// <param name="slot">The program slot to register (1-10).</param>
        /// <param name="programEntryPoint">Optional entry point for the program.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><see langword="true"/> if registration succeeded; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is less than 1 or greater than 10.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
        public static async Task<bool> RegisterProgramAsync(Connections.IShellConnection stream, int slot, string? programEntryPoint, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);

            AnsiConsole.MarkupLine("[cyan]Registering program...[/]");
            var registerCommand = $"progreg -p:{slot}{(!string.IsNullOrWhiteSpace(programEntryPoint) ? $" -C:{programEntryPoint}" : string.Empty)}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {registerCommand}");
            await stream.WriteLineAsync(registerCommand);
            await stream.WriteLineAsync("progreg");

            cancellationToken.ThrowIfCancellationRequested();

            var regSuccess = $"Program {slot} is registered{(!string.IsNullOrWhiteSpace(programEntryPoint) ? " (#)" : string.Empty)}";
            var result = await stream.WaitForCommandCompletionAsync([regSuccess], ["ERROR:Invalid Program Identifier specified."], cancellationToken, 60000);
            if (result)
            {
                AnsiConsole.MarkupLineInterpolated($"[green]Program {slot} registered successfully.[/]");
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Error: Program {slot} registration failed or timed out.[/]");
            }

            return result;
        }

        /// <summary>
        /// Restarts a Crestron program in the specified slot.
        /// </summary>
        /// <param name="stream">The shell stream to send commands through.</param>
        /// <param name="slot">The program slot to restart (1-10).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><see langword="true"/> if the program was started successfully; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is less than 1 or greater than 10.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
        public static async Task<bool> RestartProgramAsync(Connections.IShellConnection stream, int slot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);

            var startCommand = $"progres -p:{slot}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {startCommand}");

            await stream.WriteLineAsync(startCommand);
            cancellationToken.ThrowIfCancellationRequested();

            AnsiConsole.MarkupLine("[cyan]Waiting for program to start...[/]");

            IEnumerable<string> successPatterns = ["Program(s) Started..."];
            var result = await stream.WaitForCommandCompletionAsync(successPatterns, ["ERROR:Invalid Program Identifier specified.", $"Specified program({slot}) not registered."], cancellationToken, 60000);
            if (result)
            {
                AnsiConsole.MarkupLine("[green]Program started successfully.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Error: Program start failed or timed out.[/]");
            }

            return result;
        }

        /// <summary>
        /// Stops a Crestron program in the specified slot.
        /// </summary>
        /// <param name="stream">The shell stream to send commands through.</param>
        /// <param name="slot">The program slot to stop (1-10).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><see langword="true"/> if the program was stopped or did not exist; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is less than 1 or greater than 10.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
        public static async Task<bool> StopProgramAsync(Connections.IShellConnection stream, int slot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);

            var stopCommand = $"stopprog -p:{slot} -V -K";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {stopCommand}");
            await stream.WriteLineAsync(stopCommand);
            AnsiConsole.MarkupLine("[cyan]Waiting for program to stop...[/]");
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string> successPatterns = ["Program Stopped", "** Specified App does not exist **"];

            return await stream.WaitForCommandCompletionAsync(successPatterns, [], cancellationToken);
        }
    }
}
