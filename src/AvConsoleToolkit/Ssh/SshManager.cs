// <copyright file="SshManager.cs">
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
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Spectre.Console;

namespace AvConsoleToolkit.Ssh
{
    /// <summary>
    /// Manages SSH connections and shell streams with connection pooling and reuse.
    /// </summary>
    internal static class SshManager
    {
        private static readonly Dictionary<string, SshClient> SshClients = [];

        private static readonly Dictionary<string, SftpClient> SftpClients = [];

        private static readonly Dictionary<string, IShellStream> ShellStreams = [];

        private static readonly Lock LockObject = new();

        /// <summary>
        /// Gets an SSH client for the specified connection parameters.
        /// Returns an existing connected client if available, otherwise creates a new one.
        /// Will not connect the connection if it is not already connected.
        /// </summary>
        /// <param name="address">Host address.</param>
        /// <param name="username">SSH username.</param>
        /// <param name="password">SSH password.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A connected SSH client.</returns>
        public static async Task<ISshClient> GetSshClientAsync(
            string address,
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            var key = GetConnectionKey(address, username);

            lock (LockObject)
            {
                if (SshClients.TryGetValue(key, out var existingClient))
                {
                    if (existingClient.IsConnected)
                    {
                        return existingClient;
                    }

                    // Clean up disconnected client
                    existingClient.Dispose();
                    SshClients.Remove(key);

                    ReleaseShellStream(address, username);
                }
            }

            // Create new client
            var client = new SshClient(address, username, password);

            lock (LockObject)
            {
                SshClients[key] = client;
            }

            return client;
        }

        /// <summary>
        /// Gets an SFTP client for the specified connection parameters.
        /// Returns an existing connected client if available, otherwise creates a new one.
        /// Will not connect the connection if it is not already connected.
        /// </summary>
        /// <param name="address">Host address.</param>
        /// <param name="username">SSH username.</param>
        /// <param name="password">SSH password.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An SFTP client.</returns>
        public static async Task<ISftpClient> GetSftpClientAsync(
            string address,
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            var key = GetConnectionKey(address, username);

            lock (LockObject)
            {
                if (SftpClients.TryGetValue(key, out var existingClient))
                {
                    if (existingClient.IsConnected)
                    {
                        return existingClient;
                    }

                    // Clean up disconnected client
                    existingClient.Dispose();
                    SftpClients.Remove(key);
                }
            }

            // Create new client
            var client = new SftpClient(address, username, password);

            lock (LockObject)
            {
                SftpClients[key] = client;
            }

            return client;
        }

        /// <summary>
        /// Gets a shell stream for the specified connection parameters.
        /// Returns an existing shell stream if available, otherwise creates a new one.
        /// </summary>
        /// <param name="address">Host address.</param>
        /// <param name="username">SSH username.</param>
        /// <param name="password">SSH password.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A shell stream.</returns>
        public static async Task<IShellStream> GetShellStreamAsync(
            string address,
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            var key = GetConnectionKey(address, username);

            lock (LockObject)
            {
                if (ShellStreams.TryGetValue(key, out var existingStream))
                {
                    return existingStream;
                }
            }

            // Get or create SSH client
            var client = await GetSshClientAsync(address, username, password, cancellationToken);
            if (!client.IsConnected)
            {
                await AnsiConsole.Status()
                   .StartAsync("Connecting to device...", async ctx =>
                   {
                       ctx.Status("Connecting to SSH client.");
                       await client.ConnectAsync(cancellationToken);
                       ctx.Status("Connected");
                   });
            }

            // Create shell stream
            var shellStream = new ShellStreamWrapper(
                client.CreateShellStream("xterm", 80, 24, 800, 600, 1024));

            lock (LockObject)
            {
                ShellStreams[key] = shellStream;
            }

            return shellStream;
        }

        /// <summary>
        /// Releases a shell stream for the specified connection parameters.
        /// </summary>
        /// <param name="address">Host address.</param>
        /// <param name="username">SSH username.</param>
        public static void ReleaseShellStream(string address, string username)
        {
            var key = GetConnectionKey(address, username);

            lock (LockObject)
            {
                if (ShellStreams.TryGetValue(key, out var stream))
                {
                    try
                    {
                        stream.Dispose();
                    }
                    catch
                    {
                    }

                    ShellStreams.Remove(key);
                }
            }
        }

        /// <summary>
        /// Releases an SSH client for the specified connection parameters.
        /// </summary>
        /// <param name="address">Host address.</param>
        /// <param name="username">SSH username.</param>
        public static void ReleaseSshClient(string address, string username)
        {
            var key = GetConnectionKey(address, username);

            lock (LockObject)
            {
                if (SshClients.TryGetValue(key, out var client))
                {
                    try
                    {
                        if (client.IsConnected)
                        {
                            client.Disconnect();
                        }
                    }
                    catch
                    {
                        // Ignore disconnect errors
                    }

                    client.Dispose();
                    SshClients.Remove(key);
                }
            }
        }

        /// <summary>
        /// Releases an SFTP client for the specified connection parameters.
        /// </summary>
        /// <param name="address">Host address.</param>
        /// <param name="username">SSH username.</param>
        public static void ReleaseSftpClient(string address, string username)
        {
            var key = GetConnectionKey(address, username);

            lock (LockObject)
            {
                if (SftpClients.TryGetValue(key, out var client))
                {
                    try
                    {
                        if (client.IsConnected)
                        {
                            client.Disconnect();
                        }
                    }
                    catch
                    {
                        // Ignore disconnect errors
                    }

                    client.Dispose();
                    SftpClients.Remove(key);
                }
            }
        }

        /// <summary>
        /// Releases all connections.
        /// </summary>
        public static void ReleaseAll()
        {
            lock (LockObject)
            {
                foreach (var stream in ShellStreams.Values)
                {
                    try
                    {
                        stream.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }
                ShellStreams.Clear();

                foreach (var client in SshClients.Values)
                {
                    try
                    {
                        if (client.IsConnected)
                        {
                            client.Disconnect();
                        }
                        client.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }
                SshClients.Clear();

                foreach (var client in SftpClients.Values)
                {
                    try
                    {
                        if (client.IsConnected)
                        {
                            client.Disconnect();
                        }
                        client.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }
                SftpClients.Clear();
            }
        }

        private static string GetConnectionKey(string address, string username)
        {
            return $"{address}:{username}".ToLowerInvariant();
        }
    }
}
