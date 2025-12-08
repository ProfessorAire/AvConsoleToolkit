// <copyright file="ISshConnection.cs">
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
using System.Threading;
using System.Threading.Tasks;

namespace AvConsoleToolkit.Ssh
{
    /// <summary>
    /// Provides access to both shell and file transfer functionality over SSH.
    /// </summary>
    public interface ISshConnection : IShellConnection, IFileTransferConnection
    {
        /// <summary>
        /// Gets a value indicating whether the connection is established.
        /// </summary>
        new bool IsConnected { get; }

        /// <summary>
        /// Gets or sets the maximum number of reconnection attempts.
        /// A value of 0 means no automatic reconnection.
        /// A value of -1 means unlimited reconnection attempts.
        /// </summary>
        int MaxReconnectionAttempts { get; set; }
    }
}
