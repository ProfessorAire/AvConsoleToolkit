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
    /// Renders the status of both SSH and SFTP connections in a compact, live-updating format.
    /// </summary>
    public class ConnectionStatusRenderable : IRenderable
    {
        private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];

        private readonly ConnectionStatusModel model;

        private readonly bool showSpinner;

        private readonly int spinnerIndex;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionStatusRenderable"/> class.
        /// </summary>
        /// <param name="model">The status model containing SSH and SFTP status.</param>
        /// <param name="showSpinner">Whether to show a spinner for active states.</param>
        /// <param name="spinnerIndex">The spinner frame index (for animation).</param>
        public ConnectionStatusRenderable(ConnectionStatusModel model, bool showSpinner = true, int spinnerIndex = 0)
        {
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.showSpinner = showSpinner;
            this.spinnerIndex = spinnerIndex;
        }

        /// <inheritdoc/>
        public Measurement Measure(RenderOptions options, int maxWidth)
        {
            var sshText = this.GetStatusText("SSH", this.model.HostAddress, this.model.SshState, this.model.SshAttempt, this.model.SshMaxAttempts, this.showSpinner && IsActive(this.model.SshState));
            var sftpText = this.GetStatusText("SFTP", this.model.HostAddress, this.model.SftpState, this.model.SftpAttempt, this.model.SftpMaxAttempts, this.showSpinner && IsActive(this.model.SftpState));
            var maxLen = Math.Max(sshText.Length, sftpText.Length);
            return new Measurement(maxLen, maxLen);
        }

        /// <inheritdoc/>
        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            yield return new Segment(this.GetStatusText("SSH", this.model.HostAddress, this.model.SshState, this.model.SshAttempt, this.model.SshMaxAttempts, this.showSpinner && IsActive(this.model.SshState)), new Style(this.GetStatusColor(this.model.SshState)));
            yield return Segment.LineBreak;
            yield return new Segment(this.GetStatusText("SFTP", this.model.HostAddress, this.model.SftpState, this.model.SftpAttempt, this.model.SftpMaxAttempts, this.showSpinner && IsActive(this.model.SftpState)), new Style(this.GetStatusColor(this.model.SftpState)));
        }

        private static bool IsActive(ConnectionStatus status)
        {
            return status == ConnectionStatus.Connecting || status == ConnectionStatus.Reconnecting;
        }

        private Color GetStatusColor(ConnectionStatus status)
        {
            return status switch
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

        private string GetStatusText(string type, string host, ConnectionStatus status, int attempt, int maxAttempts, bool spinner)
        {
            string statusText = status switch
            {
                ConnectionStatus.NotConnected => "Not Connected",
                ConnectionStatus.Connecting => $"Connecting...{(spinner ? $" {SpinnerFrames[this.spinnerIndex % SpinnerFrames.Length]}" : string.Empty)}",
                ConnectionStatus.Connected => "Connected",
                ConnectionStatus.LostConnection => "Lost Connection...Reconnecting",
                ConnectionStatus.Reconnecting => $"Connection Failed...Reconnecting ({attempt}{(maxAttempts > 0 ? $" of {maxAttempts}" : string.Empty)}){(spinner ? $" {SpinnerFrames[this.spinnerIndex % SpinnerFrames.Length]}" : string.Empty)}",
                ConnectionStatus.ConnectionFailed => $"Connection Failed{(maxAttempts > 0 ? $" ({attempt} of {maxAttempts})" : string.Empty)}",
                ConnectionStatus.Disconnecting => "Disconnecting...",
                _ => "Unknown"
            };
            return $"{type,-5} ({host}): {statusText}";
        }
    }
}
