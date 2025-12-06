// <copyright file="ProgramUploadSettings.cs">
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

namespace AvConsoleToolkit.Commands.Crestron.Program
{
    /// <summary>
    /// Settings used to configure the <see cref="ProgramUploadCommand"/> behavior.
    /// </summary>
    public sealed class ProgramUploadSettings : CommandSettings
    {
        /// <summary>
        /// When specified, only changed files are uploaded instead of the full package.
        /// </summary>
        [CommandOption("-c|--changed-only")]
        [Description("Upload only changed files instead of the full package")]
        public bool ChangedOnly { get; set; }

        /// <summary>
        /// When specified, indicates not to restart the program.
        /// </summary>
        [CommandOption("-d|--doNotStart")]
        [Description("Do not start the program after upload")]
        public bool DoNotStart { get; set; }

        /// <summary>
        /// Target device address or hostname.
        /// </summary>
        [CommandOption("-a|--address", true)]
        [Description("Target device IP address, hostname, or address book device name.")]
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// When true, kill the program on the device prior to uploading.
        /// </summary>
        [CommandOption("-k|--kill")]
        [Description("Kill the program on the device before uploading")]
        public bool KillProgram { get; set; }

        /// <summary>
        /// When true, skip configuring the IP table from the .dip file.
        /// </summary>
        [CommandOption("--nodip")]
        [Description("Skip configuring the IP table from the program's .dip file")]
        public bool NoIpTable { get; set; }

        /// <summary>
        /// When true, skip uploading the signature file (.sig) as a .zig file alongside .lpz programs.
        /// </summary>
        [CommandOption("--nozig")]
        [Description("Skip uploading signature file (.sig) as .zig alongside .lpz programs")]
        public bool NoZig { get; set; }

        /// <summary>
        /// Password to use for SSH/SFTP authentication.
        /// </summary>
        [CommandOption("-p|--password", false)]
        [Description("Password for SSH/SFTP authentication")]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Program package file to upload.
        /// </summary>
        [CommandArgument(0, "<PROGRAM>")]
        [Description("Path to the program package file (.cpz, .clz, or .lpz)")]
        public string ProgramFile { get; set; } = string.Empty;

        /// <summary>
        /// Program slot number on the device.
        /// </summary>
        [CommandOption("-s|--slot")]
        [Description("Program slot number on the device (1-10)")]
        public int Slot { get; set; }

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