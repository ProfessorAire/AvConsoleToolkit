using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AvConsoleToolkit.Configuration
{
    public interface IPassThroughSettings
    {
        /// <summary>
        /// Gets a value indicating whether to use command history for pass-through sessions.
        /// </summary>
        [DefaultValue(true)]
        bool UseHistoryForPassThrough { get; set; }

        /// <summary>
        /// Gets or sets the number of times the system will attempt to reconnect after a connection failure.
        /// </summary>
        [DefaultValue(-1)]
        int NumberOfReconnectionAttempts { get; set; }
    }
}
