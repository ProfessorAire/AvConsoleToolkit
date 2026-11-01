// <copyright file="TerminalOutputNormalizer.cs">
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

namespace AvConsoleToolkit.Utilities
{
    /// <summary>
    /// Normalizes terminal output captured from shell streams before writing it to the console.
    /// Some devices emit carriage returns (\r) while updating the same line; those should not be
    /// rendered as raw terminal carriage returns because they cause line wrapping and duplicated output.
    /// </summary>
    internal static class TerminalOutputNormalizer
    {
        public static string NormalizeForDisplay(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            // Remove carriage-return rewrites used by devices to redraw the current line,
            // but preserve actual newline characters that are part of the remote stream.
            return value.Replace("\r", string.Empty);
        }

        public static string NormalizeCapturedOutput(string? value, string? prompt = null)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var normalized = NormalizeForDisplay(value);

            if (!string.IsNullOrWhiteSpace(prompt))
            {
                var promptWithSpace = $"{prompt} ";
                normalized = normalized.Replace(promptWithSpace, string.Empty);
                normalized = normalized.Replace(prompt, string.Empty);
            }

            while (normalized.Contains("\n\n\n"))
            {
                normalized = normalized.Replace("\n\n\n", "\n\n");
            }

            var hadTrailingNewLine = normalized.EndsWith('\n') || normalized.EndsWith('\r');
            normalized = normalized.TrimEnd('\r', '\n');

            if (hadTrailingNewLine && !string.IsNullOrEmpty(normalized))
            {
                normalized += Environment.NewLine;
            }

            return normalized;
        }
    }
}
