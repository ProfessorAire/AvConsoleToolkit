// <copyright file="ShellStreamWrapper.cs" company="AVI-SPL Global LLC.">
// Copyright (C) AVI-SPL Global LLC. All Rights Reserved.
// The intellectual and technical concepts contained herein are proprietary to AVI-SPL Global LLC. and subject to AVI-SPL's standard software license agreement.
// These materials may not be copied, reproduced, distributed or disclosed, in whole or in part, in any way without the written permission of an authorized
// representative of AVI-SPL. All references to AVI-SPL Global LLC. shall also be references to AVI-SPL Global LLC's affiliates.
// </copyright>

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Spectre.Console;

namespace ConsoleToolkit.Ssh
{
    /// <summary>
    /// Wrapper class for ShellStream to implement IShellStream interface for testability.
    /// </summary>
    public class ShellStreamWrapper : IShellStream
    {
        private readonly ShellStream shellStream;

        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShellStreamWrapper"/> class.
        /// </summary>
        /// <param name="shellStream">The underlying ShellStream instance.</param>
        public ShellStreamWrapper(ShellStream shellStream)
        {
            this.shellStream = shellStream ?? throw new ArgumentNullException(nameof(shellStream));
        }

        /// <inheritdoc/>
        public bool DataAvailable => this.shellStream.DataAvailable;

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public string Read()
        {
            return this.shellStream.Read();
        }

        public async Task<bool> WaitForCommandCompletionAsync(
            IEnumerable<string>? successPatterns,
            IEnumerable<string>? failurePatterns,
            CancellationToken cancellationToken,
            int timeoutMs = 15000,
            bool writeReceivedData = true)
        {
            var output = new StringBuilder();
            var startTime = DateTime.UtcNow;

            while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (this.shellStream.DataAvailable)
                {
                    var data = this.shellStream.Read();
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

                await Task.Delay(100);
            }

            return false;
        }

        /// <inheritdoc/>
        public void WriteLine(string line)
        {
            this.shellStream.WriteLine(line);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the ShellStreamWrapper and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    this.shellStream?.Dispose();
                }

                this.disposed = true;
            }
        }
    }
}
