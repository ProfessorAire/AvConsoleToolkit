// <copyright file="Program.cs">
// The MIT License
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using System;
using Spectre.Console.Cli;

namespace AvConsoleToolkit
{
    /// <summary>
    /// Entry point for the ConsoleToolkit application.
    /// Configures and runs the Spectre.Console.Cli command application.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main method. Configures the command-line interface and runs the application.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Exit code.</returns>
        public static int Main(string[] args)
        {
            var app = new CommandApp();

            app.Configure(config =>
            {
                config.SetApplicationName("act")
                .PropagateExceptions()
                .UseAssemblyInformationalVersion()
                .ValidateExamples();

                if (OperatingSystem.IsWindows())
                {
                    config.AddCommand<Commands.UpdateCommand>("update")
                        .WithDescription("Check for and install updates for this application")
                        .WithExample(["update"])
                        .WithExample(["update", "--yes"]);
                }

                config.AddCommand<Commands.AboutCommand>("about")
                    .WithDescription("Display information about the application")
                    .WithExample(["about"])
                    .WithExample(["about", "--license"])
                    .WithExample(["about", "--licenses"]);

                config.AddBranch("crestron", branch =>
                {
                    branch.SetDescription("Commands for Crestron hardware management");

                    branch.AddBranch("program", program =>
                    {
                        program.AddCommand<Commands.Crestron.Program.ProgramUploadCommand>("upload")
                            .WithAlias("u")
                            .WithAlias("load")
                            .WithAlias("l")
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

                    cfg.AddCommand<Commands.Config.ListConfigCommand>("list")
                        .WithAlias("l")
                        .WithDescription("Lists all configuration keys and values from merged configuration.")
                        .WithExample(["config", "list"])
                        .WithExample(["config", "l", "--show-sources"]);

                    cfg.AddCommand<Commands.Config.SetConfigCommand>("set")
                        .WithAlias("s")
                        .WithDescription("Sets a single configuration key.")
                        .WithExample(["config", "set", "-s", "Connection", "AddressBooksLocation", "C:/addressBooks"])
                        .WithExample(["config", "set", "--section", "Connection", "AddressBooksLocation", "C:/addressBooks", "--local"]);

                    cfg.AddCommand<Commands.Config.RemoveConfigCommand>("remove")
                        .WithAlias("r")
                        .WithDescription("Removes a single configuration key.")
                        .WithExample(["config", "remove", "Connection", "AddressBooksLocation"])
                        .WithExample(["config", "r", "Connection", "AddressBooksLocation", "--local"]);
                });

                config.AddBranch("addressbook", ab =>
                {
                    ab.SetDescription("Utilities for looking up Crestron device information from address books.");
                    
                    ab.AddCommand<Commands.AddressBook.AddressBookListCommand>("list")
                        .WithAlias("ls")
                        .WithDescription("List all entries from configured address books")
                        .WithExample(["addressbook", "list"])
                        .WithExample(["ab", "ls", "--detailed"]);
                    
                    ab.AddCommand<Commands.AddressBook.AddressBookLookupCommand>("lookup")
                        .WithAlias("l")
                        .WithDescription("Look up a specific address book entry by name or IP address")
                        .WithExample(["addressbook", "lookup", "SomeEntryName"])
                        .WithExample(["ab", "l", "10.10.120.12"]);
                })
                .WithAlias("ab");
            });

            return app.Run(args);
        }
    }
}