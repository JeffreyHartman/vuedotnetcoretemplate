// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SpaServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.NodeServices.Npm;
using Microsoft.AspNetCore.NodeServices.Util;
using Microsoft.AspNetCore.SpaServices.Extensions.Util;
using Microsoft.AspNetCore.SpaServices.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MemberLoyaltyLookup.Middleware.Util;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Linq;

namespace MemberLoyaltyLookup.Middleware
{
    internal static class VueDevelopmentServerMiddleware
    {
        private const string LogCategoryName = "Microsoft.AspNetCore.SpaServices";
        private static TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(5); // This is a development-time only feature, so a very long timeout is fine

        public static void Attach(
            IApplicationBuilder appBuilder,
            string sourcePath,
            string npmScriptName)
        {            
            if (string.IsNullOrEmpty(sourcePath))
            {
                throw new ArgumentException("Cannot be null or empty", nameof(sourcePath));
            }
        
            if (string.IsNullOrEmpty(npmScriptName))
            {
                throw new ArgumentException("Cannot be null or empty", nameof(npmScriptName));
            }
                  
            var logger = LoggerFinder.GetOrCreateLogger(appBuilder, LogCategoryName);
        
            // Start Vue development server
            var portTask = StartVueDevServerAsync(appBuilder, sourcePath, npmScriptName, logger);
            var targetUriTask = portTask.ContinueWith(
                task => new UriBuilder("http", "localhost", task.Result).Uri);                    
        
            // Add middleware that waits for the Vue development server to start
            // before calling the next middleware on the chain
            appBuilder.Use(async (context, next) =>
            {
                // On each request gets its own timeout. That way, even if
                // the first request times out, subsequent requests could still work.
                var timeout = TimeSpan.FromSeconds(30); 
                await targetUriTask.WithTimeout(timeout,
                    $"The vue development server did not start listening for requests " +
                    $"within the timeout period of {timeout.Seconds} seconds. " +
                    $"Check the log output for error information.");
        
                await next();
            });
        
            // Redirect all requests for root towards the Vue development server, 
            // using the resolved targetUriTask
            appBuilder.Use(async (context, next) =>
            {
                if (context.Request.Path == "/")
                {
                    var devServerUri = await targetUriTask;
                    context.Response.Redirect(devServerUri.ToString());
                } else
                {
                    await next();
                }
            });
        }

        private static async Task<int> StartVueDevServerAsync(
            IApplicationBuilder appBuilder,
            string sourcePath, 
            string npmScriptName, 
            ILogger logger)
        {
            var portNumber = TcpPortFinder.FindAvailablePort();
            logger.LogInformation($"Starting Vue dev server on port {portNumber}...");
        
            // Inject address of .NET app as the ASPNET_URL env variable 
            // which will be read it in vue.config.js from process.env
            // NOTE: When running with IISExpress this will be empty, 
            // so you need to hardcode the URL in IISExpress as a fallback
            var addresses = appBuilder.ServerFeatures.Get<IServerAddressesFeature>().Addresses;
            var envVars = new Dictionary<string, string>
            {
                { "ASPNET_URL", addresses.Count > 0 ? addresses.First() : "" },
            };
            var npmScriptRunner = new NpmScriptRunner(
                sourcePath, npmScriptName, $"--port {portNumber} --host localhost", envVars);
            npmScriptRunner.AttachToLogger(logger);
            // the rest of the method remains unchanged, waiting to see “DONE”
            // in the script output and returning the portNumber

            using (var stdErrReader = new EventedStreamStringReader(npmScriptRunner.StdErr))
            {
                try
                {
                    // Although the React dev server may eventually tell us the URL it's listening on,
                    // it doesn't do so until it's finished compiling, and even then only if there were
                    // no compiler warnings. So instead of waiting for that, consider it ready as soon
                    // as it starts listening for requests.
                    await npmScriptRunner.StdOut.WaitForMatch(
                        new Regex("DONE", RegexOptions.None, RegexMatchTimeout));
                }
                catch (EndOfStreamException ex)
                {
                    throw new InvalidOperationException(
                        $"The script '{npmScriptName}' exited without indicating that the " +
                        $"create-react-app server was listening for requests. The error output was: " +
                        $"{stdErrReader.ReadAsString()}", ex);
                }
            }

            return portNumber;
        }
    }
}