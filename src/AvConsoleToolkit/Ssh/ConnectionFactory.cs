// <copyright file="ConnectionFactory.cs">
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

namespace AvConsoleToolkit.Ssh
{
    /// <summary>
    /// Factory implementation for creating and caching SSH connections.
    /// </summary>
    public class ConnectionFactory : IConnectionFactory
    {
        private static readonly Lazy<ConnectionFactory> LazyInstance = new(() => new ConnectionFactory());
        private readonly Dictionary<string, ISshConnection> connectionCache = [];
        private readonly Lock lockObject = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionFactory"/> class.
        /// </summary>
        protected ConnectionFactory()
        {
        }

        /// <summary>
        /// Gets the singleton instance of the connection factory.
        /// </summary>
        public static ConnectionFactory Instance => LazyInstance.Value;

        /// <inheritdoc/>
        public ISshConnection GetSshConnection(string hostAddress, int port, string username, string password)
        {
            if (string.IsNullOrEmpty(hostAddress))
            {
                throw new ArgumentNullException(nameof(hostAddress));
            }

            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentNullException(nameof(username));
            }

            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentNullException(nameof(password));
            }

            var key = this.GetConnectionKey(hostAddress, port, username);

            lock (this.lockObject)
            {
                if (this.connectionCache.TryGetValue(key, out var existingConnection))
                {
                    return existingConnection;
                }

                var connection = new SshConnection(hostAddress, port, username, password);
                this.connectionCache[key] = connection;
                return connection;
            }
        }

        /// <inheritdoc/>
        public ISshConnection GetSshConnection(string hostAddress, int port)
        {
            if (string.IsNullOrEmpty(hostAddress))
            {
                throw new ArgumentNullException(nameof(hostAddress));
            }

            // Determine the path to the user's SSH private key
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var sshDirectory = Path.Combine(homeDirectory, ".ssh");
            var privateKeyPath = Path.Combine(sshDirectory, "id_rsa");

            // Check for other common key names if id_rsa doesn't exist
            if (!File.Exists(privateKeyPath))
            {
                privateKeyPath = Path.Combine(sshDirectory, "id_ed25519");
            }

            if (!File.Exists(privateKeyPath))
            {
                throw new FileNotFoundException("No SSH private key found. Expected key at ~/.ssh/id_rsa or ~/.ssh/id_ed25519");
            }

            // Use system username for cache key since key auth doesn't require explicit username
            var key = this.GetConnectionKey(hostAddress, port, Environment.UserName);

            lock (this.lockObject)
            {
                if (this.connectionCache.TryGetValue(key, out var existingConnection))
                {
                    return existingConnection;
                }

                var connection = new SshConnection(hostAddress, port, privateKeyPath);
                this.connectionCache[key] = connection;
                return connection;
            }
        }

        /// <inheritdoc/>
        public void ReleaseAll()
        {
            lock (this.lockObject)
            {
                foreach (var connection in this.connectionCache.Values)
                {
                    try
                    {
                        connection.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }

                this.connectionCache.Clear();
            }
        }

        private string GetConnectionKey(string hostAddress, int port, string username)
        {
            return $"{hostAddress}:{port}:{username}".ToLowerInvariant();
        }
    }
}
