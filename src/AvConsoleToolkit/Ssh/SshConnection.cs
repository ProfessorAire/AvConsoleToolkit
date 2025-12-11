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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AvConsoleToolkit.Ssh
{
    /// <summary>
    /// Implements SSH connection with lazy initialization and automatic recovery.
    /// </summary>
    public class SshConnection : ISshConnection
    {
        private readonly string hostAddress;

        private readonly Lock lockObject = new();

        private readonly string? password;

        private readonly int port;

        private readonly string? privateKeyPath;

        private readonly string? username;

        private readonly bool verbose;

        private bool disposed;

        private bool isReconnecting;

        private Task? reconnectionTask;

        private SftpClient? sftpClient;

        private bool sftpClientNeeded;

        private ShellStream? shellStream;

        private SshClient? sshClient;

        private bool sshClientNeeded;

        private bool wasSftpConnected;

        private bool wasSshConnected;

        /// <summary>
        /// Initializes a new instance of the <see cref="SshConnection"/> class with password authentication.
        /// </summary>
        /// <param name="hostAddress">The host address to connect to.</param>
        /// <param name="port">The port to connect on.</param>
        /// <param name="username">The username for authentication.</param>
        /// <param name="password">The password for authentication.</param>
        /// <param name="verbose">A value indicating whether the connection should write verbose messages.</param>
        public SshConnection(string hostAddress, int port, string username, string password, bool verbose = false)
        {
            this.hostAddress = hostAddress ?? throw new ArgumentNullException(nameof(hostAddress));
            this.port = port;
            this.username = username ?? throw new ArgumentNullException(nameof(username));
            this.password = password ?? throw new ArgumentNullException(nameof(password));
            this.privateKeyPath = null;
            this.verbose = verbose;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SshConnection"/> class with SSH key authentication.
        /// </summary>
        /// <param name="hostAddress">The host address to connect to.</param>
        /// <param name="port">The port to connect on.</param>
        /// <param name="username">The username for authentication.</param>
        /// <param name="privateKeyPath">The path to the private key file.</param>
        /// <param name="usePrivateKey">Must be true to use this constructor for private key authentication.</param>
        /// <param name="verbose">A value indicating whether the connection should write verbose messages.</param>
        public SshConnection(string hostAddress, int port, string username, string privateKeyPath, bool usePrivateKey, bool verbose = false)
        {
            if (!usePrivateKey)
            {
                throw new ArgumentException("This constructor is for private key authentication only. Set usePrivateKey to true.", nameof(usePrivateKey));
            }

            this.hostAddress = hostAddress ?? throw new ArgumentNullException(nameof(hostAddress));
            this.port = port;
            this.username = username ?? throw new ArgumentNullException(nameof(username));
            this.privateKeyPath = privateKeyPath ?? throw new ArgumentNullException(nameof(privateKeyPath));
            this.password = null;
            this.verbose = verbose;
        }

        public event EventHandler? FileTransferDisconnected;

        public event EventHandler? FileTransferReconnected;

        public event EventHandler? ShellDisconnected;

        public event EventHandler? ShellReconnected;

        public bool DataAvailable
        {
            get
            {
                lock (this.lockObject)
                {
                    return this.shellStream?.DataAvailable ?? false;
                }
            }
        }

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

        /// <summary>
        /// Gets or sets the maximum number of reconnection attempts.
        /// A value of 0 means no automatic reconnection (default).
        /// A value of -1 means unlimited reconnection attempts.
        /// </summary>
        public int MaxReconnectionAttempts { get; set; } = 0;

        public async Task<bool> ConnectFileTransferAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await this.EnsureSftpClientAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.ConnectionFailed));
                AnsiConsole.WriteLine();
                if (this.verbose)
                {
                    AnsiConsole.WriteException(ex);
                }

                if (this.MaxReconnectionAttempts == -1 || this.MaxReconnectionAttempts > 0)
                {
                    this.StartReconnection();
                    if (this.reconnectionTask != null)
                    {
                        await this.reconnectionTask;
                    }

                    return this.sftpClient?.IsConnected ?? false;
                }
            }

            return false;
        }

        public async Task<bool> ConnectShellAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await this.EnsureShellStreamAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.ConnectionFailed));
                AnsiConsole.WriteLine();
                if (this.verbose)
                {
                    AnsiConsole.WriteException(ex);
                }

                if (this.MaxReconnectionAttempts == -1 || this.MaxReconnectionAttempts > 0)
                {
                    this.StartReconnection();
                    if (this.reconnectionTask != null)
                    {
                        await this.reconnectionTask;
                    }

                    return this.sshClient?.IsConnected ?? false;
                }
            }

            return false;
        }

        public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            await Task.Run(() => client.CreateDirectory(path), cancellationToken);
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async Task DownloadFileAsync(string remotePath, Stream destination, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            await Task.Run(() => client.DownloadFile(remotePath, destination), cancellationToken);
        }

        public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            return await Task.Run(() => client.Exists(path), cancellationToken);
        }

        public async Task<IEnumerable<ISftpFile>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            return await Task.Run(() => client.ListDirectory(path).ToList(), cancellationToken);
        }

        public async Task<string> ReadAsync(CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("ReadAsync");
            var stream = await this.EnsureShellStreamAsync(cancellationToken);
            return await Task.Run(() => stream.Read(), cancellationToken);
        }

        public async Task SetLastWriteTimeUtcAsync(string remotePath, DateTime lastWriteTime, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            await Task.Run(() => client.SetLastWriteTimeUtc(remotePath, lastWriteTime), cancellationToken);
        }

        public async Task UploadFileAsync(Stream source, string remotePath, bool canOverride, Action<ulong>? uploadCallback = null, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            await Task.Run(() => client.UploadFile(source, remotePath, canOverride, uploadCallback), cancellationToken);
        }

        public async Task<bool> WaitForCommandCompletionAsync(
            IEnumerable<string>? successPatterns,
            IEnumerable<string>? failurePatterns,
            CancellationToken cancellationToken,
            int timeoutMs = 15000,
            bool writeReceivedData = true)
        {
            var stream = await this.EnsureShellStreamAsync(cancellationToken);
            var output = new StringBuilder();
            var startTime = DateTime.UtcNow;

            while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stream.DataAvailable)
                {
                    var data = stream.Read();
                    output.Append(data);

                    if (writeReceivedData)
                    {
                        // Print output as it's received
                        AnsiConsole.Write(data);
                    }

                    var currentOutput = output.ToString();

                    // Check for failure patterns first
                    if (failurePatterns != null)
                    {
                        foreach (var pattern in failurePatterns)
                        {
                            if (currentOutput.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }
                        }
                    }

                    // Check for success patterns
                    if (successPatterns != null)
                    {
                        foreach (var pattern in successPatterns)
                        {
                            if (currentOutput.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }

                await Task.Delay(100, cancellationToken);
            }

            return false;
        }

        public async Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            var stream = await this.EnsureShellStreamAsync(cancellationToken);
            await Task.Run(() => stream.WriteLine(line), cancellationToken);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    lock (this.lockObject)
                    {
                        this.disposed = true;
                        this.CleanupShellStream();
                        this.CleanupSshClient();
                        this.CleanupSftpClient();
                    }

                    // Wait for reconnection task to complete (with timeout)
                    this.reconnectionTask?.Wait(TimeSpan.FromSeconds(5));
                }

                this.disposed = true;
            }
        }

        private void CleanupSftpClient()
        {
            if (this.sftpClient != null)
            {
                try
                {
                    // Unsubscribe from error event
                    this.sftpClient.ErrorOccurred -= this.OnSftpClientError;

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
                    // Unsubscribe from error event
                    this.sshClient.ErrorOccurred -= this.OnSshClientError;

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

        private async Task<SftpClient> ConnectSftpClientAsync(CancellationToken cancellationToken)
        {
            SftpClient client;

            if (!string.IsNullOrEmpty(this.privateKeyPath) && !string.IsNullOrEmpty(this.username))
            {
                var privateKey = new PrivateKeyFile(this.privateKeyPath);
                var connectionInfo = new ConnectionInfo(
                    this.hostAddress,
                    this.port,
                    this.username,
                    new PrivateKeyAuthenticationMethod(this.username, privateKey));
                client = new SftpClient(connectionInfo);
            }
            else if (this.username != null && this.password != null)
            {
                client = new SftpClient(this.hostAddress, this.port, this.username, this.password);
            }
            else
            {
                throw new InvalidOperationException("Either privateKeyPath or username/password must be provided");
            }

            // Subscribe to error event to detect disconnections
            client.ErrorOccurred += this.OnSftpClientError;

            lock (this.lockObject)
            {
                this.sftpClient = client;
            }

            if (!client.IsConnected)
            {
                // Only write "Connecting..." if not in reconnection mode
                // (reconnection status is already written by StartReconnection)
                if (!this.isReconnecting)
                {
                    AnsiConsole.Write(new ConnectionStatusRenderable("SFTP", this.hostAddress, ConnectionStatus.Connecting));
                    AnsiConsole.WriteLine();
                }

                await client.ConnectAsync(cancellationToken);

                // Only write "Connected" if not in reconnection mode
                // (reconnection will write its own success message)
                if (!this.isReconnecting)
                {
                    AnsiConsole.Write(new ConnectionStatusRenderable("SFTP", this.hostAddress, ConnectionStatus.Connected));
                    AnsiConsole.WriteLine();
                }

                this.wasSftpConnected = true;
            }

            return client;
        }

        private async Task<SshClient> ConnectSshClientAsync(CancellationToken cancellationToken)
        {
            SshClient client;

            if (!string.IsNullOrEmpty(this.privateKeyPath) && !string.IsNullOrEmpty(this.username))
            {
                var privateKey = new PrivateKeyFile(this.privateKeyPath);
                var connectionInfo = new ConnectionInfo(
                    this.hostAddress,
                    this.port,
                    this.username,
                    new PrivateKeyAuthenticationMethod(this.username, privateKey));
                client = new SshClient(connectionInfo);
            }
            else if (this.username != null && this.password != null)
            {
                client = new SshClient(this.hostAddress, this.port, this.username, this.password);
            }
            else
            {
                throw new InvalidOperationException("Either privateKeyPath or username/password must be provided");
            }

            client.KeepAliveInterval = TimeSpan.FromSeconds(3);

            // Subscribe to error event to detect disconnections
            client.ErrorOccurred += this.OnSshClientError;

            lock (this.lockObject)
            {
                this.sshClient = client;
            }

            if (!client.IsConnected)
            {
                // Only write "Connecting..." if not in reconnection mode
                // (reconnection status is already written by StartReconnection)
                if (!this.isReconnecting)
                {
                    AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.Connecting));
                    AnsiConsole.WriteLine();
                }

                await client.ConnectAsync(cancellationToken);

                // Only write "Connected" if not in reconnection mode
                // (reconnection will write its own success message)
                if (!this.isReconnecting)
                {
                    AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.Connected));
                    AnsiConsole.WriteLine();
                }

                this.wasSshConnected = true;
            }

            return client;
        }

        private async Task<SftpClient> EnsureSftpClientAsync(CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            this.sftpClientNeeded = true;
            var wasDisconnected = false;

            lock (this.lockObject)
            {
                if (this.sftpClient != null && this.sftpClient.IsConnected)
                {
                    return this.sftpClient;
                }

                if (this.sftpClient != null && !this.sftpClient.IsConnected)
                {
                    wasDisconnected = true;

                    // Only write disconnection status if not already reconnecting
                    // (ErrorOccurred handler already wrote the status)
                    if (!this.isReconnecting)
                    {
                        AnsiConsole.Write(new ConnectionStatusRenderable("SFTP", this.hostAddress, this.wasSftpConnected ? ConnectionStatus.LostConnection : ConnectionStatus.ConnectionFailed));
                        AnsiConsole.WriteLine();
                    }

                    this.CleanupSftpClient();

                    if (!this.isReconnecting)
                    {
                        this.FileTransferDisconnected?.Invoke(this, EventArgs.Empty);
                    }
                }
            }

            var client = await this.ConnectSftpClientAsync(cancellationToken);

            if (wasDisconnected && !this.isReconnecting)
            {
                this.FileTransferReconnected?.Invoke(this, EventArgs.Empty);
            }

            return client;
        }

        private async Task<ShellStream> EnsureShellStreamAsync(CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            this.sshClientNeeded = true;
            var wasReconnecting = false;

            lock (this.lockObject)
            {
                if (this.shellStream != null && this.shellStream.CanWrite)
                {
                    return this.shellStream;
                }

                if (this.shellStream != null && !this.shellStream.CanWrite)
                {
                    // Only write disconnection status if not already reconnecting
                    if (!this.isReconnecting)
                    {
                        AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.LostConnection));
                        AnsiConsole.WriteLine();
                    }

                    this.CleanupShellStream();
                    wasReconnecting = true;
                }
            }

            // Fire disconnection event only if not already reconnecting
            if (!this.isReconnecting && wasReconnecting)
            {
                this.ShellDisconnected?.Invoke(this, EventArgs.Empty);
            }

            var client = await this.EnsureSshClientAsync(cancellationToken);

            if (!client.IsConnected)
            {
                // Only write status if not in reconnection mode
                if (!this.isReconnecting)
                {
                    if (wasReconnecting)
                    {
                        AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.Reconnecting));
                        AnsiConsole.WriteLine();
                    }
                    else
                    {
                        AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.Connecting));
                        AnsiConsole.WriteLine();
                    }
                }

                await client.ConnectAsync(cancellationToken);

                // Only write success status if not in reconnection mode
                if (!this.isReconnecting)
                {
                    AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.Connected));
                    AnsiConsole.WriteLine();
                }
            }

            var stream = client.CreateShellStream("xterm", 80, 24, 800, 600, 1024);

            lock (this.lockObject)
            {
                this.shellStream = stream;
            }

            if (wasReconnecting && !this.isReconnecting)
            {
                Debug.WriteLine("Ensure Shell Stream");
                this.ShellReconnected?.Invoke(this, EventArgs.Empty);
            }

            return stream;
        }

        private async Task<SshClient> EnsureSshClientAsync(CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();

            var wasDisconnected = false;

            lock (this.lockObject)
            {
                if (this.sshClient != null && this.sshClient.IsConnected)
                {
                    return this.sshClient;
                }

                if (this.sshClient != null && !this.sshClient.IsConnected)
                {
                    wasDisconnected = true;

                    // Only write disconnection status if not already reconnecting
                    // (ErrorOccurred handler already wrote the status)
                    if (!this.isReconnecting)
                    {
                        AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, this.wasSshConnected ? ConnectionStatus.LostConnection : ConnectionStatus.ConnectionFailed));
                        AnsiConsole.WriteLine();
                    }

                    this.CleanupSshClient();
                }
            }

            var client = await this.ConnectSshClientAsync(cancellationToken);

            if (wasDisconnected && !this.isReconnecting)
            {
                this.ShellReconnected?.Invoke(this, EventArgs.Empty);
            }

            return client;
        }

        private void OnSftpClientError(object? sender, Renci.SshNet.Common.ExceptionEventArgs e)
        {
            lock (this.lockObject)
            {
                if (this.disposed || this.sftpClient == null)
                {
                    return;
                }

                // Write disconnection status
                AnsiConsole.Write(new ConnectionStatusRenderable("SFTP", this.hostAddress, ConnectionStatus.LostConnection));
                AnsiConsole.WriteLine();

                // Unsubscribe from the event before cleanup
                this.sftpClient.ErrorOccurred -= this.OnSftpClientError;
            }

            // Fire disconnection event
            this.FileTransferDisconnected?.Invoke(this, EventArgs.Empty);

            lock (this.lockObject)
            {
                // Start automatic reconnection
                this.StartReconnection();
            }
        }

        private void OnSshClientError(object? sender, Renci.SshNet.Common.ExceptionEventArgs e)
        {
            lock (this.lockObject)
            {
                if (this.disposed || this.sshClient == null)
                {
                    return;
                }

                // Write disconnection status
                try
                {
                    AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.LostConnection));
                    AnsiConsole.WriteLine();
                }
                catch
                {
                }

                // Clean up the shell stream and clients
                this.CleanupShellStream();

                // Unsubscribe from the event before cleanup
                this.sshClient.ErrorOccurred -= this.OnSshClientError;
            }

            // Fire disconnection event
            this.ShellDisconnected?.Invoke(this, EventArgs.Empty);

            // Start automatic reconnection
            lock (this.lockObject)
            {
                this.StartReconnection();
            }
        }

        private void StartReconnection()
        {
            // Only start one reconnection task at a time

            if (this.isReconnecting || this.disposed)
            {
                return;
            }

            // Check if automatic reconnection is disabled
            if (this.MaxReconnectionAttempts == 0)
            {
                return;
            }

            this.isReconnecting = true;

            // Start reconnection task in the background
            this.reconnectionTask = Task.Run(async () =>
            {
                var attemptCount = 0;
                int maxAttempts = this.MaxReconnectionAttempts;
                bool isInfiniteAttempts = maxAttempts < 0;
                int[] backoffDelays = [1000, 1000, 2000, 3000, 5000, 5000, 10000];

                while ((isInfiniteAttempts || attemptCount < maxAttempts) && !this.disposed)
                {
                    try
                    {
                        attemptCount++;

                        // Write reconnecting status with attempt information
                        if (this.sshClientNeeded)
                        {
                            var status = this.wasSshConnected ? ConnectionStatus.Reconnecting : ConnectionStatus.Connecting;
                            AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, status, attemptCount, isInfiniteAttempts ? -1 : maxAttempts));
                            AnsiConsole.WriteLine();
                        }

                        if (this.sftpClientNeeded)
                        {
                            var status = this.wasSftpConnected ? ConnectionStatus.Reconnecting : ConnectionStatus.Connecting;
                            AnsiConsole.Write(new ConnectionStatusRenderable("SFTP", this.hostAddress, status, attemptCount, isInfiniteAttempts ? -1 : maxAttempts));
                            AnsiConsole.WriteLine();
                        }

                        // Wait before attempting reconnection (exponential backoff)
                        // Skip delay on first attempt for immediate reconnection
                        if (attemptCount > 1)
                        {
                            int delayIndex = Math.Min(attemptCount - 2, backoffDelays.Length - 1);
                            await Task.Delay(backoffDelays[delayIndex]);
                        }

                        // Attempt to reconnect
                        var connectionSucceeded = false;

                        // Reconnect SSH client if it was previously connected
                        if (this.sshClientNeeded)
                        {
                            try
                            {
                                await this.EnsureSshClientAsync(CancellationToken.None);
                                connectionSucceeded = true;
                            }
                            catch
                            {
                                // Write connection failed status
                                AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.ConnectionFailed));
                                AnsiConsole.WriteLine();
                                continue;
                            }
                        }

                        // Reconnect SFTP client if it was previously connected
                        if (this.sftpClientNeeded)
                        {
                            try
                            {
                                await this.EnsureSftpClientAsync(CancellationToken.None);
                                connectionSucceeded = true;
                            }
                            catch
                            {
                                // Write connection failed status
                                AnsiConsole.Write(new ConnectionStatusRenderable("SFTP", this.hostAddress, ConnectionStatus.ConnectionFailed));
                                AnsiConsole.WriteLine();
                                continue;
                            }
                        }

                        // If we got here, reconnection was successful
                        if (connectionSucceeded)
                        {
                            // Write success status
                            AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.Connected));
                            AnsiConsole.WriteLine();

                            // Fire reconnected events
                            if (this.sshClientNeeded)
                            {
                                this.ShellReconnected?.Invoke(this, EventArgs.Empty);
                            }

                            if (this.sftpClientNeeded)
                            {
                                this.FileTransferReconnected?.Invoke(this, EventArgs.Empty);
                            }

                            lock (this.lockObject)
                            {
                                this.isReconnecting = false;
                            }
                            return;
                        }
                    }
                    catch
                    {
                        // Write connection failed status
                        if (this.sshClientNeeded)
                        {
                            AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.ConnectionFailed));
                            AnsiConsole.WriteLine();
                        }

                        if (this.sftpClientNeeded)
                        {
                            AnsiConsole.Write(new ConnectionStatusRenderable("SFTP", this.hostAddress, ConnectionStatus.ConnectionFailed));
                            AnsiConsole.WriteLine();
                        }
                    }
                }

                // All reconnection attempts failed
                lock (this.lockObject)
                {
                    this.isReconnecting = false;
                }

                AnsiConsole.MarkupLine($"[red]Failed to connect to {this.hostAddress} after {maxAttempts} attempts[/]");
            });
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
