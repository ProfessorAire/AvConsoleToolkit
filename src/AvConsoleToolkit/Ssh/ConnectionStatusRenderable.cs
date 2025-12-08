// <copyright file="ConnectionStatusRenderable.cs">
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
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AvConsoleToolkit.Ssh
{
    /// <summary>
    /// Provides information about a connection's status.
    /// </summary>
    public class ConnectionStatusRenderable : IRenderable
    {
        private readonly string connectionType;

        private readonly int? currentAttempt;

        private readonly string hostAddress;

        private readonly int? maxAttempts;

        private readonly ConnectionStatus status;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionStatusRenderable"/> class.
        /// </summary>
        /// <param name="connectionType">The type of connection (e.g., "SSH", "Telnet").</param>
        /// <param name="hostAddress">The host address being connected to.</param>
        /// <param name="status">The current status of the connection.</param>
        /// <param name="currentAttempt">The current attempt number (for reconnecting or connection failed status).</param>
        /// <param name="maxAttempts">The maximum number of attempts (use -1 for unlimited, null if not applicable).</param>
        public ConnectionStatusRenderable(string connectionType, string hostAddress, ConnectionStatus status, int? currentAttempt = null, int? maxAttempts = null)
        {
            this.connectionType = connectionType ?? throw new ArgumentNullException(nameof(connectionType));
            this.hostAddress = hostAddress ?? throw new ArgumentNullException(nameof(hostAddress));
            this.status = status;
            this.currentAttempt = currentAttempt;
            this.maxAttempts = maxAttempts;
        }

        /// <inheritdoc/>
        public Measurement Measure(RenderOptions options, int maxWidth)
        {
            var text = this.GetStatusText();
            return new Measurement(text.Length, text.Length);
        }

        /// <inheritdoc/>
        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            var color = this.GetStatusColor();
            var text = this.GetStatusText();
            yield return new Segment(text, new Style(color));
        }

        private string GetReconnectingText()
        {
            if (this.currentAttempt == null)
            {
                return "Reconnecting...";
            }

            if (this.maxAttempts == null || this.maxAttempts <= 0)
            {
                // Unlimited attempts or not specified
                return $"Reconnecting ({this.currentAttempt})";
            }

            // Show attempt count with max attempts
            return $"Reconnecting ({this.currentAttempt} of {this.maxAttempts})";
        }

        private Color GetStatusColor()
        {
            return this.status switch
            {
                ConnectionStatus.NotConnected => Color.Grey,
                ConnectionStatus.Connecting => Color.Yellow,
                ConnectionStatus.Connected => Color.Green,
                ConnectionStatus.LostConnection => Color.Red,
                ConnectionStatus.Reconnecting => Color.Orange1,
                ConnectionStatus.ConnectionFailed => Color.Red,
                ConnectionStatus.Disconnecting => Color.Yellow,
                _ => Color.White
            };
        }

        private string GetStatusText()
        {
            var statusText = this.status switch
            {
                ConnectionStatus.NotConnected => "Not Connected",
                ConnectionStatus.Connecting => "Connecting...",
                ConnectionStatus.Connected => "Connected",
                ConnectionStatus.LostConnection => "Lost Connection",
                ConnectionStatus.Reconnecting => this.GetReconnectingText(),
                ConnectionStatus.ConnectionFailed => "Connection Failed",
                ConnectionStatus.Disconnecting => "Disconnecting...",
                _ => "Unknown"
            };

            return $"{this.connectionType} ({this.hostAddress}): {statusText}";
        }
    }
}
