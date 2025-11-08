using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleToolkit.Configuration
{
    public interface ISettings
    {
        IConnectionSettings Connection { get; }
    }
}
