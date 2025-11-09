// <copyright file="Device.cs">
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using ConsoleToolkit.Commands;
using ConsoleToolkit.Commands.Config;
using ConsoleToolkit.Commands.Program;
using Spectre.Console.Cli;

namespace ConsoleToolkit
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var app = new CommandApp();

            app.Configure(config =>
            {
                config.SetApplicationName("ConsoleToolkit")
                .PropagateExceptions()
                .UseAssemblyInformationalVersion()
                .ValidateExamples()
                .AddBranch("crestron", branch =>
                {
                    branch.SetDescription("Commands for Crestron hardware management");

                    branch.AddBranch("program", program =>
                    {
                        program.AddCommand<ProgramUploadCommand>("upload")
                            .WithAlias("u")
                            .WithDescription("Upload a program to Crestron hardware")
                            .WithExample(["crestron", "program", "upload", "myprogram.cpz", "-s", "1", "--address", "192.168.1.100", "-u", "admin", "-p", "password"])
                            .WithExample(["crestron", "program", "upload", "myprogram.cpz", "-s", "1", "-a", "192.168.1.100", "-u", "admin", "-p", "password", "-c"]);
                    })
                    .WithAlias("p");
                })
                .WithAlias("c");

                config.AddBranch("config", cfg =>
                {
                    cfg.SetDescription("Configuration management, such as setting or reading configuration values.");

                    cfg.AddCommand<ListConfigCommand>("list")
                        .WithAlias("l")
                        .WithDescription("Lists all configuration keys and values from merged configuration.")
                        .WithExample(["config", "list"])
                        .WithExample(["config", "l", "--show-sources"]);

                    cfg.AddCommand<SetConfigCommand>("set")
                        .WithAlias("s")
                        .WithDescription("Sets a single configuration key.")
                        .WithExample(["config", "set", "Connection", "AddressBooksLocation", "C:/addressBooks"])
                        .WithExample(["config", "set", "Connection", "AddressBooksLocation", "C:/addressBooks", "--global"]);

                    cfg.AddCommand<RemoveConfigCommand>("remove")
                        .WithAlias("r")
                        .WithDescription("Removes a single configuration key.")
                        .WithExample(["config", "remove", "Connection", "AddressBooksLocation"])
                        .WithExample(["config", "r", "Connection", "AddressBooksLocation", "--global"]);
                                    });

                config.AddBranch("addressbook", ab =>
                {
                    ab.SetDescription("Utilities for looking up Crestron device information from address books.");
                    ab.AddCommand<AddressBookLookupCommand>("lookup")
                        .WithAlias("l")
                        .WithExample("addressbook", "lookup", "SomeEntryName")
                        .WithExample("ab", "l", "10.10.120.12");
                })
                .WithAlias("ab");
            });

            return app.Run(args);
        }
    }
}