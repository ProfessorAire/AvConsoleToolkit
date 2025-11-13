// <copyright file="ConsoleCommands.cs">
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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToolkit.Ssh;
using Spectre.Console;

namespace ConsoleToolkit.Crestron
{
    /// <summary>
    /// Provides static command handler methods for interacting with Crestron programs via SSH shell stream.
    /// </summary>
    internal static class ConsoleCommands
    {
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
        public static async Task<bool> KillProgramAsync(IShellStream stream, int slot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);

            var killCommand = $"killprog -p:{slot}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {killCommand}");

            stream.WriteLine(killCommand);
            cancellationToken.ThrowIfCancellationRequested();

            AnsiConsole.MarkupLine("[cyan]Waiting for killprog to complete.");

            IEnumerable<string> successPatterns = ["Program Stopped", "** Specified App does not exist **"];
            return await stream.WaitForCommandCompletionAsync(successPatterns, [], cancellationToken, 30000);
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
        public static async Task<bool> RegisterProgramAsync(IShellStream stream, int slot, CancellationToken cancellationToken)
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
        public static async Task<bool> RegisterProgramAsync(IShellStream stream, int slot, string? programEntryPoint, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);

            AnsiConsole.MarkupLine("[cyan]Registering program...[/]");
            var registerCommand = $"progreg -p:{slot}{(!string.IsNullOrWhiteSpace(programEntryPoint) ? $" -C:{programEntryPoint}" : string.Empty)}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {registerCommand}");
            stream.WriteLine(registerCommand);
            stream.WriteLine("progreg");

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
        public static async Task<bool> RestartProgramAsync(IShellStream stream, int slot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);

            var startCommand = $"progres -p:{slot}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {startCommand}");

            stream.WriteLine(startCommand);
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
        public static async Task<bool> StopProgramAsync(IShellStream stream, int slot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);

            var stopCommand = $"stopprog -p:{slot}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {stopCommand}");
            stream.WriteLine(stopCommand);
            AnsiConsole.MarkupLine("[cyan]Waiting for program to stop...[/]");
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string> successPatterns = ["Program Stopped", "** Specified App does not exist **"];

            return await stream.WaitForCommandCompletionAsync(successPatterns, [], cancellationToken);
        }
    }
}
