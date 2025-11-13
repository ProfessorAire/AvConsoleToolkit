// <copyright file="IShellStream.cs">
// The MIT License
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”),
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

namespace ConsoleToolkit.Ssh
{
    /// <summary>
    /// Interface for shell stream operations to enable testability.
    /// </summary>
    internal interface IShellStream : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether data is available to read.
        /// </summary>
        bool DataAvailable { get; }

        /// <summary>
        /// Reads data from the shell stream.
        /// </summary>
        /// <returns>The data read from the stream.</returns>
        string Read();

        /// <summary>
        /// Asynchronously waits for the completion of a command executed on the shell stream.
        /// </summary>
        /// <param name="successPatterns">A collection of string patterns indicating successful command completion.</param>
        /// <param name="failurePatterns">A collection of string patterns indicating command failure.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for command completion. Default is 15000ms.</param>
        /// <param name="writeReceivedData">If <see langword="true"/>, writes received data to the output. Default is <see langword="true"/>.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result is <see langword="true"/> if a success pattern is matched;
        /// <see langword="false"/> if a failure pattern is matched or the operation times out.
        /// </returns>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled during execution.</exception>
        Task<bool> WaitForCommandCompletionAsync(
            IEnumerable<string>? successPatterns,
            IEnumerable<string>? failurePatterns,
            CancellationToken cancellationToken,
            int timeoutMs = 15000,
            bool writeReceivedData = true);

        /// <summary>
        /// Writes a line to the shell stream.
        /// </summary>
        /// <param name="line">The line to write.</param>
        void WriteLine(string line);
    }
}
