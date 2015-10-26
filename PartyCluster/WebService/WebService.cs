// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService
{
    using System.Collections.Generic;
    using Microsoft.ServiceFabric.Services;

    public class WebService : StatelessService
    {
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new[]
            {
                new ServiceInstanceListener(parameters => new OwinCommunicationListener("partyclusters", new Startup(), parameters))
            };
        }
    }
}