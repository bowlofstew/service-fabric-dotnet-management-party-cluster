// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService
{
    using System.Collections.Generic;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using System.Fabric;
    public class WebService : StatelessService
    {
        public WebService(StatelessServiceContext context)
            : base (context)
        { }

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new[]
            {
                new ServiceInstanceListener(parameters => new OwinCommunicationListener("", new Startup(parameters), parameters))
            };
        }
    }
}