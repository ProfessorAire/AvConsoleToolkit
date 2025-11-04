using System;
using ConsoleToolkit.Commands.Program;
using Spectre.Console.Cli;

namespace ConsoleToolkit;

public static class Program
{
    public static int Main(string[] args)
    {
        var app = new CommandApp();

        app.Configure(config =>
        {
            config.AddBranch("program", program =>
            {
                program.AddCommand<ProgramUploadCommand>("upload")
                    .WithDescription("Upload a program to Crestron hardware")
                    .WithExample(new[] { "program", "upload", "myprogram.cpz", "-s", "1", "-h", "192.168.1.100", "-u", "admin", "-p", "password" })
                    .WithExample(new[] { "program", "upload", "myprogram.cpz", "-s", "1", "-h", "192.168.1.100", "-u", "admin", "-p", "password", "-c" });
            });
        });

        return app.Run(args);
    }
}