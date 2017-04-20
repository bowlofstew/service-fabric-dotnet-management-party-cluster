// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService
{
    using System.Fabric;
    using System.Web.Http;
    using Microsoft.Practices.Unity;
    using PartyCluster.WebService.Controllers;
    using Unity.WebApi;

    public static class UnityConfig
    {
        public static void RegisterComponents(HttpConfiguration config, StatelessServiceContext serviceContext)
        {
            UnityContainer container = new UnityContainer();
            

            config.DependencyResolver = new UnityDependencyResolver(container);
        }
    }
}