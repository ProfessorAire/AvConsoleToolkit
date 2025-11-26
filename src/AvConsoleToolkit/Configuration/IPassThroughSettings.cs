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
    }
}
