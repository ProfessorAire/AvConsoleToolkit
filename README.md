# Audio/Visual Console Toolkit (ACT)

ACT provides a set of command-line utilities to help make development and deployment of Audio Visual systems easier. While this is _not_ intended to be a one-stop-shop application for everything you could possibly imagine, suggestions for new commands are welcome.

## Installation

Download the latest release and unzip it to a location of your choice. Ensure that location is on your path so you can execute the application by typing `act` and get started. Try `act -h` to get started and see the help for the application.

## Updating

You can update the program by running the `update` command to check for updates from GitHub.

## Command Highlights

What follows will never be a complete listing of commands available in the application, but a selection of commands worth highlighting.

### Crestron Program Upload

Uploading a program to a Crestron processor isn't a super complicated process, but there are many things you can do to simplify your life when rapidly iterating on programs, be they in C# or SIMPL Windows. This program includes several options for making this process faster, most notably the `-c` or `--changed-only` option, which will use file hashes and timestamps to ensure that only changed files are uploaded. For large SIMPL Windows programs or C# programs the amount of time spend uploading files that haven't changed is considerable, especially if you're deploying to remote sites over a VPN. Additionally, if you're developing C# code for SIMPL Windows you go through a hefty development cycle of compiling the C#, compiling the SIMPL Windows, and reloading the program. With this command you can upload a `clz` file (with or without the `-c` flag) and your program will be stopped, the files uploaded, and the program restarted, no SIMPL Compilation needed. (As long as you haven't changed anything in your S+ interaction layers, of course.)

### Crestron Connect

This command starts a remote session, where the SSH connection is established by the application and then forwarded to your terminal. The application provides history (on a per-device basis) that is accessible via the up-arrow or by typing, at which point fuzzy matching is used to find previous commands you've run on a device. It also supports pass-through commands for things such as `Program Upload`, by prefixing the command with a `:` character.

For example, to upload a program while connected via `act crestron connect`, you would type `:program upload path/to/program/file.lpz|cpz|clz`. You don't need to specify any connection details, as the exisiting connection is used. This helps speed up commands further, as no session needs to be established.

Additionally, if your SSH connection drops for any reason the application will automatically attempt to reconnect to the device.

Since only specific keys cause commands to be sent to the underlying device (Tab and Enter), the command you're currently typing is always persisted at the end of the session output, meaning that a command being typed won't have its text split by incoming data.