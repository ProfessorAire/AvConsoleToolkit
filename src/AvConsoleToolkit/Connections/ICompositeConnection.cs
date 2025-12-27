// <copyright file="ICompositeConnection.cs">
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

namespace AvConsoleToolkit.Connections
{
    /// <summary>
    /// Represents a connection that supports both shell command execution and file transfer operations.
    /// </summary>
    /// <remarks>This interface combines the capabilities of <see cref="IShellConnection"/> and <see cref="IFileTransferConnection"/>,
    /// allowing implementations to provide both interactive shell access and file transfer functionality over a single
    /// connection. Members inherited from the base interfaces define the available operations.</remarks>
    public interface ICompositeConnection : IShellConnection, IFileTransferConnection
    {
    }
}
