// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using PartyCluster.Domain;

    internal class FakeClusterOperator : IClusterOperator
    {
        private readonly IReliableStateManager stateManager;
        private string addressFormat = "localhost";
        private Random random = new Random();

        public FakeClusterOperator(IReliableStateManager stateManager)
        {
            this.stateManager = stateManager;
        }

        public async Task<string> CreateClusterAsync(Platform platform, string name, IEnumerable<int> ports)
        {
            IReliableDictionary<string, ClusterOperationStatus> clusters =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ClusterOperationStatus>>(new Uri("fakeclusterops:/clusters"));

            IReliableDictionary<string, DateTimeOffset> clusterCreateDelay =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, DateTimeOffset>>(new Uri("fakeclusterops:/clusterCreateDelay"));

            string domain = this.addressFormat;

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                await clusters.SetAsync(tx, name, ClusterOperationStatus.Creating);
                await clusterCreateDelay.SetAsync(tx, name, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(this.random.Next(2, 20)));
                await tx.CommitAsync();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(200, 1000)));

            return domain;
        }

        public async Task DeleteClusterAsync(string name)
        {
            IReliableDictionary<string, ClusterOperationStatus> clusters =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ClusterOperationStatus>>(new Uri("fakeclusterops:/clusters"));

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                await clusters.SetAsync(tx, name, ClusterOperationStatus.Deleting);
                await tx.CommitAsync();
            }
        }

        public async Task<ClusterOperationStatus> GetClusterStatusAsync(string name)
        {
            IReliableDictionary<string, ClusterOperationStatus> clusters =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ClusterOperationStatus>>(new Uri("fakeclusterops:/clusters"));

            IReliableDictionary<string, DateTimeOffset> clusterCreateDelay =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, DateTimeOffset>>(new Uri("fakeclusterops:/clusterCreateDelay"));

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                ClusterOperationStatus status = (await clusters.TryGetValueAsync(tx, name)).Value;
                DateTimeOffset clusterDelay = (await clusterCreateDelay.TryGetValueAsync(tx, name)).Value;
                ClusterOperationStatus newStatus = ClusterOperationStatus.Ready;

                switch (status)
                {
                    case ClusterOperationStatus.Creating:
                        if (DateTimeOffset.UtcNow > clusterDelay)
                        {
                            newStatus = ClusterOperationStatus.Ready;
                        }
                        break;
                    case ClusterOperationStatus.Ready:
                        newStatus = ClusterOperationStatus.Ready;
                        break;
                    case ClusterOperationStatus.Deleting:
                        newStatus = ClusterOperationStatus.ClusterNotFound;
                        break;
                }

                await clusters.SetAsync(tx, name, newStatus);
                await tx.CommitAsync();

                return newStatus;
            }
        }
    }
}