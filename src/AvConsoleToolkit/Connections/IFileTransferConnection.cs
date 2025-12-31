// <copyright file="IFileTransferConnection.cs">
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet.Sftp;

namespace AvConsoleToolkit.Connections
{
    /// <summary>
    /// Provides access to functionality for transferring files to/from a remote device.
    /// </summary>
    public interface IFileTransferConnection : IDisposable
    {
        /// <summary>
        /// Occurs when the file transfer connection is disconnected.
        /// </summary>
        event EventHandler? FileTransferDisconnected;

        /// <summary>
        /// Occurs when the file transfer connection is reconnected after a disconnection.
        /// </summary>
        event EventHandler? FileTransferReconnected;
        
        /// <summary>
        /// Occurs when the connection status of a file transfer changes.
        /// </summary>
        /// <remarks>Subscribers can use this event to monitor changes in the file transfer connection,
        /// such as when the connection is established, lost, or transitions between states. The event provides the new
        /// connection status as a parameter.</remarks>
        event Action<Connections.ConnectionStatus> FileTransferConnectionStatusChanged;

        /// <summary>
        /// Gets a value indicating whether the file transfer connection is established.
        /// </summary>
        bool IsFileTransferConnected { get; }

        /// <summary>
        /// Gets or sets a value indicating whether console output should be suppressed.
        /// When true, the connection will not write status updates or progress information to the console.
        /// </summary>
        bool SuppressOutput { get; set; }

        /// <summary>
        /// Establishes a connection for file transfer operations asynchronously.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the connection attempt.</param>
        /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the
        /// connection is established successfully; otherwise, <see langword="false"/>.</returns>
        Task<bool> ConnectFileTransferAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a directory on the remote server.
        /// </summary>
        /// <param name="path">The path of the directory to create.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes files matching a glob pattern on the remote server.
        /// </summary>
        /// <param name="pattern">The glob pattern for files to delete.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of files deleted.</returns>
        Task<int> DeleteFilesByGlobAsync(string pattern, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a file from the remote server.
        /// </summary>
        /// <param name="remotePath">The remote file path.</param>
        /// <param name="destination">The destination stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task DownloadFileAsync(string remotePath, Stream destination, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads files matching a glob pattern from the remote server to a local directory.
        /// </summary>
        /// <param name="pattern">The glob pattern for remote files.</param>
        /// <param name="localDirectory">The local directory to download files to.</param>
        /// <param name="preserveStructure">Whether to preserve the directory structure (default: true).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of files downloaded.</returns>
        Task<int> DownloadFilesByGlobAsync(string pattern, string localDirectory, bool preserveStructure = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks whether a file or directory exists on the remote server.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the path exists; otherwise, false.</returns>
        Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the contents of a directory on the remote server.
        /// </summary>
        /// <param name="path">The path of the directory to list.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An enumerable collection of file system entries.</returns>
        Task<IEnumerable<ISftpFile>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists files on the remote server matching a glob pattern.
        /// Supports wildcards: * (any characters except /), ** (any characters including /), ? (single character), [abc] (character class).
        /// </summary>
        /// <param name="pattern">The glob pattern (e.g., "*.txt", "**/*.log", "program*/file_[0-9].dat").</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An enumerable collection of file entries matching the pattern.</returns>
        Task<IEnumerable<ISftpFile>> ListFilesByGlobAsync(string pattern, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the last write time of a file on the remote server.
        /// </summary>
        /// <param name="remotePath">The remote file path.</param>
        /// <param name="lastWriteTime">The last write time to set.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task SetLastWriteTimeUtcAsync(string remotePath, DateTime lastWriteTime, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads a file to the remote server.
        /// </summary>
        /// <param name="source">The source stream.</param>
        /// <param name="remotePath">The remote file path.</param>
        /// <param name="canOverride">Whether to overwrite an existing file.</param>
        /// <param name="uploadCallback">Optional callback for tracking upload progress.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task UploadFileAsync(Stream source, string remotePath, bool canOverride, Action<ulong>? uploadCallback = null, CancellationToken cancellationToken = default);
    }
}
