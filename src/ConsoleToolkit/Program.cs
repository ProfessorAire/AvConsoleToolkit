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

using Config.Net;
using ConsoleToolkit.Commands.Program;
using ConsoleToolkit.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleToolkit
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            AnsiConsole.WriteLine($"AddressBook Location: {AppConfig.Settings.Connection.AddressBooksLocation}");
            AnsiConsole.Prompt(new ConfirmationPrompt("Continue?"));

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

                    //branch.AddExample("crestron program upload program.cpz -s 1 -h 192.168.100.1 -u user -p password");
                    //branch.AddExample("crestron program upload program.cpz -s 1 -h 192.168.100.1 -u user -p password -k -c");

                    branch.AddBranch("program", program =>
                    {
                        program.AddCommand<ProgramUploadCommand>("upload")
                            .WithAlias("u")
                            .WithDescription("Upload a program to Crestron hardware")
                            .WithExample(["crestron", "program", "upload", "myprogram.cpz", "-s", "1", "-h", "192.168.1.100", "-u", "admin", "-p", "password"])
                            .WithExample(["crestron", "program", "upload", "myprogram.cpz", "-s", "1", "-h", "192.168.1.100", "-u", "admin", "-p", "password", "-c"]);
                    })
                    .WithAlias("p");
                })
                .WithAlias("c");
            });

            return app.Run(args);
        }
    }
}