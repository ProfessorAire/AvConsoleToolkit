// <copyright file="ConnectionStatusModel.cs">
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

namespace AvConsoleToolkit.Ssh
{
    /// <summary>
    /// Represents the connection status and attempt counts for SFTP and SSH operations to a specified host.    
    /// </summary>
    /// <remarks>This model provides information about the current state and retry attempts for both SFTP and
    /// SSH connections. It can be used to track progress and status when managing remote connections in applications
    /// that interact with external hosts.</remarks>
    public class ConnectionStatusModel
    {
        /// <summary>
        /// Gets the network address of the host to which the connection will be established.
        /// </summary>
        public required string HostAddress { get; init; }

        /// <summary>
        /// Gets or sets the number of SFTP connection attempts made.
        /// </summary>
        public int SftpAttempt { get; set; } = 0;

        /// <summary>
        /// Gets or sets the maximum number of attempts to establish an SFTP connection before failing.
        /// </summary>
        public int SftpMaxAttempts { get; set; } = 0;

        /// <summary>
        /// Gets or sets the current status of the SFTP connection.
        /// </summary>
        public ConnectionStatus SftpState { get; set; } = ConnectionStatus.NotConnected;

        /// <summary>
        /// Gets or sets the number of SSH connection attempts made.
        /// </summary>
        public int SshAttempt { get; set; } = 0;

        /// <summary>
        /// Gets or sets the maximum number of SSH connection attempts before the operation fails.
        /// </summary>
        public int SshMaxAttempts { get; set; } = 0;

        /// <summary>
        /// Gets or sets the current SSH connection status.
        /// </summary>
        public ConnectionStatus SshState { get; set; } = ConnectionStatus.NotConnected;
    }
}
