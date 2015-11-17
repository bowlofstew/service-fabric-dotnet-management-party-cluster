// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Mocks
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Domain;

    public class MockApplicationDeployService : IApplicationDeployService
    {
        public Task<int> GetApplicationCountAsync(string clusterAddress, int clusterPort)
        {
            return Task.FromResult(0);
        }

        public Task<int> GetServiceCountAsync(string clusterAddress, int clusterPort)
        {
            return Task.FromResult(0);
        }

        public Task<ApplicationDeployStatus> GetStatusAsync(Guid deployId)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Guid>> QueueApplicationDeploymentAsync(string clusterAddress, int clusterPort)
        {
            return Task.FromResult(Enumerable.Repeat(Guid.NewGuid(), 1));
        }
    }
}