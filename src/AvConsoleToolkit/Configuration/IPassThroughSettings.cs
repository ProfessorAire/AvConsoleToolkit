// <copyright file="IPassThroughSettings.cs">
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
using System.Diagnostics.CodeAnalysis;

namespace AvConsoleToolkit.Configuration
{
    /// <summary>
    /// Defines settings for pass-through sessions.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public interface IPassThroughSettings
    {
        /// <summary>
        /// Gets or sets custom command mappings for Crestron pass-through sessions.
        /// Format: "command1=mappedCommand1;command2=mappedCommand2"
        /// Example: "ls=dir;cat=type;clear=cls"
        /// These mappings will be merged with the default mappings, with user-defined mappings taking precedence.
        /// </summary>
        [DefaultValue("")]
        string CrestronCommandMappings { get; set; }

        /// <summary>
        /// Gets or sets the number of times the system will attempt to reconnect after a connection failure.
        /// A value of -1 indicates infinite reconnection attempts.
        /// </summary>
        [DefaultValue(-1)]
        int NumberOfReconnectionAttempts { get; set; }

        /// <summary>
        /// Gets a value indicating whether to use command history for pass-through sessions.
        /// </summary>
        [DefaultValue(true)]
        bool UseHistoryForPassThrough { get; set; }
    }
}
