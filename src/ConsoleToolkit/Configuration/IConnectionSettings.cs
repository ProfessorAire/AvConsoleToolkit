using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleToolkit.Configuration
{
    public interface IConnectionSettings
    {
        string AddressBooksLocation { get; set; }
    }
}
