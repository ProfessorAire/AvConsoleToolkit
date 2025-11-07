// <copyright file="IShellStream.cs" company="AVI-SPL Global LLC.">
// Copyright (C) AVI-SPL Global LLC. All Rights Reserved.
// The intellectual and technical concepts contained herein are proprietary to AVI-SPL Global LLC. and subject to AVI-SPL's standard software license agreement.
// These materials may not be copied, reproduced, distributed or disclosed, in whole or in part, in any way without the written permission of an authorized
// representative of AVI-SPL. All references to AVI-SPL Global LLC. shall also be references to AVI-SPL Global LLC's affiliates.
// </copyright>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace ConsoleToolkit.Ssh
{
    /// <summary>
    /// Interface for shell stream operations to enable testability.
    /// </summary>
    internal interface IShellStream : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether data is available to read.
        /// </summary>
        bool DataAvailable { get; }

        /// <summary>
        /// Reads data from the shell stream.
        /// </summary>
        /// <returns>The data read from the stream.</returns>
        string Read();

        /// <summary>
        /// Writes a line to the shell stream.
        /// </summary>
        /// <param name="line">The line to write.</param>
        void WriteLine(string line);

        /// <summary>
        /// Asynchronously waits for the completion of a command executed on the shell stream.
        /// </summary>
        /// <param name="successPatterns">A collection of string patterns indicating successful command completion.</param>
        /// <param name="failurePatterns">A collection of string patterns indicating command failure.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for command completion. Default is 15000ms.</param>
        /// <param name="writeReceivedData">If <see langword="true"/>, writes received data to the output. Default is <see langword="true"/>.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result is <see langword="true"/> if a success pattern is matched;
        /// <see langword="false"/> if a failure pattern is matched or the operation times out.
        /// </returns>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
        Task<bool> WaitForCommandCompletionAsync(
            IEnumerable<string>? successPatterns,
            IEnumerable<string>? failurePatterns,
            CancellationToken cancellationToken,
            int timeoutMs = 15000,
            bool writeReceivedData = true);
    }
}
