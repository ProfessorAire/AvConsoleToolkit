// <copyright file="IConnectionFactory.cs">
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
    /// Factory for creating and caching SSH connections.
    /// </summary>
    public interface IConnectionFactory
    {
        /// <summary>
        /// Gets a composite connection with SSH key authentication.
        /// Attempts to authenticate using the user's local SSH key.
        /// </summary>
        /// <param name="hostAddress">The host address to connect to.</param>
        /// <param name="port">The port to connect on.</param>
        /// <param name="username">The username for authentication.</param>
        /// <returns>An SSH connection instance.</returns>
        ICompositeConnection GetCompositeConnection(string hostAddress, int port, string username);

        /// <summary>
        /// Gets a composite connection with password authentication.
        /// </summary>
        /// <param name="hostAddress">The host address to connect to.</param>
        /// <param name="port">The port to connect on.</param>
        /// <param name="username">The username for authentication.</param>
        /// <param name="password">The password for authentication.</param>
        /// <returns>An SSH connection instance.</returns>
        ICompositeConnection GetCompositeConnection(string hostAddress, int port, string username, string password);

        /// <summary>
        /// Releases all cached connections.
        /// </summary>
        void ReleaseAll();
    }
}
