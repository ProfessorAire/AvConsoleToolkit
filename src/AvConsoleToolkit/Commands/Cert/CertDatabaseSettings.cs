// <copyright file="CertDatabaseSettings.cs">
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
using AvConsoleToolkit.CertManager;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Cert
{
    /// <summary>
    /// Base settings shared by all cert commands, providing a database path option.
    /// </summary>
    public class CertDatabaseSettings : CommandSettings
    {
        /// <summary>
        /// Gets or sets the path to the certificate database. If not specified, the working directory is checked
        /// for a <c>certmanager.ddb</c> file, followed by the global database path from configuration.
        /// </summary>
        [CommandOption("--db <PATH>")]
        [Description("Path to the certificate database file. If not specified, searches the working directory and global config.")]
        public string? DatabasePath { get; set; }

        /// <summary>
        /// Resolves the database path using the explicit path, working directory, and global config.
        /// </summary>
        /// <returns>The resolved database path, or <see langword="null"/>.</returns>
        public string? ResolveDbPath()
        {
            var globalPath = Configuration.AppConfig.Settings.CertManager?.DatabasePath;
            return CertDatabase.ResolveDatabasePath(DatabasePath, globalPath);
        }
    }
}
