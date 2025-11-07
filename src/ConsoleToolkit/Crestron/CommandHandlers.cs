using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToolkit.Ssh;
using Renci.SshNet;
using Spectre.Console;

namespace ConsoleToolkit.Crestron
{
    internal static class CommandHandlers
    {
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

        public static async Task<bool> RegisterProgramAsync(IShellStream stream, int slot, CancellationToken cancellationToken)
        {
            return await RegisterProgramAsync(stream, slot, null, cancellationToken);
        }

        public static async Task<bool> RegisterProgramAsync(IShellStream stream, int slot, string? programEntryPoint, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 10);
            
            AnsiConsole.MarkupLine("[cyan]Registering program...[/]");
            var registerCommand = $"progreg -p:{slot}{(!string.IsNullOrWhiteSpace(programEntryPoint) ? $"-C:{programEntryPoint}" : string.Empty)}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {registerCommand}");
            stream.WriteLine(registerCommand);

            cancellationToken.ThrowIfCancellationRequested();

            AnsiConsole.MarkupLine("[cyan]Waiting for program to register...[/]");

            var result = await stream.WaitForCommandCompletionAsync(["Program(s) Registered..."], ["ERROR:Invalid Program Identifier specified."], cancellationToken, 60000);
            if (result)
            {
                AnsiConsole.MarkupLineInterpolated($"[green]Program {slot} registered successfully.[/]");
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] Program {slot} registration failed or timed out.");
            }

            return result;
        }

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
            return await stream.WaitForCommandCompletionAsync(successPatterns, ["ERROR:Invalid Program Identifier specified.", $"Specified program({slot}) not registered."], cancellationToken, 60000);
        }
    }
}
