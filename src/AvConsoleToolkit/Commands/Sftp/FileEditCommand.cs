// <copyright file="FileEditCommand.cs">
// The MIT License
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.Configuration;
using AvConsoleToolkit.Crestron;
using AvConsoleToolkit.Editors;
using AvConsoleToolkit.Connections;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Sftp
{
    /// <summary>
    /// Command that allows in-line editing of files on remote Crestron devices.
    /// Downloads the file locally, opens an editor, and uploads changes back to the device.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class FileEditCommand : AsyncCommand<FileEditSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, FileEditSettings settings, CancellationToken cancellationToken)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(settings.RemoteFilePath))
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Remote file path is required.");
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(settings.Host))
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Host address is required.");
                    return 1;
                }

                // Resolve credentials if needed
                if (string.IsNullOrEmpty(settings.Username) || string.IsNullOrEmpty(settings.Password))
                {
                    if (settings.Verbose)
                    {
                        AnsiConsole.MarkupLine("[fuchsia]No username/password provided, looking up values from address books.[/]");
                    }

                    var entry = await ToolboxAddressBook.LookupEntryAsync(settings.Host);
                    if (entry is null)
                    {
                        AnsiConsole.MarkupLine("[red]Error:[/] Could not find device in address books and no username/password provided.");
                        return 101;
                    }

                    if (entry.Username is null || entry.Password is null)
                    {
                        AnsiConsole.MarkupLine("[red]Error:[/] Address book entry is missing username or password.");
                        return 102;
                    }

                    settings.Username = entry.Username;
                    settings.Password = entry.Password;
                }

                // Get SSH connection
                var connection = ConnectionFactory.Instance.GetCompositeConnection(settings.Host, 22, settings.Username, settings.Password);

                // Check for cached file or download
                var cache = TempFileCache.Instance;
                var localPath = cache.GetCachedFilePath(settings.Host, settings.RemoteFilePath);

                if (localPath == null || settings.ForceDownload || !System.IO.File.Exists(localPath))
                {
                    localPath = cache.GetOrCreateCachePath(settings.Host, settings.RemoteFilePath);
                    await this.DownloadFileAsync(connection, settings, localPath, cancellationToken);
                }
                else if (settings.Verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Using cached file at '{localPath}'[/]");
                }

                // Determine which editor to use
                var editorPath = this.GetEditorForFile(settings.RemoteFilePath, settings);

                if (string.IsNullOrEmpty(editorPath))
                {
                    // Use built-in editor
                    await this.EditWithBuiltinEditorAsync(connection, settings, localPath, cancellationToken);
                }
                else
                {
                    // Use external editor with file monitoring
                    await this.EditWithExternalEditorAsync(connection, settings, localPath, editorPath, cancellationToken);
                }

                return 0;
            }
            catch (Exception ex)
            {
                if (settings.Verbose)
                {
                    AnsiConsole.MarkupLine($"[red]Error:\r\n{ex}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                }

                return 1;
            }
        }

        /// <summary>
        /// Asynchronously downloads a remote file to the specified local path using the provided file transfer
        /// connection and settings.
        /// </summary>
        /// <remarks>If the directory for <paramref name="localPath"/> does not exist, it is created
        /// automatically. The method overwrites the file at <paramref name="localPath"/> if it already
        /// exists.</remarks>
        /// <param name="connection">The file transfer connection used to access the remote file and perform the download operation. Must be
        /// connected and capable of file transfer.</param>
        /// <param name="settings">The settings that specify details for the file download, including the remote file path and any transfer
        /// options.</param>
        /// <param name="localPath">The full path to the local file where the downloaded content will be saved. The method creates the directory
        /// if it does not exist.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the download operation.</param>
        /// <returns>A task that represents the asynchronous download operation.</returns>
        /// <exception cref="FileNotFoundException">Thrown if the remote file specified in <paramref name="settings"/> does not exist.</exception>
        private async Task DownloadFileAsync(IFileTransferConnection connection, FileEditSettings settings, string localPath, CancellationToken cancellationToken)
        {
            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns([
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new TransferSpeedColumn(),
                    new RemainingTimeColumn()
                ])
                .StartAsync(async ctx =>
                {
                    var displayName = settings.Verbose ? settings.RemoteFilePath : Path.GetFileName(settings.RemoteFilePath);
                    
                    var downloadTask = ctx.AddTask($"[cyan]Downloading {displayName}[/]");

                    await connection.ConnectFileTransferAsync(cancellationToken);

                    // Check if file exists remotely
                    if (!await connection.ExistsAsync(settings.RemoteFilePath, cancellationToken))
                    {
                        downloadTask.StopTask();
                        throw new FileNotFoundException($"Remote file not found: {settings.RemoteFilePath}");
                    }

                    // Ensure local directory exists
                    var localDir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(localDir))
                    {
                        Directory.CreateDirectory(localDir);
                    }

                    // Download to temporary file.
                    using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write))
                    {
                        await connection.DownloadFileAsync(settings.RemoteFilePath, fileStream, cancellationToken);
                    }

                    downloadTask.Value = 100;
                    downloadTask.StopTask();
                });

            AnsiConsole.MarkupLine("[green]Download complete.[/]");
        }

        /// <summary>
        /// Asynchronously uploads a local file to a remote destination using the specified file transfer connection and
        /// settings.
        /// </summary>
        /// <remarks>After the upload completes, the remote file's last write time is set to match the
        /// local file's last write time. The method reports progress only if a progress callback is provided.</remarks>
        /// <param name="connection">The file transfer connection to use for uploading the file. Must be connected before the transfer begins.</param>
        /// <param name="settings">The settings that specify how the file should be uploaded, including the remote file path.</param>
        /// <param name="localPath">The full path to the local file to upload. The file must exist and be accessible for reading.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the upload operation.</param>
        /// <param name="progressCallback">An optional callback that receives progress updates as a percentage (from 0 to 100) of the file upload
        /// completion. If null, progress is not reported.</param>
        /// <returns>A task that represents the asynchronous upload operation.</returns>
        private async Task UploadFileAsync(IFileTransferConnection connection, FileEditSettings settings, string localPath, CancellationToken cancellationToken, Action<double>? progressCallback = null)
        {
            await connection.ConnectFileTransferAsync(cancellationToken);

            using var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read);
            var fileSize = fileStream.Length;

            await connection.UploadFileAsync(
                fileStream,
                settings.RemoteFilePath,
                true,
                uploaded =>
                {
                    var percentage = ((double)uploaded / fileSize) * 100;
                    progressCallback?.Invoke(percentage);
                },
                cancellationToken);

            // Preserve timestamp
            var lastWriteTime = System.IO.File.GetLastWriteTimeUtc(localPath);
            await connection.SetLastWriteTimeUtcAsync(settings.RemoteFilePath, lastWriteTime, cancellationToken);
        }

        /// <summary>
        /// Opens the specified local file in the built-in text editor for editing and uploads changes to the remote
        /// location using the provided file transfer connection.
        /// </summary>
        /// <remarks>While the editor is open, connection status changes are reflected in the editor
        /// interface. Upload progress is displayed during save operations. If the connection is lost, the editor will
        /// indicate the status and attempt to reconnect as needed.</remarks>
        /// <param name="connection">The file transfer connection used to upload the edited file and receive connection status updates. Must be
        /// connected to the target remote system.</param>
        /// <param name="settings">The settings that specify details for editing and uploading the file, including the remote file path and
        /// editor options.</param>
        /// <param name="localPath">The full path to the local file to be edited. The file must exist and be accessible for reading and writing.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the editing or upload operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task completes when the editor is closed and any
        /// changes have been uploaded.</returns>
        private async Task EditWithBuiltinEditorAsync(IFileTransferConnection connection, FileEditSettings settings, string localPath, CancellationToken cancellationToken)
        {
            var displayName = Path.GetFileName(settings.RemoteFilePath);
            FileTextEditor? editor = null;

            async Task OnSaveAsync()
            {
                try
                {
                    // Show upload progress in the editor
                    if (editor != null)
                    {
                        editor.UploadProgress = 0;
                    }

                    await this.UploadFileAsync(
                        connection,
                        settings,
                        localPath,
                        cancellationToken,
                        progress =>
                        {
                                editor?.UploadProgress = (int)progress;
                        });

                    if (editor != null)
                    {
                        editor.UploadProgress = 100;
                        editor.UploadProgress = -1;
                    }
                }
                catch
                {
                    if (editor != null)
                    {
                        editor.UploadProgress = -1;
                    }

                    throw;
                }
            }

            editor = new FileTextEditor(localPath, displayName, OnSaveAsync, verbose: settings.Verbose);

            var statusTask = new Action<ConnectionStatus>(status => editor?.UpdateConnectionStatus(status));
            connection.FileTransferConnectionStatusChanged += statusTask;
            connection.SuppressOutput = true;
            editor.UpdateConnectionStatus(connection.IsFileTransferConnected ? ConnectionStatus.Connected : ConnectionStatus.Reconnecting);
            await editor.RunAsync(cancellationToken);
            connection.FileTransferConnectionStatusChanged -= statusTask;
            connection.SuppressOutput = false;
        }

        /// <summary>
        /// Opens a remote file in an external editor, monitors for changes, and uploads modifications automatically
        /// until the editor is closed or the operation is cancelled.
        /// </summary>
        /// <remarks>While the external editor is open, changes to the local file are detected and
        /// automatically uploaded to the remote location. Some editors may require specific command-line flags (such as
        /// '--wait') to block until the file is closed; otherwise, monitoring may end prematurely. The user can press
        /// Ctrl+K in the console to manually stop monitoring and cancel the operation. If the editor closes within a
        /// few seconds, a warning is displayed indicating that the editor may not be blocking as expected.</remarks>
        /// <param name="connection">The file transfer connection used to upload the file when changes are detected. Must be a valid, open
        /// connection.</param>
        /// <param name="settings">The settings describing the remote file to edit, including its path and transfer options. Cannot be null.</param>
        /// <param name="localPath">The full path to the local copy of the file to be edited. The file at this path will be monitored for
        /// changes and uploaded when modified.</param>
        /// <param name="editorPath">The path to the external editor executable to launch. The editor should support blocking or waiting for the
        /// file to close if possible.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation. If cancellation is requested, monitoring and
        /// uploading will stop and the editor process will no longer be tracked.</param>
        /// <returns>A task that represents the asynchronous operation of editing and uploading the file. The task completes when
        /// the editor is closed or the operation is cancelled.</returns>
        private async Task EditWithExternalEditorAsync(IFileTransferConnection connection, FileEditSettings settings, string localPath, string editorPath, CancellationToken cancellationToken)
        {
            var displayName = Path.GetFileName(settings.RemoteFilePath);
            AnsiConsole.MarkupLine($"[cyan]Opening '{displayName}' in external editor: {editorPath}[/]");
            AnsiConsole.MarkupLine("[dim]File will be uploaded automatically when saved. Press Ctrl+K to kill watcher.[/]");

            // Start file system watcher
            var fileDir = Path.GetDirectoryName(localPath) ?? ".";
            var fileName = Path.GetFileName(localPath);
            var lastWriteTime = System.IO.File.GetLastWriteTimeUtc(localPath);
            var uploading = false;

            using var watcher = new FileSystemWatcher(fileDir, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            void OnFileChanged(object sender, FileSystemEventArgs e)
            {
                // Run async work in a fire-and-forget task with proper exception handling
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (uploading)
                        {
                            return;
                        }

                        // Debounce - wait a moment to ensure the file is fully written
                        await Task.Delay(500, cancellationToken);

                        var newWriteTime = System.IO.File.GetLastWriteTimeUtc(localPath);
                        if (newWriteTime <= lastWriteTime)
                        {
                            return;
                        }

                        lastWriteTime = newWriteTime;
                        uploading = true;

                        try
                        {
                            await AnsiConsole.Progress()
                                .AutoClear(true)
                                .Columns([
                                    new TaskDescriptionColumn(),
                                    new ProgressBarColumn(),
                                    new PercentageColumn()
                                ])
                                .StartAsync(async ctx =>
                                {
                                    var uploadTask = ctx.AddTask($"[green]Uploading {displayName}[/]");
                                    await this.UploadFileAsync(
                                        connection,
                                        settings,
                                        localPath,
                                        cancellationToken,
                                        progress => uploadTask.Value = progress);

                                    uploadTask.Value = 100;
                                    uploadTask.StopTask();
                                });

                            AnsiConsole.MarkupLine($"[green]'{displayName}' uploaded successfully at {DateTime.Now:HH:mm:ss}[/]");
                        }
                        finally
                        {
                            uploading = false;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation is requested
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Upload failed: {ex.Message}[/]");
                    }
                }, cancellationToken);
            }

            watcher.Changed += OnFileChanged;
            var waitFlags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { 
                ["code"] = "--wait",
                ["subl"] = "--wait",
                ["atom"] = "--wait",
                ["codium"] = "--wait",
                ["kate"] = "--block",
                ["gedit"] = "--wait",
                ["geany"] = "--new-instance --wait",
                ["notepad++"] = "-multiInst -notabbar -nosession -noPlugin"
            };

            var waitFlag = waitFlags.TryGetValue(editorPath, out var flag) ? $" {flag}" : string.Empty;

            // Start the external editor
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = editorPath,
                    Arguments = $"\"{localPath}\"{waitFlag}",
                    UseShellExecute = true
                }
            };

            var startTime = DateTime.Now;
            process.Start();
            // Wait for user to cancel or editor to close
            try
            {
                while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    if (System.Console.KeyAvailable)
                    {
                        var key = System.Console.ReadKey();
                        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.K)
                        {
                            watcher.Dispose();
                            throw new OperationCanceledException();
                        }
                    }

                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled
            }

            watcher.Changed -= OnFileChanged;
            watcher.EnableRaisingEvents = false;

            if (!process.HasExited)
            {
                AnsiConsole.MarkupLine("[yellow]Stopped watching file. Editor process is still running.[/]");
            }
            else
            {
                if (DateTime.Now - startTime < TimeSpan.FromSeconds(5))
                {
                    AnsiConsole.MarkupLine("[red]Editor closed within 5 seconds of opening. If this was not intended, your selected editor may not block properly and may require an additional flag set to function properly, such as 'code --wait'.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[dim]Editor closed.[/]");
                }
            }
        }

        /// <summary>
        /// Determines the appropriate editor to use for the specified file based on the provided settings and
        /// application configuration.
        /// </summary>
        /// <remarks>If the settings specify the use of the built-in editor, or if no external editor is
        /// configured for the file type, the method returns null. Otherwise, it returns the path to the configured
        /// external editor, either from user settings or from extension-specific mappings in the application
        /// configuration.</remarks>
        /// <param name="filePath">The full path of the file for which to select an editor. The file extension is used to determine editor
        /// mappings.</param>
        /// <param name="settings">The settings that control editor selection, including whether to use the built-in editor and any
        /// user-specified external editor.</param>
        /// <returns>The path to the external editor executable to use for the file, or null if the built-in editor should be
        /// used.</returns>
        private string? GetEditorForFile(string filePath, FileEditSettings settings)
        {
            if (settings.UseBuiltinEditor)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(settings.ExternalEditor))
            {
                return settings.ExternalEditor;
            }

            var extension = Path.GetExtension(filePath)?.TrimStart('.').ToLowerInvariant() ?? string.Empty;
            var editorSettings = AppConfig.Settings.Editor;

            // Check for extension-specific mapping in the dictionary
            if (!string.IsNullOrWhiteSpace(editorSettings?.Mappings))
            {
                var mappings = editorSettings.Mappings.Split(';', StringSplitOptions.RemoveEmptyEntries).ToDictionary(s => s.Split('=')[0], s => s.Split('=')[1]);
                if(mappings.TryGetValue(extension, out var editorPath) && !string.IsNullOrWhiteSpace(editorPath))
                {
                    return editorPath;
                }
            }
            
            // Check if a default editor other than the built-in editor is configured.
            if (!string.IsNullOrWhiteSpace(editorSettings?.DefaultEditor))
            {
                return editorSettings.DefaultEditor;
            }

            // No editor configured - use built-in
            return null;
        }
    }
}
