// <copyright file="CertServeSettings.cs">
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

using System.ComponentModel;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert
{
    /// <summary>
    /// Settings for the <c>cert serve</c> command.
    /// </summary>
    public class CertServeSettings : CertDatabaseSettings
    {
        /// <summary>
        /// Gets or sets the port for the web server.
        /// </summary>
        [CommandOption("--port <PORT>")]
        [Description("Port for the web server (default 5120).")]
        [DefaultValue(5120)]
        public int Port { get; set; } = 5120;

        /// <summary>
        /// Gets or sets a value indicating whether to open the browser automatically.
        /// </summary>
        [CommandOption("--open")]
        [Description("Open the browser automatically when the server starts.")]
        public bool OpenBrowser { get; set; }
    }
}
