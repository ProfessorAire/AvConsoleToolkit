// <copyright file="SshConnection.cs">
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Spectre.Console;

namespace AvConsoleToolkit.Ssh
{
    /// <summary>
    /// Implements SSH connection with lazy initialization and automatic recovery.
    /// </summary>
    public class SshConnection : ISshConnection
    {
        private readonly string hostAddress;
        private readonly int port;
        private readonly string? username;
        private readonly string? password;
        private readonly string? privateKeyPath;
        private readonly Lock lockObject = new();
        
        private SshClient? sshClient;
        private SftpClient? sftpClient;
        private IShellStream? shellStream;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="SshConnection"/> class with password authentication.
        /// </summary>
        /// <param name="hostAddress">The host address to connect to.</param>
        /// <param name="port">The port to connect on.</param>
        /// <param name="username">The username for authentication.</param>
        /// <param name="password">The password for authentication.</param>
        public SshConnection(string hostAddress, int port, string username, string password)
        {
            this.hostAddress = hostAddress ?? throw new ArgumentNullException(nameof(hostAddress));
            this.port = port;
            this.username = username ?? throw new ArgumentNullException(nameof(username));
            this.password = password ?? throw new ArgumentNullException(nameof(password));
            this.privateKeyPath = null;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SshConnection"/> class with SSH key authentication.
        /// </summary>
        /// <param name="hostAddress">The host address to connect to.</param>
        /// <param name="port">The port to connect on.</param>
        /// <param name="privateKeyPath">The path to the private key file.</param>
        public SshConnection(string hostAddress, int port, string privateKeyPath)
        {
            this.hostAddress = hostAddress ?? throw new ArgumentNullException(nameof(hostAddress));
            this.port = port;
            this.privateKeyPath = privateKeyPath ?? throw new ArgumentNullException(nameof(privateKeyPath));
            this.username = null;
            this.password = null;
        }

        /// <inheritdoc/>
        public bool IsConnected
        {
            get
            {
                lock (this.lockObject)
                {
                    return (this.sshClient?.IsConnected ?? false) ||
                           (this.sftpClient?.IsConnected ?? false);
                }
            }
        }

        /// <inheritdoc/>
        public async Task<ISshClient> GetSshClientAsync(CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            lock (this.lockObject)
            {
                if (this.sshClient != null && this.sshClient.IsConnected)
                {
                    return this.sshClient;
                }

                if (this.sshClient != null && !this.sshClient.IsConnected)
                {
                    // Clean up disconnected client
                    this.CleanupSshClient();
                }
            }

            // Create and connect new client
            return await this.ConnectSshClientAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<ISftpClient> GetSftpClientAsync(CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            lock (this.lockObject)
            {
                if (this.sftpClient != null && this.sftpClient.IsConnected)
                {
                    return this.sftpClient;
                }

                if (this.sftpClient != null && !this.sftpClient.IsConnected)
                {
                    // Clean up disconnected client
                    this.CleanupSftpClient();
                }
            }

            // Create and connect new client
            return await this.ConnectSftpClientAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<IShellStream> GetShellStreamAsync(CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();

            lock (this.lockObject)
            {
                if (this.shellStream != null)
                {
                    return this.shellStream;
                }
            }

            // Ensure SSH client is connected first
            var client = await this.GetSshClientAsync(cancellationToken);
            
            if (!client.IsConnected)
            {
                AnsiConsole.Write(new ConnectionStatusRenderable(this.hostAddress, ConnectionStatus.Connecting));
                AnsiConsole.WriteLine();
                await client.ConnectAsync(cancellationToken);
                AnsiConsole.Write(new ConnectionStatusRenderable(this.hostAddress, ConnectionStatus.Connected));
                AnsiConsole.WriteLine();
            }

            // Create shell stream
            var stream = new ShellStreamWrapper(
                ((SshClient)client).CreateShellStream("xterm", 80, 24, 800, 600, 1024));

            lock (this.lockObject)
            {
                this.shellStream = stream;
            }

            return stream;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="SshConnection"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    lock (this.lockObject)
                    {
                        this.CleanupShellStream();
                        this.CleanupSshClient();
                        this.CleanupSftpClient();
                    }
                }

                this.disposed = true;
            }
        }

        private async Task<ISshClient> ConnectSshClientAsync(CancellationToken cancellationToken)
        {
            SshClient client;

            if (!string.IsNullOrEmpty(this.privateKeyPath))
            {
                // SSH key authentication
                var privateKey = new PrivateKeyFile(this.privateKeyPath);
                var connectionInfo = new ConnectionInfo(
                    this.hostAddress,
                    this.port,
                    Environment.UserName,
                    new PrivateKeyAuthenticationMethod(Environment.UserName, privateKey));
                client = new SshClient(connectionInfo);
            }
            else
            {
                // Password authentication
                client = new SshClient(this.hostAddress, this.port, this.username!, this.password!);
            }

            client.KeepAliveInterval = TimeSpan.FromSeconds(10);

            lock (this.lockObject)
            {
                this.sshClient = client;
            }

            if (!client.IsConnected)
            {
                AnsiConsole.Write(new ConnectionStatusRenderable(this.hostAddress, ConnectionStatus.Connecting));
                AnsiConsole.WriteLine();
                await client.ConnectAsync(cancellationToken);
                AnsiConsole.Write(new ConnectionStatusRenderable(this.hostAddress, ConnectionStatus.Connected));
                AnsiConsole.WriteLine();
            }

            return client;
        }

        private async Task<ISftpClient> ConnectSftpClientAsync(CancellationToken cancellationToken)
        {
            SftpClient client;

            if (!string.IsNullOrEmpty(this.privateKeyPath))
            {
                // SSH key authentication
                var privateKey = new PrivateKeyFile(this.privateKeyPath);
                var connectionInfo = new ConnectionInfo(
                    this.hostAddress,
                    this.port,
                    Environment.UserName,
                    new PrivateKeyAuthenticationMethod(Environment.UserName, privateKey));
                client = new SftpClient(connectionInfo);
            }
            else
            {
                // Password authentication
                client = new SftpClient(this.hostAddress, this.port, this.username!, this.password!);
            }

            lock (this.lockObject)
            {
                this.sftpClient = client;
            }

            if (!client.IsConnected)
            {
                AnsiConsole.Write(new ConnectionStatusRenderable(this.hostAddress, ConnectionStatus.Connecting));
                AnsiConsole.WriteLine();
                await client.ConnectAsync(cancellationToken);
                AnsiConsole.Write(new ConnectionStatusRenderable(this.hostAddress, ConnectionStatus.Connected));
                AnsiConsole.WriteLine();
            }

            return client;
        }

        private void CleanupShellStream()
        {
            if (this.shellStream != null)
            {
                try
                {
                    this.shellStream.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }

                this.shellStream = null;
            }
        }

        private void CleanupSshClient()
        {
            if (this.sshClient != null)
            {
                this.CleanupShellStream();

                try
                {
                    if (this.sshClient.IsConnected)
                    {
                        this.sshClient.Disconnect();
                    }

                    this.sshClient.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }

                this.sshClient = null;
            }
        }

        private void CleanupSftpClient()
        {
            if (this.sftpClient != null)
            {
                try
                {
                    if (this.sftpClient.IsConnected)
                    {
                        this.sftpClient.Disconnect();
                    }

                    this.sftpClient.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }

                this.sftpClient = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(SshConnection));
            }
        }
    }
}
