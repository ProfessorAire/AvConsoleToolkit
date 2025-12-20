// <copyright file="FileEditSettings.cs">
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

namespace AvConsoleToolkit.Commands.Sftp
{
    /// <summary>
    /// Settings used to configure the <see cref="FileEditCommand"/> behavior.
    /// </summary>
    public sealed class FileEditSettings : CommandSettings
    {
        /// <summary>
        /// Gets or sets the path to the external application used to open the file.
        /// </summary>
        [CommandOption("-e|--external")]
        [Description("Specify an external application to open the file with.")]
        public string ExternalEditor { get; set; } = string.Empty;

        /// <summary>
        /// Force download even if file exists locally in cache.
        /// </summary>
        [CommandOption("-f|--force")]
        [Description("Force download even if file exists in local cache")]
        public bool ForceDownload { get; set; }

        /// <summary>
        /// Target device address or hostname.
        /// </summary>
        [CommandOption("-a|--address", true)]
        [Description("Target device IP address, hostname, or address book device name.")]
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// Password to use for SSH/SFTP authentication.
        /// </summary>
        [CommandOption("-p|--password", false)]
        [Description("Password for SSH/SFTP authentication")]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Remote file path to edit.
        /// </summary>
        [CommandArgument(0, "<FILE>")]
        [Description("Remote file path to edit (e.g., program01/config.xml)")]
        public string RemoteFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Use the built-in editor regardless of configured external editors.
        /// </summary>
        [CommandOption("-b|--builtin")]
        [Description("Use the built-in editor instead of any configured external editor. Overrides the '-e|--external' option, if present.")]
        public bool UseBuiltinEditor { get; set; }

        /// <summary>
        /// Username to use for SSH/SFTP authentication.
        /// </summary>
        [CommandOption("-u|--username", false)]
        [Description("Username for SSH/SFTP authentication")]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Emit verbose diagnostic output.
        /// </summary>
        [CommandOption("-v|--verbose")]
        [Description("Show detailed diagnostic output")]
        public bool Verbose { get; set; }
    }
}
