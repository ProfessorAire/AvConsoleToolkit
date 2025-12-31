// <copyright file="ConnectionStatus.cs">
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

namespace AvConsoleToolkit.Ssh
{
    /// <summary>
    /// Status of a connection.
    /// </summary>
    public enum ConnectionStatus
    {
        /// <summary>
        /// The connection is not established.
        /// </summary>
        NotConnected = 0,

        /// <summary>
        /// The connection is being established.
        /// </summary>
        Connecting,

        /// <summary>
        /// The connection has been established.
        /// </summary>
        Connected,

        /// <summary>
        /// The connection was lost.
        /// </summary>
        LostConnection,

        /// <summary>
        /// The connection is being re-established.
        /// </summary>
        Reconnecting,

        /// <summary>
        /// A connection attempt failed.
        /// </summary>
        ConnectionFailed,

        /// <summary>
        /// The connection is being closed.
        /// </summary>
        Disconnecting,
    }
}
