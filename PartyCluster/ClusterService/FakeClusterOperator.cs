// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Domain;

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
            string domain = String.Format(this.addressFormat, name);

            this.clusters[domain] = ClusterOperationStatus.Creating;
            this.clusterCreateDelay[domain] = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(this.random.Next(2, 20));

            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(200, 1000)));

            return domain;
        }

        public Task DeleteClusterAsync(string domain)
        {
            this.clusters[domain] = ClusterOperationStatus.Deleting;

            return Task.FromResult(true);
        }

        public Task<IEnumerable<int>> GetClusterPortsAsync(string domain)
        {
            Random random = new Random();
            List<int> ports = new List<int>(this.config.MaximumUsersPerCluster);
            for (int i = 0; i < this.config.MaximumUsersPerCluster; ++i)
            {
                ports.Add(80 + i);
            }

            return Task.FromResult((IEnumerable<int>) ports);
        }

        public Task<ClusterOperationStatus> GetClusterStatusAsync(string domain)
        {
            ClusterOperationStatus status = this.clusters[domain];

            switch (status)
            {
                case ClusterOperationStatus.Creating:
                    if (DateTimeOffset.UtcNow > this.clusterCreateDelay[domain])
                    {
                        this.clusters[domain] = ClusterOperationStatus.Ready;
                    }
                    break;
                case ClusterOperationStatus.Ready:
                    this.clusters[domain] = ClusterOperationStatus.Ready;
                    break;
                case ClusterOperationStatus.Deleting:
                    this.clusters[domain] = ClusterOperationStatus.ClusterNotFound;
                    break;
            }

            return Task.FromResult(this.clusters[domain]);
        }
    }
}