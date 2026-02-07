// <copyright file="AddressBookListSettings.cs">
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

namespace AvConsoleToolkit.Commands.AddressBook
{
    /// <summary>
    /// Settings for the AddressBookListCommand, used to list all address book entries.
    /// </summary>
    public class AddressBookListSettings : CommandSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether to show detailed information.
        /// </summary>
        [CommandOption("--detailed")]
        [Description("Show detailed information including source file for each entry.")]
        [DefaultValue(false)]
        public bool Detailed { get; set; }

        [CommandOption("--show-passwords")]
        [Description("Shows passwords for entries that include them. Not recommended when in a shared environment, your console is being recorded, or screenshots are being taken.")]
        [DefaultValue(false)]
        public bool ShowPasswords { get; set; }
    }
}
