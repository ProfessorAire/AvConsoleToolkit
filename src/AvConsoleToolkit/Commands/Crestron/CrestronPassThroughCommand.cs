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
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace AvConsoleToolkit.Commands.Crestron
{
    /// <summary>
    /// Crestron-specific implementation of the pass-through command.
    /// Provides interactive SSH sessions tailored for Crestron devices.
    /// </summary>
    public sealed class CrestronPassThroughCommand : PassThroughCommand<PassThroughSettings>
    {
        /// <summary>
        /// Gets the Crestron exit command ("bye").
        /// </summary>
        protected override string ExitCommand => "bye";

        /// <summary>
        /// Gets the command branch for Crestron commands ("crestron").
        /// </summary>
        protected override string CommandBranch => "crestron";

        /// <summary>
        /// Handles tab completion for Crestron devices by forwarding the current buffer and tab to the device.
        /// The base class already sends the buffer + tab, so we just return false to use default behavior.
        /// </summary>
        /// <param name="keyInfo">The console key information.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>False to allow base class to handle tab completion.</returns>
        protected override Task<bool> HandleSpecialKeyAsync(System.ConsoleKeyInfo keyInfo, CancellationToken cancellationToken)
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
            // Wait a moment for the device to be ready
            await Task.Delay(100, cancellationToken);

            // Could send any Crestron-specific initialization commands here if needed
            // For example: Set terminal type, configure display options, etc.
        }
    }
}
