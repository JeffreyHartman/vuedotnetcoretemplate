// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;

namespace MemberLoyaltyLookup.Middleware
{
    /// <summary>
    /// Extension methods for enabling React development server middleware support.
    /// </summary>
    public static class VueDevelopmentServerMiddlewareExtensions
    {
        /// <summary>
        /// Handles requests by passing them through to an instance of the create-react-app server.
        /// This means you can always serve up-to-date CLI-built resources without having
        /// to run the create-react-app server manually.
        ///
        /// This feature should only be used in development. For production deployments, be
        /// sure not to enable the create-react-app server.
        /// </summary>
        /// <param name="appBuilder">The <see cref="IApplicationBuilder"/>.</param>
        /// <param name="npmScript">The name of the script in your package.json file that launches the create-react-app server.</param>
        public static void UseVueDevelopmentServer(
            this IApplicationBuilder appBuilder,
            string sourcePath = "ClientApp",
            string npmScript = "serve")
        {
            if (appBuilder == null)
            {
                throw new ArgumentNullException(nameof(appBuilder));
            }
        
            VueDevelopmentServerMiddleware.Attach(appBuilder, sourcePath, npmScript);
        }
    }
}