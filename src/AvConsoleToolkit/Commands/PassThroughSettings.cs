// <copyright file="PassThroughSettings.cs">
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

using System.ComponentModel;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands
{
    /// <summary>
    /// Base settings for pass-through commands that establish interactive SSH sessions.
    /// </summary>
    public class PassThroughSettings : CommandSettings
    {
        /// <summary>
        /// Gets or sets the host address (IP or hostname) of the device.
        /// </summary>
        [CommandArgument(0, "[address]")]
        [Description("Host address (IP or hostname) of the device")]
        public string? Address { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether automatic reconnection should be disabled.
        /// </summary>
        [CommandOption("-n|--no-reconnect")]
        [Description("Disable automatic reconnection on connection loss")]
        public bool NoReconnect { get; set; }

        /// <summary>
        /// Gets or sets the password for SSH authentication.
        /// </summary>
        [CommandOption("-p|--password <PASSWORD>")]
        [Description("Password for SSH authentication")]
        public string? Password { get; set; }

        /// <summary>
        /// Gets or sets the username for SSH authentication.
        /// </summary>
        [CommandOption("-u|--username <USERNAME>")]
        [Description("Username for SSH authentication")]
        public string? Username { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to show verbose output.
        /// </summary>
        [CommandOption("-v|--verbose")]
        [Description("Show verbose diagnostic output")]
        public bool Verbose { get; set; }
    }
}
