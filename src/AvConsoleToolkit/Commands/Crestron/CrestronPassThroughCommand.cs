// <copyright file="CrestronPassThroughCommand.cs">
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

namespace AvConsoleToolkit.Commands.Crestron
{
    /// <summary>
    /// Crestron-specific implementation of the pass-through command.
    /// Provides interactive SSH sessions tailored for Crestron devices.
    /// </summary>
    public sealed class CrestronPassThroughCommand : PassThroughCommand<PassThroughSettings>
    {
        /// <summary>
        /// Gets the command branch for Crestron commands ("crestron").
        /// </summary>
        protected override string CommandBranch => "crestron";

        /// <summary>
        /// Gets the Crestron exit command ("bye").
        /// </summary>
        protected override string ExitCommand => "bye";

        /// <summary>
        /// Gets command mappings for Crestron devices, merging default Unix-like aliases with user-configured mappings.
        /// User-configured mappings from settings take precedence over defaults.
        /// </summary>
        /// <returns>A dictionary mapping common Unix commands to their Crestron equivalents.</returns>
        protected override IReadOnlyDictionary<string, string>? GetCommandMappings()
        {
            // Start with default mappings
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ls", "dir" },
                { "cat", "type" },
                { "rm", "del" },
                { "cp", "copy" },
                { "mv", "move" },
                { "pwd", "cd" },
                { "edit", "::sftp edit" },
            };

            // Parse and merge user-defined mappings from configuration
            var configMappings = Configuration.AppConfig.Settings.PassThrough.CrestronCommandMappings;
            if (!string.IsNullOrWhiteSpace(configMappings))
            {
                var pairs = configMappings.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var pair in pairs)
                {
                    var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        // User-defined mappings override defaults
                        merged[parts[0]] = parts[1];
                    }
                }
            }

            return merged.Count > 0 ? merged : null;
        }

        /// <summary>
        /// Handles tab completion for Crestron devices by forwarding the current buffer and tab to the device.
        /// The base class already sends the buffer + tab, so we just return false to use default behavior.
        /// </summary>
        /// <param name="keyInfo">The console key information.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>False to allow base class to handle tab completion.</returns>
        protected override Task<bool> HandleSpecialKeyAsync(ConsoleKeyInfo keyInfo, CancellationToken cancellationToken)
        {
            // For Crestron, we want the default tab completion behavior
            // which sends the current buffer + tab to the device
            // The device will handle completion and send back results
            return Task.FromResult(false);
        }

        /// <summary>
        /// Called when connected to perform Crestron-specific initialization.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        protected override async Task OnConnectedAsync(CancellationToken cancellationToken)
        {
            // Some Crestron 3-Series devices require an initial carriage return to start sending data
            // Send a newline to trigger the initial prompt and header
            if (this.SshConnection != null)
            {
                await this.SshConnection.WriteLineAsync("echo off", cancellationToken);
            }

            // Could send any other Crestron-specific initialization commands here if needed
            // For example: Set terminal type, configure display options, etc.
        }
    }
}
