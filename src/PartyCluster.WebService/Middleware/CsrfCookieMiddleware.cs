// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService.Middleware
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Owin;

    public class CsrfCookieMiddleware : OwinMiddleware
    {
        private readonly ConfigSettings config;

        public CsrfCookieMiddleware(OwinMiddleware next, ConfigSettings config) : base(next)
        {
            this.config = config;
        }

        public async override Task Invoke(IOwinContext context)
        {
            context.Response.OnSendingHeaders((state) =>
            {
                if (context.Request.Path == new PathString("/signin-github")
                || context.Request.Path == new PathString("/signin-facebook")
#if LOCAL
                || context.Request.Path == new PathString("/auth/login")
#endif
                )
                {
                    if (context.Response.Headers.ContainsKey("set-cookie"))
                    {
                        var cookies = context.Response.Headers.GetCommaSeparatedValues("set-cookie");
                        var authcookie = cookies.FirstOrDefault(x => x.Contains(this.config.AuthenticationTypeName));
                        if (!string.IsNullOrEmpty(authcookie))
                        {
                            var authcookieValue = authcookie.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries)[1].Split(new[] { ';' })[0];
                            var tokenProvider = new CsrfTokenProvider(this.config);
                            var csrfToken = tokenProvider.GenerateCsrfToken(authcookieValue);

                            context.Response.Cookies.Append(
                                "csrf-token",
                                csrfToken,
                                new CookieOptions()
                                {
#if !LOCAL
                                    Secure = true,
#endif
                                    Expires = DateTime.UtcNow.AddHours(1),
                                    //HttpOnly = true
                                });
                        }
                    }
                }
                else if (context.Request.Path == new PathString("/auth/logout"))
                {
                    context.Response.Cookies.Append(
                                "csrf-token",
                                string.Empty,
                                new CookieOptions()
                                {
                                    Secure = true,
                                    Expires = DateTime.UtcNow.AddDays(-1),
                                });
                }
            }, state: null);

            await this.Next.Invoke(context);
        }
    }
}
