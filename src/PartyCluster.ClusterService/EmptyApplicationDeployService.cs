// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using PartyCluster.Domain;

    public class EmptyApplicationDeployService : IApplicationDeployService
    {
        public Task<int> GetApplicationCountAsync(string clusterAddress, int clusterPort)
        {
            ServiceEventSource.Current.Message(
                "EmptyApplicationDeployService.GetApplicationCountAsync:: clusterAddress {0}, clusterPort {1}", 
                clusterAddress, 
                clusterPort);
            return Task.FromResult(-1);
        }

        public Task<IEnumerable<ApplicationView>> GetApplicationDeploymentsAsync(string clusterAddress, int clusterPort)
        {
            ServiceEventSource.Current.Message(
                "EmptyApplicationDeployService.GetApplicationDeploymentsAsync:: clusterAddress {0}, clusterPort {1}",
                clusterAddress,
                clusterPort);
            return Task.FromResult(Enumerable.Empty<ApplicationView>());
        }

        public Task<int> GetServiceCountAsync(string clusterAddress, int clusterPort)
        {
            ServiceEventSource.Current.Message(
                "EmptyApplicationDeployService.GetServiceCountAsync:: clusterAddress {0}, clusterPort {1}",
                clusterAddress,
                clusterPort);
            return Task.FromResult(-1);
        }

        public Task<ApplicationDeployStatus> GetStatusAsync(Guid deployId)
        {
            ServiceEventSource.Current.Message(
                "EmptyApplicationDeployService.GetStatusAsync:: deployId {0}",
                deployId);
            return Task.FromResult(ApplicationDeployStatus.None);
        }

        public Task<IEnumerable<Guid>> QueueApplicationDeploymentAsync(string clusterAddress, int clusterPort)
        {
            ServiceEventSource.Current.Message(
                "EmptyApplicationDeployService.QueueApplicationDeploymentAsync:: clusterAddress {0}, clusterPort {1}",
                clusterAddress,
                clusterPort);
            return Task.FromResult(Enumerable.Empty<Guid>());
        }
    }
}
