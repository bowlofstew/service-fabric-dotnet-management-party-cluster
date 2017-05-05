// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService.Middleware
{
    using System.Threading.Tasks;
    using Microsoft.Owin;

    public class CustomHeadersMiddleware : OwinMiddleware
    {
        public CustomHeadersMiddleware(OwinMiddleware next) : base(next)
        {
        }

        public override async Task Invoke(IOwinContext context)
        {
            context.Response.Headers.Add("Strict-Transport-Security", new string[] { "max-age=31536000; includeSubDomains" });
            context.Response.Headers.Add("x-content-type-options", new string[] { "nosniff" });
            context.Response.Headers.Add("x-frame-options", new string[] { "deny" });

            await this.Next.Invoke(context);
        }
    }
}
