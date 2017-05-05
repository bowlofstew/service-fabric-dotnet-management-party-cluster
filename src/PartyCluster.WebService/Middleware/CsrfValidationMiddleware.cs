// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService.Middleware
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Owin;

    public class CsrfValidationMiddleware : OwinMiddleware
    {
        private readonly ConfigSettings config;

        public CsrfValidationMiddleware(OwinMiddleware next, ConfigSettings config) : base(next)
        {
            this.config = config;
        }

        public async override Task Invoke(IOwinContext context)
        {
            if (context.Request.Method == "GET")
            {
                await this.Next.Invoke(context);
            }
            else
            {
                var requestCookies = context.Request.Cookies;
                if (requestCookies == null || requestCookies.Count() == 0)
                {
                    context.Response.StatusCode = (int)System.Net.HttpStatusCode.Unauthorized;
                    return;
                }

                var authCookie = requestCookies.FirstOrDefault(k => k.Key.Contains(this.config.AuthenticationTypeName));
                if (authCookie.Key == null || authCookie.Value == null)
                {
                    context.Response.StatusCode = (int)System.Net.HttpStatusCode.Unauthorized;
                    return;
                }

                if (!context.Request.Headers.ContainsKey("x-csrf-token"))
                {
                    context.Response.StatusCode = (int)System.Net.HttpStatusCode.Unauthorized;
                    return;
                }
                
                var csrfToken = context.Request.Headers.Get("x-csrf-token");
                var tokenProvider = new CsrfTokenProvider(this.config);
                var valid = tokenProvider.IsCsrfTokenValid(csrfToken, authCookie.Value);

                if (!valid)
                {
                    context.Response.StatusCode = (int)System.Net.HttpStatusCode.Unauthorized;
                    return;
                }

                await this.Next.Invoke(context);
            }
        }
    }
}