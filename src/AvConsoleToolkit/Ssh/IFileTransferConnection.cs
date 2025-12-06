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

namespace AvConsoleToolkit.Ssh
{
    /// <summary>
    /// Provides access to functionality for transferring files to/from a remote device.
    /// </summary>
    public interface IFileTransferConnection : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether the connection is established.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Checks whether a file or directory exists on the remote server.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the path exists; otherwise, false.</returns>
        Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a directory on the remote server.
        /// </summary>
        /// <param name="path">The path of the directory to create.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the contents of a directory on the remote server.
        /// </summary>
        /// <param name="path">The path of the directory to list.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An enumerable collection of file system entries.</returns>
        Task<IEnumerable<ISftpFile>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a file from the remote server.
        /// </summary>
        /// <param name="remotePath">The remote file path.</param>
        /// <param name="destination">The destination stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task DownloadFileAsync(string remotePath, Stream destination, CancellationToken cancellationToken = default);

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

        /// <summary>
        /// Sets the last write time of a file on the remote server.
        /// </summary>
        /// <param name="remotePath">The remote file path.</param>
        /// <param name="lastWriteTime">The last write time to set.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task SetLastWriteTimeUtcAsync(string remotePath, DateTime lastWriteTime, CancellationToken cancellationToken = default);
    }
}
