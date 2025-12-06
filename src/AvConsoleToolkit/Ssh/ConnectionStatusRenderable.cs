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
    /// Status of a connection.
    /// </summary>
    public enum ConnectionStatus
    {
        /// <summary>
        /// The connection is not established.
        /// </summary>
        NotConnected,

        /// <summary>
        /// The connection is being established.
        /// </summary>
        Connecting,

        /// <summary>
        /// The connection has been established.
        /// </summary>
        Connected,

        /// <summary>
        /// The connection was lost.
        /// </summary>
        LostConnection,

        /// <summary>
        /// The connection is being re-established.
        /// </summary>
        Reconnecting,

        /// <summary>
        /// The connection is being closed.
        /// </summary>
        Disconnecting,
    }

    /// <summary>
    /// Provides information about a connection's status.
    /// </summary>
    public class ConnectionStatusRenderable : IRenderable
    {
        private readonly string connectionName;
        private readonly ConnectionStatus status;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionStatusRenderable"/> class.
        /// </summary>
        /// <param name="connectionName">The name or description of the connection.</param>
        /// <param name="status">The current status of the connection.</param>
        public ConnectionStatusRenderable(string connectionName, ConnectionStatus status)
        {
            this.connectionName = connectionName ?? throw new ArgumentNullException(nameof(connectionName));
            this.status = status;
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

        private string GetStatusText()
        {
            var statusText = this.status switch
            {
                ConnectionStatus.NotConnected => "Not Connected",
                ConnectionStatus.Connecting => "Connecting...",
                ConnectionStatus.Connected => "Connected",
                ConnectionStatus.LostConnection => "Lost Connection",
                ConnectionStatus.Reconnecting => "Reconnecting...",
                ConnectionStatus.Disconnecting => "Disconnecting...",
                _ => "Unknown"
            };

            return $"{this.connectionName}: {statusText}";
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
                ConnectionStatus.Disconnecting => Color.Yellow,
                _ => Color.White
            };
        }
    }
}
