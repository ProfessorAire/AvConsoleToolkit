// <copyright file="SshConnection.cs">
// The MIT License
// Copyright © Christopher McNeely
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;
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
        private ShellStream? shellStream;
        private bool disposed;

        public event EventHandler? ShellDisconnected;
        public event EventHandler? ShellReconnected;
        public event EventHandler? FileTransferDisconnected;
        public event EventHandler? FileTransferReconnected;

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
        /// <param name="username">The username for authentication.</param>
        /// <param name="privateKeyPath">The path to the private key file.</param>
        /// <param name="usePrivateKey">Must be true to use this constructor for private key authentication.</param>
        public SshConnection(string hostAddress, int port, string username, string privateKeyPath, bool usePrivateKey)
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

        public async Task ConnectShellAsync(CancellationToken cancellationToken = default)
        {
            await this.EnsureShellStreamAsync(cancellationToken);
        }

        public async Task ConnectFileTransferAsync(CancellationToken cancellationToken = default)
        {
            await this.EnsureSftpClientAsync(cancellationToken);
        }

        public async Task<string> ReadAsync(CancellationToken cancellationToken = default)
        {
            var stream = await this.EnsureShellStreamAsync(cancellationToken);
            return await Task.Run(() => stream.Read(), cancellationToken);
        }

        public async Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            var stream = await this.EnsureShellStreamAsync(cancellationToken);
            await Task.Run(() => stream.WriteLine(line), cancellationToken);
        }

        public async Task<bool> WaitForCommandCompletionAsync(
            IEnumerable<string>? successPatterns,
            IEnumerable<string>? failurePatterns,
            CancellationToken cancellationToken,
            int timeoutMs = 15000,
            bool writeReceivedData = true)
        {
            var stream = await this.EnsureShellStreamAsync(cancellationToken);
            var wrapper = new ShellStreamWrapper(stream);
            return await wrapper.WaitForCommandCompletionAsync(successPatterns, failurePatterns, cancellationToken, timeoutMs, writeReceivedData);
        }

        public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            return await Task.Run(() => client.Exists(path), cancellationToken);
        }

        public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            await Task.Run(() => client.CreateDirectory(path), cancellationToken);
        }

        public async Task<IEnumerable<ISftpFile>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            return await Task.Run(() => client.ListDirectory(path).ToList(), cancellationToken);
        }

        public async Task DownloadFileAsync(string remotePath, Stream destination, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            await Task.Run(() => client.DownloadFile(remotePath, destination), cancellationToken);
        }

        public async Task UploadFileAsync(Stream source, string remotePath, bool canOverride, Action<ulong>? uploadCallback = null, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            await Task.Run(() => client.UploadFile(source, remotePath, canOverride, uploadCallback), cancellationToken);
        }

        public async Task SetLastWriteTimeUtcAsync(string remotePath, DateTime lastWriteTime, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            await Task.Run(() => client.SetLastWriteTimeUtc(remotePath, lastWriteTime), cancellationToken);
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

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

        private async Task<SshClient> EnsureSshClientAsync(CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();

            bool wasDisconnected = false;

            lock (this.lockObject)
            {
                if (this.sshClient != null && this.sshClient.IsConnected)
                {
                    return this.sshClient;
                }

                if (this.sshClient != null && !this.sshClient.IsConnected)
                {
                    wasDisconnected = true;
                    this.CleanupSshClient();
                    this.ShellDisconnected?.Invoke(this, EventArgs.Empty);
                }
            }

            var client = await this.ConnectSshClientAsync(cancellationToken);

            if (wasDisconnected)
            {
                this.ShellReconnected?.Invoke(this, EventArgs.Empty);
            }

            return client;
        }

        private async Task<SftpClient> EnsureSftpClientAsync(CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();

            bool wasDisconnected = false;

            lock (this.lockObject)
            {
                if (this.sftpClient != null && this.sftpClient.IsConnected)
                {
                    return this.sftpClient;
                }

                if (this.sftpClient != null && !this.sftpClient.IsConnected)
                {
                    wasDisconnected = true;
                    this.CleanupSftpClient();
                    this.FileTransferDisconnected?.Invoke(this, EventArgs.Empty);
                }
            }

            var client = await this.ConnectSftpClientAsync(cancellationToken);

            if (wasDisconnected)
            {
                this.FileTransferReconnected?.Invoke(this, EventArgs.Empty);
            }

            return client;
        }

        private async Task<ShellStream> EnsureShellStreamAsync(CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();

            bool wasReconnecting = false;

            lock (this.lockObject)
            {
                if (this.shellStream != null && this.shellStream.CanRead)
                {
                    return this.shellStream;
                }

                if (this.shellStream != null && !this.shellStream.CanRead)
                {
                    this.CleanupShellStream();
                    wasReconnecting = true;
                }
            }

            var client = await this.EnsureSshClientAsync(cancellationToken);
            
            if (!client.IsConnected)
            {
                AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.Connecting));
                AnsiConsole.WriteLine();
                await client.ConnectAsync(cancellationToken);
                AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.Connected));
                AnsiConsole.WriteLine();
            }

            var stream = client.CreateShellStream("xterm", 80, 24, 800, 600, 1024);

            lock (this.lockObject)
            {
                this.shellStream = stream;
            }

            if (wasReconnecting)
            {
                this.ShellReconnected?.Invoke(this, EventArgs.Empty);
            }

            return stream;
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

            client.KeepAliveInterval = TimeSpan.FromSeconds(10);

            lock (this.lockObject)
            {
                this.sshClient = client;
            }

            if (!client.IsConnected)
            {
                AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.Connecting));
                AnsiConsole.WriteLine();
                await client.ConnectAsync(cancellationToken);
                AnsiConsole.Write(new ConnectionStatusRenderable("SSH", this.hostAddress, ConnectionStatus.Connected));
                AnsiConsole.WriteLine();
            }

            return client;
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

            lock (this.lockObject)
            {
                this.sftpClient = client;
            }

            if (!client.IsConnected)
            {
                AnsiConsole.Write(new ConnectionStatusRenderable("SFTP", this.hostAddress, ConnectionStatus.Connecting));
                AnsiConsole.WriteLine();
                await client.ConnectAsync(cancellationToken);
                AnsiConsole.Write(new ConnectionStatusRenderable("SFTP", this.hostAddress, ConnectionStatus.Connected));
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
