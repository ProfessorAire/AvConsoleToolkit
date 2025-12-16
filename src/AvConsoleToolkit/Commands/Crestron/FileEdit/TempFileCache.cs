// <copyright file="TempFileCache.cs">
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

namespace AvConsoleToolkit.Commands.Crestron.FileEdit
{
    /// <summary>
    /// Manages temporary files downloaded from remote devices during editing sessions.
    /// Files are cached for the duration of the program execution and cleaned up on exit.
    /// </summary>
    public sealed class TempFileCache : IDisposable
    {
        private static readonly Lazy<TempFileCache> instance = new(() => new TempFileCache());

        private readonly Dictionary<string, string> cachedFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly string cacheDirectory;
        private readonly Lock lockObject = new();
        private bool disposed;

        private TempFileCache()
        {
            this.cacheDirectory = Path.Combine(Path.GetTempPath(), "AvConsoleToolkit", "FileCache");
            Directory.CreateDirectory(this.cacheDirectory);

            // Register cleanup on process exit
            AppDomain.CurrentDomain.ProcessExit += this.OnProcessExit;
        }

        /// <summary>
        /// Gets the singleton instance of the temp file cache.
        /// </summary>
        public static TempFileCache Instance => instance.Value;

        /// <summary>
        /// Gets the local file path for a cached file, or null if not cached.
        /// </summary>
        /// <param name="hostAddress">The host address the file was downloaded from.</param>
        /// <param name="remotePath">The remote file path.</param>
        /// <returns>The local file path if cached; otherwise, null.</returns>
        public string? GetCachedFilePath(string hostAddress, string remotePath)
        {
            lock (this.lockObject)
            {
                var key = this.GetCacheKey(hostAddress, remotePath);
                if (this.cachedFiles.TryGetValue(key, out var localPath) && System.IO.File.Exists(localPath))
                {
                    return localPath;
                }

                return null;
            }
        }

        /// <summary>
        /// Gets or creates a local file path for caching a remote file.
        /// </summary>
        /// <param name="hostAddress">The host address the file will be downloaded from.</param>
        /// <param name="remotePath">The remote file path.</param>
        /// <returns>The local file path to use for caching.</returns>
        public string GetOrCreateCachePath(string hostAddress, string remotePath)
        {
            lock (this.lockObject)
            {
                var key = this.GetCacheKey(hostAddress, remotePath);

                if (this.cachedFiles.TryGetValue(key, out var existingPath))
                {
                    return existingPath;
                }

                // Create a subdirectory for the host
                var hostDir = Path.Combine(this.cacheDirectory, this.SanitizeFileName(hostAddress));
                Directory.CreateDirectory(hostDir);

                // Create path preserving remote directory structure
                var remoteDir = Path.GetDirectoryName(remotePath);
                if (!string.IsNullOrEmpty(remoteDir))
                {
                    // Sanitize each directory component separately, then combine
                    var dirParts = remoteDir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in dirParts)
                    {
                        hostDir = Path.Combine(hostDir, this.SanitizeFileName(part));
                    }

                    Directory.CreateDirectory(hostDir);
                }

                var fileName = Path.GetFileName(remotePath);
                var localPath = Path.Combine(hostDir, fileName);

                this.cachedFiles[key] = localPath;
                return localPath;
            }
        }

        /// <summary>
        /// Removes a file from the cache and deletes it from disk.
        /// </summary>
        /// <param name="hostAddress">The host address.</param>
        /// <param name="remotePath">The remote file path.</param>
        public void RemoveFromCache(string hostAddress, string remotePath)
        {
            lock (this.lockObject)
            {
                var key = this.GetCacheKey(hostAddress, remotePath);
                if (this.cachedFiles.TryGetValue(key, out var localPath))
                {
                    this.cachedFiles.Remove(key);
                    try
                    {
                        if (System.IO.File.Exists(localPath))
                        {
                            System.IO.File.Delete(localPath);
                        }
                    }
                    catch
                    {
                        // Ignore deletion errors
                    }
                }
            }
        }

        /// <summary>
        /// Clears all cached files.
        /// </summary>
        public void ClearCache()
        {
            lock (this.lockObject)
            {
                this.cachedFiles.Clear();
                try
                {
                    if (Directory.Exists(this.cacheDirectory))
                    {
                        Directory.Delete(this.cacheDirectory, true);
                        Directory.CreateDirectory(this.cacheDirectory);
                    }
                }
                catch
                {
                    // Ignore deletion errors
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            AppDomain.CurrentDomain.ProcessExit -= this.OnProcessExit;
            this.CleanupCache();
        }

        private void OnProcessExit(object? sender, EventArgs e)
        {
            this.CleanupCache();
        }

        private void CleanupCache()
        {
            try
            {
                if (Directory.Exists(this.cacheDirectory))
                {
                    Directory.Delete(this.cacheDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors during shutdown
            }
        }

        private string GetCacheKey(string hostAddress, string remotePath)
        {
            return $"{hostAddress}:{remotePath}";
        }

        private string SanitizeFileName(string input)
        {
            var invalidChars = new HashSet<char>(Path.GetInvalidFileNameChars());
            var result = new System.Text.StringBuilder(input.Length);

            foreach (var c in input)
            {
                result.Append(invalidChars.Contains(c) ? '_' : c);
            }

            return result.ToString();
        }
    }
}
