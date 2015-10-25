using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Domain;

namespace ClusterService
{
    internal class FakeClusterOperator : IClusterOperator
    {
        private Dictionary<string, ClusterOperationStatus> clusters = new Dictionary<string, ClusterOperationStatus>();
        private Dictionary<string, DateTimeOffset> clusterCreateDelay = new Dictionary<string, DateTimeOffset>();
        private string addressFormat = "cluster-{0}.westus.cloudapp.azure.com";
        private ClusterConfig config;
        private Random random = new Random();

        public FakeClusterOperator(ClusterConfig config)
        {
            this.config = config;
        }
        public async Task<string> CreateClusterAsync(string name)
        {
            string domain = String.Format(addressFormat, name);

            clusters[domain] = ClusterOperationStatus.Creating;
            clusterCreateDelay[domain] = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(random.Next(2, 20));

            await Task.Delay(TimeSpan.FromMilliseconds(random.Next(200, 1000)));

            return domain;
        }

        public Task DeleteClusterAsync(string domain)
        {
            clusters[domain] = ClusterOperationStatus.Deleting;

            return Task.FromResult(true);
        }

        public Task<IEnumerable<int>> GetClusterPortsAsync(string domain)
        {
            Random random = new Random();
            List<int> ports = new List<int>(config.MaximumUsersPerCluster);
            for (int i = 0; i < config.MaximumUsersPerCluster; ++i)
            {
                ports.Add(80 + i);
            }

            return Task.FromResult((IEnumerable<int>)ports);
        }

        public Task<ClusterOperationStatus> GetClusterStatusAsync(string domain)
        {
            ClusterOperationStatus status = clusters[domain];

            switch (status)
            {
                case ClusterOperationStatus.Creating:
                    if (DateTimeOffset.UtcNow > clusterCreateDelay[domain])
                    {
                        clusters[domain] = ClusterOperationStatus.Ready;
                    }
                    break;
                case ClusterOperationStatus.Ready:
                    clusters[domain] = ClusterOperationStatus.Ready;
                    break;
                case ClusterOperationStatus.Deleting:
                    clusters[domain] = ClusterOperationStatus.ClusterNotFound;
                    break;
            }

            return Task.FromResult(clusters[domain]);
        }
    }
}
