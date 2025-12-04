using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace AvConsoleToolkit.Configuration
{
    /// <summary>
    /// Defines settings for pass-through sessions.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public interface IPassThroughSettings
    {
        /// <summary>
        /// Gets a value indicating whether to use command history for pass-through sessions.
        /// </summary>
        [DefaultValue(true)]
        bool UseHistoryForPassThrough { get; set; }

        /// <summary>
        /// Gets or sets the number of times the system will attempt to reconnect after a connection failure.
        /// A value of -1 indicates infinite reconnection attempts.
        /// </summary>
        [DefaultValue(-1)]
        int NumberOfReconnectionAttempts { get; set; }

        /// <summary>
        /// Gets or sets custom command mappings for Crestron pass-through sessions.
        /// Format: "command1=mappedCommand1;command2=mappedCommand2"
        /// Example: "ls=dir;cat=type;clear=cls"
        /// These mappings will be merged with the default mappings, with user-defined mappings taking precedence.
        /// </summary>
        [DefaultValue("")]
        string CrestronCommandMappings { get; set; }
    }
}
