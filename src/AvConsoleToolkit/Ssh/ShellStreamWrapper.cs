// <copyright file="ShellStreamWrapper.cs">
// The MIT License
// Copyright � Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the �Software�),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Spectre.Console;

namespace AvConsoleToolkit.Ssh
{
    /// <summary>
    /// Provides a wrapper for <see cref="ShellStream"/> to add async command completion waiting functionality.
    /// </summary>
    internal class ShellStreamWrapper
    {
        /// <summary>
        /// The underlying <see cref="ShellStream"/> instance.
        /// </summary>
        private readonly ShellStream shellStream;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShellStreamWrapper"/> class.
        /// </summary>
        /// <param name="shellStream">The <see cref="ShellStream"/> to wrap.</param>
        /// <exception cref="ArgumentNullException"><paramref name="shellStream"/> is null.</exception>
        public ShellStreamWrapper(ShellStream shellStream)
        {
            this.shellStream = shellStream ?? throw new ArgumentNullException(nameof(shellStream));
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
    }
}
