using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Domain;

namespace Mocks
{
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
