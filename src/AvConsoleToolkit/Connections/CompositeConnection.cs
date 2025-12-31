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
using AvConsoleToolkit.Utilities;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Spectre.Console;

namespace AvConsoleToolkit.Connections
{
    /// <summary>
    /// Implements SSH and SFTP connection with lazy initialization and automatic recovery.
    /// </summary>
    public class CompositeConnection : ICompositeConnection
    {
        private readonly string hostAddress;

        private readonly Lock lockObject = new();

        private readonly string? password;

        private readonly int port;

        private readonly string? privateKeyPath;

        private readonly Connections.ConnectionStatusModel statusModel;

        private readonly string? username;

        private readonly bool verbose;

        private bool disposed;

        private bool isReconnecting;

        private CancellationTokenSource? liveStatusCts;

        private Task? liveStatusTask;

        private Task? reconnectionTask;

        private SftpClient? sftpClient;

        private bool sftpClientNeeded;

        private ShellStream? shellStream;

        private int spinnerIndex = 0;

        private SshClient? sshClient;

        private bool sshClientNeeded;

        private bool wasSftpConnected;

        private bool wasSshConnected;

        /// <summary>
        /// Initializes a new instance of the <see cref="CompositeConnection"/> class with password authentication.
        /// </summary>
        /// <param name="hostAddress">The host address to connect to.</param>
        /// <param name="port">The port to connect on.</param>
        /// <param name="username">The username for authentication.</param>
        /// <param name="password">The password for authentication.</param>
        /// <param name="verbose">A value indicating whether the connection should write verbose messages.</param>
        public CompositeConnection(string hostAddress, int port, string username, string password, bool verbose = false)
        {
            this.hostAddress = hostAddress ?? throw new ArgumentNullException(nameof(hostAddress));
            this.port = port;
            this.username = username ?? throw new ArgumentNullException(nameof(username));
            this.password = password ?? throw new ArgumentNullException(nameof(password));
            this.privateKeyPath = null;
            this.verbose = verbose;
            this.statusModel = new Connections.ConnectionStatusModel
            {
                HostAddress = hostAddress,
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompositeConnection"/> class with SSH key authentication.
        /// </summary>
        /// <param name="hostAddress">The host address to connect to.</param>
        /// <param name="port">The port to connect on.</param>
        /// <param name="username">The username for authentication.</param>
        /// <param name="privateKeyPath">The path to the private key file.</param>
        /// <param name="usePrivateKey">Must be true to use this constructor for private key authentication.</param>
        /// <param name="verbose">A value indicating whether the connection should write verbose messages.</param>
        public CompositeConnection(string hostAddress, int port, string username, string privateKeyPath, bool usePrivateKey, bool verbose = false)
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
            this.statusModel = new Connections.ConnectionStatusModel
            {
                HostAddress = hostAddress,
            };
        }

        public event EventHandler? FileTransferDisconnected;

        public event EventHandler? FileTransferReconnected;

        public event EventHandler? ShellDisconnected;

        public event EventHandler? ShellReconnected;

        public event EventHandler<Connections.ConnectionStatusModel>? StatusChanged;

        public event Action<Connections.ConnectionStatus> SshConnectionStatusChanged
        {
            add => this.statusModel.SshStateChanged += value;
            remove => this.statusModel.SshStateChanged -= value;
        }

        public event Action<Connections.ConnectionStatus> FileTransferConnectionStatusChanged
        {
            add => this.statusModel.SftpStateChanged += value;
            remove => this.statusModel.SftpStateChanged -= value;
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

        /// <inheritdoc/>
        public bool IsFileTransferConnected
        {
            get
            {
                lock (this.lockObject)
                {
                    return this.sftpClient?.IsConnected ?? false;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsShellConnected
        {
            get
            {
                lock (this.lockObject)
                {
                    return this.sshClient?.IsConnected ?? false;
                }
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of reconnection attempts.
        /// A value of 0 means no automatic reconnection (default).
        /// A value of -1 means unlimited reconnection attempts.
        /// </summary>
        public int MaxReconnectionAttempts { get; set; } = 0;

        /// <inheritdoc/>
        public bool SuppressOutput { get; set; }

        public async Task<bool> ConnectFileTransferAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                this.StartLiveStatusAsync(cancellationToken);
                await this.EnsureSftpClientAsync(cancellationToken);
                return this.sftpClient?.IsConnected ?? false;
            }
            catch (Exception ex)
            {
                this.UpdateSftpStatus(Connections.ConnectionStatus.ConnectionFailed);
                if (this.verbose && !this.SuppressOutput)
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
            finally
            {
                this.StopLiveStatus();
            }

            return false;
        }

        public async Task<bool> ConnectShellAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                this.StartLiveStatusAsync(cancellationToken);
                await this.EnsureShellStreamAsync(cancellationToken);
                return this.sshClient?.IsConnected ?? false;
            }
            catch (Exception ex)
            {
                this.UpdateSshStatus(Connections.ConnectionStatus.ConnectionFailed);
                if (this.verbose && !this.SuppressOutput)
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
            finally
            {
                this.StopLiveStatus();
            }

            return false;
        }

        public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);
            await Task.Run(() => client.CreateDirectory(path), cancellationToken);
        }

        public async Task<int> DeleteFilesByGlobAsync(string pattern, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);

            // Get matching files
            var matchingFiles = await this.ListFilesByGlobAsync(pattern, cancellationToken);
            var filesArray = matchingFiles.ToArray();

            if (filesArray.Length == 0)
            {
                return 0;
            }

            // Delete each file
            foreach (var file in filesArray)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Run(() => client.DeleteFile(file.FullName), cancellationToken);
            }

            return filesArray.Length;
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

        public async Task<int> DownloadFilesByGlobAsync(string pattern, string localDirectory, bool preserveStructure = true, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);

            // Ensure local directory exists
            Directory.CreateDirectory(localDirectory);

            // Get matching files
            var matchingFiles = await this.ListFilesByGlobAsync(pattern, cancellationToken);
            var filesArray = matchingFiles.ToArray();

            if (filesArray.Length == 0)
            {
                return 0;
            }

            // Determine base path for structure preservation
            var baseRemotePath = this.GetBasePathFromPattern(pattern);

            // Download each file
            foreach (var file in filesArray)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string localPath;
                if (preserveStructure && !string.IsNullOrEmpty(baseRemotePath))
                {
                    // Preserve directory structure
                    var relativePath = file.FullName.StartsWith(baseRemotePath)
                        ? file.FullName[baseRemotePath.Length..].TrimStart('/')
                        : Path.GetFileName(file.Name);

                    localPath = Path.Combine(localDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

                    // Ensure subdirectory exists
                    var localDir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(localDir))
                    {
                        Directory.CreateDirectory(localDir);
                    }
                }
                else
                {
                    // Flatten structure - just use filename
                    localPath = Path.Combine(localDirectory, file.Name);
                }

                // Download the file
                using var fileStream = File.Create(localPath);
                await Task.Run(() => client.DownloadFile(file.FullName, fileStream), cancellationToken);

                // Preserve timestamp
                File.SetLastWriteTimeUtc(localPath, file.LastWriteTimeUtc);
            }

            return filesArray.Length;
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

        public async Task<IEnumerable<ISftpFile>> ListFilesByGlobAsync(string pattern, CancellationToken cancellationToken = default)
        {
            var client = await this.EnsureSftpClientAsync(cancellationToken);

            // Normalize pattern
            pattern = pattern.Replace('\\', '/');

            // Extract the base directory from the pattern
            var lastSlash = pattern.LastIndexOf('/');
            string baseDirectory;
            string filePattern;

            if (lastSlash >= 0)
            {
                baseDirectory = lastSlash > 0 ? pattern[..lastSlash] : ".";
                filePattern = pattern[(lastSlash + 1)..];
            }
            else
            {
                baseDirectory = ".";
                filePattern = pattern;
            }

            // Check if pattern contains ** (recursive wildcard)
            var isRecursive = pattern.Contains("**");

            // Get all files from base directory (recursively if needed)
            var allFiles = new List<ISftpFile>();

            if (isRecursive)
            {
                // For recursive patterns, start from the first directory before **
                var doubleStarIndex = pattern.IndexOf("**", StringComparison.Ordinal);
                var recursiveBase = doubleStarIndex > 0 ? pattern[..pattern.LastIndexOf('/', doubleStarIndex - 1)] : ".";
                if (string.IsNullOrEmpty(recursiveBase))
                {
                    recursiveBase = ".";
                }

                await this.ListFilesRecursiveAsync(client, recursiveBase, allFiles, cancellationToken);
            }
            else
            {
                // Non-recursive: just list the base directory
                var items = await Task.Run(() => client.ListDirectory(baseDirectory), cancellationToken);
                allFiles.AddRange(items.Where(f => !f.IsDirectory && f.Name != "." && f.Name != ".."));
            }

            // Filter files by glob pattern
            return allFiles.Where(file => GlobMatcher.IsMatch(pattern, file.FullName)).ToList();
        }

        public async Task<string> ReadAsync(CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("ReadAsync");
            var stream = await this.EnsureShellStreamAsync(cancellationToken);
            return await Task.Run(() => stream.Read(), cancellationToken);
        }

        /// <summary>
        /// Starts a Spectre.Console live status display for this connection.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to stop the live display.</param>
        private void StartLiveStatusAsync(CancellationToken cancellationToken = default)
        {
            if (this.SuppressOutput)
            {
                return;
            }

            if (this.liveStatusTask != null && !this.liveStatusTask.IsCompleted)
            {
                return;
            }

            this.liveStatusCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = this.liveStatusCts.Token;

            this.liveStatusTask = Task.Run(async () =>
            {
                var renderable = new Connections.ConnectionStatusRenderable(this.statusModel, true, this.spinnerIndex);
                var live = AnsiConsole.Live(renderable)
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Crop)
                    .Cropping(VerticalOverflowCropping.Top);

                try
                {
                    await live.StartAsync(async ctx =>
                    {
                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                this.spinnerIndex++;
                                ctx.UpdateTarget(new Connections.ConnectionStatusRenderable(this.statusModel, true, this.spinnerIndex));
                                await Task.Delay(120, token);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            ctx.UpdateTarget(new Connections.ConnectionStatusRenderable(this.statusModel, false, this.spinnerIndex));
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
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
                    this.StopLiveStatus();
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

            client.ErrorOccurred += this.OnSftpClientError;

            lock (this.lockObject)
            {
                this.sftpClient = client;
            }

            if (!client.IsConnected)
            {
                if (!this.isReconnecting)
                {
                    this.UpdateSftpStatus(Connections.ConnectionStatus.Connecting);
                }

                await client.ConnectAsync(cancellationToken);
                this.UpdateSftpStatus(Connections.ConnectionStatus.Connected);
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
            client.ErrorOccurred += this.OnSshClientError;

            lock (this.lockObject)
            {
                this.sshClient = client;
            }

            if (!client.IsConnected)
            {
                if (!this.isReconnecting)
                {
                    this.UpdateSshStatus(Connections.ConnectionStatus.Connecting);
                }

                await client.ConnectAsync(cancellationToken);
                this.UpdateSshStatus(Connections.ConnectionStatus.Connected);
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

                    if (!this.isReconnecting)
                    {
                        this.UpdateSftpStatus(this.wasSftpConnected ? Connections.ConnectionStatus.LostConnection : Connections.ConnectionStatus.ConnectionFailed);
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
                    if (!this.isReconnecting)
                    {
                        this.UpdateSshStatus(Connections.ConnectionStatus.LostConnection);
                    }

                    this.CleanupShellStream();
                    wasReconnecting = true;
                }
            }

            if (!this.isReconnecting && wasReconnecting)
            {
                this.ShellDisconnected?.Invoke(this, EventArgs.Empty);
            }

            var client = await this.EnsureSshClientAsync(cancellationToken);

            if (!client.IsConnected)
            {
                if (!this.isReconnecting)
                {
                    this.UpdateSshStatus(wasReconnecting ? Connections.ConnectionStatus.Reconnecting : Connections.ConnectionStatus.Connecting);
                }

                await client.ConnectAsync(cancellationToken);

                if (!this.isReconnecting)
                {
                    this.UpdateSshStatus(Connections.ConnectionStatus.Connected);
                }
            }

            var stream = client.CreateShellStream("xterm", 80, 24, 800, 600, 1024);

            lock (this.lockObject)
            {
                this.shellStream = stream;
            }

            if (wasReconnecting && !this.isReconnecting)
            {
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

                    if (!this.isReconnecting)
                    {
                        this.UpdateSshStatus(this.wasSshConnected ? Connections.ConnectionStatus.LostConnection : Connections.ConnectionStatus.ConnectionFailed);
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

        /// <summary>
        /// Extracts the base path from a glob pattern (the directory part before any wildcards).
        /// </summary>
        private string GetBasePathFromPattern(string pattern)
        {
            pattern = pattern.Replace('\\', '/');

            // Find the first wildcard character
            var wildcardIndex = pattern.IndexOfAny(['*', '?', '[']);

            if (wildcardIndex == -1)
            {
                // No wildcards - return the directory portion
                var lastSlash = pattern.LastIndexOf('/');
                return lastSlash >= 0 ? pattern[..lastSlash] : string.Empty;
            }

            // Find the last / before the first wildcard
            var lastSlashBeforeWildcard = pattern.LastIndexOf('/', wildcardIndex);
            return lastSlashBeforeWildcard >= 0 ? pattern[..lastSlashBeforeWildcard] : string.Empty;
        }

        /// <summary>
        /// Recursively lists all files under a directory.
        /// </summary>
        private async Task ListFilesRecursiveAsync(SftpClient client, string path, List<ISftpFile> files, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var items = await Task.Run(() => client.ListDirectory(path), cancellationToken);

                foreach (var item in items)
                {
                    if (item.Name == "." || item.Name == "..")
                    {
                        continue;
                    }

                    if (item.IsDirectory)
                    {
                        await this.ListFilesRecursiveAsync(client, item.FullName, files, cancellationToken);
                    }
                    else
                    {
                        files.Add(item);
                    }
                }
            }
            catch
            {
                // Skip directories we can't access
            }
        }

        private void OnSftpClientError(object? sender, Renci.SshNet.Common.ExceptionEventArgs e)
        {
            lock (this.lockObject)
            {
                if (this.disposed || this.sftpClient == null)
                {
                    return;
                }

                this.UpdateSftpStatus(Connections.ConnectionStatus.LostConnection);
                this.sftpClient.ErrorOccurred -= this.OnSftpClientError;
            }

            this.FileTransferDisconnected?.Invoke(this, EventArgs.Empty);

            lock (this.lockObject)
            {
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

                try
                {
                    this.UpdateSshStatus(Connections.ConnectionStatus.LostConnection);
                }
                catch
                {
                }

                this.CleanupShellStream();
                this.sshClient.ErrorOccurred -= this.OnSshClientError;
            }

            this.ShellDisconnected?.Invoke(this, EventArgs.Empty);

            lock (this.lockObject)
            {
                this.StartReconnection();
            }
        }

        private void StartReconnection()
        {
            if (this.isReconnecting || this.disposed)
            {
                return;
            }

            if (this.MaxReconnectionAttempts == 0)
            {
                return;
            }

            this.isReconnecting = true;

            this.liveStatusCts?.Cancel(); // Ensure any previous live status is stopped

            this.liveStatusTask = null;
            this.spinnerIndex = 0;

            this.reconnectionTask = Task.Run(async () =>
            {
                var attemptCount = 0;
                int maxAttempts = this.MaxReconnectionAttempts;
                bool isInfiniteAttempts = maxAttempts < 0;
                int[] backoffDelays = [1000, 1000, 2000, 3000, 5000, 5000, 10000];

                // Start live status for reconnection
                this.StartLiveStatusAsync();

                while ((isInfiniteAttempts || attemptCount < maxAttempts) && !this.disposed)
                {
                    try
                    {
                        attemptCount++;

                        if (this.sshClientNeeded)
                        {
                            var status = this.wasSshConnected ? Connections.ConnectionStatus.Reconnecting : Connections.ConnectionStatus.Connecting;
                            this.UpdateSshStatus(status, attemptCount, maxAttempts);
                        }

                        if (this.sftpClientNeeded)
                        {
                            var status = this.wasSftpConnected ? Connections.ConnectionStatus.Reconnecting : Connections.ConnectionStatus.Connecting;
                            this.UpdateSftpStatus(status, attemptCount, maxAttempts);
                        }

                        if (attemptCount > 1)
                        {
                            int delayIndex = Math.Min(attemptCount - 2, backoffDelays.Length - 1);
                            await Task.Delay(backoffDelays[delayIndex]);
                        }

                        Task? sshTask = null;
                        Task? sftpTask = null;

                        if (this.sshClientNeeded)
                        {
                            sshTask = this.EnsureSshClientAsync(CancellationToken.None);
                        }

                        if (this.sftpClientNeeded)
                        {
                            sftpTask = this.EnsureSftpClientAsync(CancellationToken.None);
                        }

                        await Task.WhenAll(sshTask ?? Task.CompletedTask, sftpTask ?? Task.CompletedTask);
                        var fail = false;
                        if (this.sshClientNeeded && (sshTask?.IsFaulted ?? true))
                        {
                            this.UpdateSshStatus(Connections.ConnectionStatus.ConnectionFailed, attemptCount, maxAttempts);
                            fail = true;
                        }

                        if (this.sftpClientNeeded && (sftpTask?.IsFaulted ?? true))
                        {
                            this.UpdateSftpStatus(Connections.ConnectionStatus.ConnectionFailed, attemptCount, maxAttempts);
                            fail = true;
                        }

                        if (fail)
                        {
                            continue;
                        }
                        else
                        {
                            if (this.sshClientNeeded)
                            {
                                this.UpdateSshStatus(Connections.ConnectionStatus.Connected);
                            }

                            if (this.sftpClientNeeded)
                            {
                                this.UpdateSftpStatus(Connections.ConnectionStatus.Connected);
                            }

                            this.StopLiveStatus();

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
                        if (this.sshClientNeeded)
                        {
                            this.UpdateSshStatus(Connections.ConnectionStatus.ConnectionFailed, attemptCount, maxAttempts);
                        }

                        if (this.sftpClientNeeded)
                        {
                            this.UpdateSftpStatus(Connections.ConnectionStatus.ConnectionFailed, attemptCount, maxAttempts);
                        }
                    }
                }

                lock (this.lockObject)
                {
                    this.isReconnecting = false;
                }

                if (!this.SuppressOutput)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to connect to {this.hostAddress} after {maxAttempts} attempts.[/]");
                }
            });
        }

        /// <summary>
        /// Stops the live status display if running.
        /// </summary>
        private void StopLiveStatus()
        {
            if (this.liveStatusCts != null)
            {
                this.liveStatusCts.Cancel();
                this.liveStatusCts.Dispose();
                this.liveStatusCts = null;
            }
            if (this.liveStatusTask != null)
            {
                try
                {
                    this.liveStatusTask.Wait(500);
                }
                catch
                {
                }
                this.liveStatusTask = null;
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
        }

        private void UpdateSftpStatus(Connections.ConnectionStatus status, int attempt = 0, int maxAttempts = 0)
        {
            this.statusModel.SftpState = status;
            this.statusModel.SftpAttempt = attempt;
            this.statusModel.SftpMaxAttempts = maxAttempts;
            this.StatusChanged?.Invoke(this, this.statusModel);
        }

        private void UpdateSshStatus(Connections.ConnectionStatus status, int attempt = 0, int maxAttempts = 0)
        {
            this.statusModel.SshState = status;
            this.statusModel.SshAttempt = attempt;
            this.statusModel.SshMaxAttempts = maxAttempts;
            this.StatusChanged?.Invoke(this, this.statusModel);
        }
    }
}
