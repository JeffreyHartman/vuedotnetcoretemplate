// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using MemberLoyaltyLookup.Middleware;

// This is under the NodeServices namespace because post 2.1 it will be moved to that package
namespace MemberLoyaltyLookup.Middleware.Util
{
    /// <summary>
    /// Executes the <c>script</c> entries defined in a <c>package.json</c> file,
    /// capturing any output written to stdio.
    /// </summary>
    internal class NpmScriptRunner
    {
        public EventedStreamReader StdOut { get; }
        public EventedStreamReader StdErr { get; }

        private static Regex AnsiColorRegex = new Regex("\x001b\\[[0-9;]*m", RegexOptions.None, TimeSpan.FromSeconds(1));

        public NpmScriptRunner(string workingDirectory, string scriptName, string arguments, IDictionary<string, string> envVars)
        {
            if (string.IsNullOrEmpty(workingDirectory))
            {
                throw new ArgumentException("Cannot be null or empty.", nameof(workingDirectory));
            }

            if (string.IsNullOrEmpty(scriptName))
            {
                throw new ArgumentException("Cannot be null or empty.", nameof(scriptName));
            }

            var npmExe = "npm";
            var completeArguments = $"run {scriptName} -- {arguments ?? string.Empty}";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, the NPM executable is a .cmd file, so it can't be executed
                // directly (except with UseShellExecute=true, but that's no good, because
                // it prevents capturing stdio). So we need to invoke it via "cmd /c".
                npmExe = "cmd";
                completeArguments = $"/c npm {completeArguments}";
            }

            var processStartInfo = new ProcessStartInfo(npmExe)
            {
                Arguments = completeArguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory
            };

            if (envVars != null)
            {
                foreach (var keyValuePair in envVars)
                {
                    processStartInfo.Environment[keyValuePair.Key] = keyValuePair.Value;
                }
            }

            var process = LaunchNodeProcess(processStartInfo);
            StdOut = new EventedStreamReader(process.StandardOutput);
            StdErr = new EventedStreamReader(process.StandardError);
        }

        public void AttachToLogger(ILogger logger)
        {
            // When the NPM task emits complete lines, pass them through to the real logger
            StdOut.OnReceivedLine += line =>
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    // NPM tasks commonly emit ANSI colors, but it wouldn't make sense to forward
                    // those to loggers (because a logger isn't necessarily any kind of terminal)
                    logger.LogInformation(StripAnsiColors(line));
                }
            };

            StdErr.OnReceivedLine += line =>
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    logger.LogError(StripAnsiColors(line));
                }
            };

            // But when it emits incomplete lines, assume this is progress information and
            // hence just pass it through to StdOut regardless of logger config.
            StdErr.OnReceivedChunk += chunk =>
            {
                var containsNewline = Array.IndexOf(
                    chunk.Array, '\n', chunk.Offset, chunk.Count) >= 0;
                if (!containsNewline)
                {
                    Console.Write(chunk.Array, chunk.Offset, chunk.Count);
                }
            };
        }

        private static string StripAnsiColors(string line)
            => AnsiColorRegex.Replace(line, string.Empty);

        private static Process LaunchNodeProcess(ProcessStartInfo startInfo)
        {
            try
            {
                var process = Process.Start(startInfo);

                // See equivalent comment in OutOfProcessNodeInstance.cs for why
                process.EnableRaisingEvents = true;

                return process;
            }
            catch (Exception ex)
            {
                var message = $"Failed to start 'npm'. To resolve this:.\n\n"
                            + "[1] Ensure that 'npm' is installed and can be found in one of the PATH directories.\n"
                            + $"    Current PATH enviroment variable is: { Environment.GetEnvironmentVariable("PATH") }\n"
                            + "    Make sure the executable is in one of those directories, or update your PATH.\n\n"
                            + "[2] See the InnerException for further details of the cause.";
                throw new InvalidOperationException(message, ex);
            }
        }
    }
}