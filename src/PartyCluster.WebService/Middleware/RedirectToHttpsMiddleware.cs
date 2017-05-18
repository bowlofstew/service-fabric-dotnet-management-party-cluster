// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService.Middleware
{
    using System;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Owin;

    public class RedirectToHttpsMiddleware : OwinMiddleware
    {
        public RedirectToHttpsMiddleware(OwinMiddleware next) : base(next)
        {
        }

        public override async Task Invoke(IOwinContext context)
        {
            if (context.Request.Uri.Scheme == "http")
            {
                var requestUri = context.Request.Uri;
                var responseUri = string.Format("https://{0}:8081{1}", requestUri.Authority, requestUri.PathAndQuery);
                context.Response.Redirect(responseUri);
            }

            await this.Next.Invoke(context);
        }
    }
}
 