// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services.Remoting;

    public interface IApplicationDeployService : IService
    {
        Task<IEnumerable<Guid>> QueueApplicationDeploymentAsync(string clusterAddress, int clusterPort);
        Task<ApplicationDeployStatus> GetStatusAsync(Guid deployId);
        Task<int> GetApplicationCountAsync(string clusterAddress, int clusterPort);
        Task<int> GetServiceCountAsync(string clusterAddress, int clusterPort);
    }
}