using System;
using ConsoleToolkit.Commands.Program;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleToolkit;

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