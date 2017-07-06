// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService.Pool
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Fabric;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Common;
    using Domain;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;

    /// <summary>
    /// Represents a pool of clusters in the party clusters app.
    /// </summary>
    public class ClusterPool
    {
        /// <summary>
        /// List of ports that are opened up on each cluster
        /// </summary>
        private static readonly IEnumerable<int> ports = new ReadOnlyCollection<int>(new int[] { 443, 8080, 8081, 8082, 8083 });

        /// <summary>
        /// A timeout value for Reliable Collection operations.
        /// The default value is 4 seconds.
        /// </summary>
        private static readonly TimeSpan transactionTimeout = TimeSpan.FromSeconds(4);

        private IReliableStateManager stateManager;
        private IClusterOperator clusterOperator;
        private Platform platform;

        public ClusterPool(Platform platform, string poolName, IReliableStateManager stateManager, 
            string dictionaryName, IClusterOperator clusterOperator, ClusterConfig clusterConfig)
        {
            if (string.IsNullOrEmpty(poolName))
            {
                throw new ArgumentNullException(nameof(poolName));
            }

            if (stateManager == null)
            {
                throw new ArgumentNullException(nameof(stateManager));
            }

            if (string.IsNullOrEmpty(dictionaryName))
            {
                throw new ArgumentNullException(nameof(dictionaryName));
            }

            if (clusterOperator == null)
            {
                throw new ArgumentNullException(nameof(clusterOperator));
            }

            if (clusterConfig == null)
            {
                throw new ArgumentNullException(nameof(clusterConfig));
            }

            this.platform = platform;
            this.Name = poolName;
            this.stateManager = stateManager;
            this.DictionaryName = dictionaryName;
            this.clusterOperator = clusterOperator;
            this.Config = clusterConfig;
        }

        public string Name { get; private set; }

        public string DictionaryName { get; private set; }

        public ClusterConfig Config { get; set; }

        /// <summary>
        /// Adds clusters by the given amount without going over the max threshold and without resulting in below the min threshold.
        /// </summary>
        /// <param name="target">
        /// <returns></returns>
        public async Task BalanceClustersAsync(int target, CancellationToken cancellationToken)
        {
            Trace.Message("{0}.BalanceClustersAsync distionaryName {1}", this.Name, this.DictionaryName);
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(this.DictionaryName);

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                IEnumerable<KeyValuePair<int, Cluster>> activeClusters = await this.GetActiveClusters(clusterDictionary, cancellationToken);
                int activeClusterCount = activeClusters.Count();

                if (target < this.Config.MinimumClusterCount)
                {
                    target = this.Config.MinimumClusterCount;
                }

                if (target > this.Config.MaximumClusterCount)
                {
                    target = this.Config.MaximumClusterCount;
                }

                Trace.Message(
                    this.Name + ": Balancing clusters started. Target: {0} Total active: {1}. New: {2}. Creating: {3}. Ready: {4}.",
                    target,
                    activeClusterCount,
                    activeClusters.Count(x => x.Value.Status == ClusterStatus.New),
                    activeClusters.Count(x => x.Value.Status == ClusterStatus.Creating),
                    activeClusters.Count(x => x.Value.Status == ClusterStatus.Ready));

                if (activeClusterCount < target)
                {
                    int limit = Math.Min(target, this.Config.MaximumClusterCount) - activeClusterCount;
                    string prefix = this.platform == Platform.Windows ? "win" : "lnx";
                    for (int i = 0; i < limit; ++i)
                    {
                        var newName = RandomNameGenerator.GetRandomNameString(prefix);
                        Trace.Message("{0}.BalanceClustersAsync newClusterName {1}", this.Name, newName);
                        await
                            clusterDictionary.AddAsync(
                                tx,
                                RandomNameGenerator.GetRandomId(),
                                new Cluster(newName),
                                transactionTimeout,
                                cancellationToken);
                    }

                    await tx.CommitAsync();

                    Trace.Message(this.Name + ": Balancing clusters completed. Added: {0}", limit);
                }
                else if (activeClusterCount > target)
                {
                    IEnumerable<KeyValuePair<int, Cluster>> removeList = activeClusters
                        .Where(x => x.Value.Users.Count() == 0)
                        .Take(Math.Min(activeClusterCount - this.Config.MinimumClusterCount, activeClusterCount - target));

                    int ix = 0;
                    foreach (KeyValuePair<int, Cluster> item in removeList)
                    {
                        await clusterDictionary.SetAsync(tx, item.Key, new Cluster(ClusterStatus.Remove, item.Value), transactionTimeout, cancellationToken);
                        ++ix;
                    }

                    await tx.CommitAsync();

                    Trace.Message(this.Name + ": Balancing clusters completed. Marked for removal: {0}", ix);
                }
            }
        }

        /// <summary>
        /// Processes each cluster that is currently in the list of clusters managed by this service.
        /// </summary>
        /// <returns></returns>
        public async Task ProcessClustersAsync(CancellationToken cancellationToken)
        {
            Trace.Message("{0}.ProcessClustersAsync distionaryName {1}", this.Name, this.DictionaryName);
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(this.DictionaryName);

            using (ITransaction enumTx = this.stateManager.CreateTransaction())
            {
                IAsyncEnumerable<KeyValuePair<int, Cluster>> clusterAsyncEnumerable = await clusterDictionary.CreateEnumerableAsync(enumTx);

                await clusterAsyncEnumerable.ForeachAsync(
                    cancellationToken,
                    async (item) =>
                    {
                        // use a separate transaction to process each cluster,
                        // otherwise operations will begin to timeout.
                        using (ITransaction tx = this.stateManager.CreateTransaction())
                        {
                            try
                            {
                                // The Cluster struct is immutable so that we don't end up with a partially-updated Cluster
                                // in local memory in case the transaction doesn't complete.
                                Cluster updatedCluster = await this.ProcessClusterStatusAsync(item.Value);

                                if (updatedCluster.Status == ClusterStatus.Deleted)
                                {
                                    await clusterDictionary.TryRemoveAsync(tx, item.Key);
                                }
                                else
                                {
                                    await clusterDictionary.SetAsync(tx, item.Key, updatedCluster);
                                }

                                Trace.Message("{0}.ProcessClustersAsync completed. UpdatedClusterName: {1}, Status {2}", 
                                    this.Name, updatedCluster.InternalName, updatedCluster.Status);

                                await tx.CommitAsync();
                            }
                            catch (TimeoutException te)
                            {
                                // Log this and continue processing the next cluster.
                                Trace.Error(
                                    "TimeoutException while processing cluster {0}. {1}",
                                    item.Value.Address,
                                    te.ToString());
                            }
                            catch (FabricTransientException fte)
                            {
                                // Log this and continue processing the next cluster.
                                Trace.Error(
                                    "FabricTransientException while processing cluster {0}. {1}",
                                    item.Value.Address,
                                    fte.ToString());
                            }
                            catch (OperationCanceledException)
                            {
                                // this means the service needs to shut down. Make sure it gets re-thrown.
                                throw;
                            }
                            catch (Exception e)
                            {
                                Trace.Error(
                                    "Failed to process cluster: {0}. {1}",
                                    item.Value.Address,
                                    e.GetActualMessage());

                                // this could be fatal and we don't know how to handle it here,
                                // so rethrow and let the service fail over.
                                throw;
                            }
                        }
                    });
            }
        }

        /// <summary>
        /// Processes a cluster based on its current state.
        /// </summary>
        /// <returns></returns>
        internal Task<Cluster> ProcessClusterStatusAsync(Cluster cluster)
        {
            switch (cluster.Status)
            {
                case ClusterStatus.New:
                    return this.ProcessNewClusterAsync(cluster);

                case ClusterStatus.Creating:
                    return this.ProcessCreatingClusterAsync(cluster);

                case ClusterStatus.Ready:
                    return this.ProcessReadyClusterAsync(cluster);

                case ClusterStatus.Remove:
                    return this.ProcessRemoveClusterAsync(cluster);

                case ClusterStatus.Deleting:
                    return this.ProcessDeletingClusterAsync(cluster);

                default:
                    return Task.FromResult(cluster);
            }
        }

        /// <summary>
        /// Processes a new cluster.
        /// </summary>
        /// <param name="cluster"></param>
        /// <returns></returns>
        private async Task<Cluster> ProcessNewClusterAsync(Cluster cluster)
        {
            try
            {
                string address = await this.clusterOperator.CreateClusterAsync(this.platform, cluster.InternalName, ports);

                Trace.Message(
                    "Creating cluster: {0} with ports {1}",
                    address,
                    String.Join(",", ports));

                return new Cluster(
                    cluster.InternalName,
                    ClusterStatus.Creating,
                    cluster.AppCount,
                    cluster.ServiceCount,
                    address,
                    ports,
                    new List<ClusterUser>(cluster.Users),
                    cluster.CreatedOn,
                    cluster.LifetimeStartedOn);
            }
            catch (InvalidOperationException e)
            {
                // cluster with this name might already exist, so remove this one.
                Trace.Error("Cluster failed to create: {0}. {1}", cluster.Address, e.Message);

                // mark as deleted so it gets removed from the list.
                return new Cluster(ClusterStatus.Deleted, cluster);
            }
        }

        /// <summary>
        /// Processes a cluster in the "Creating" stage.
        /// </summary>
        /// <param name="cluster"></param>
        /// <returns></returns>
        private async Task<Cluster> ProcessCreatingClusterAsync(Cluster cluster)
        {
            ClusterOperationStatus creatingStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.InternalName);

            switch (creatingStatus)
            {
                case ClusterOperationStatus.Creating:

                    // Still creating, no updates necessary.
                    return cluster;

                case ClusterOperationStatus.Ready:

                    Trace.Message("Cluster is ready: {0}", cluster.Address);

                    return new Cluster(
                        cluster.InternalName,
                        ClusterStatus.Ready,
                        cluster.AppCount,
                        cluster.ServiceCount,
                        cluster.Address,
                        cluster.Ports,
                        new List<ClusterUser>(cluster.Users),
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.MaxValue);

                case ClusterOperationStatus.CreateFailed:

                    // Failed to create the cluster, so remove it.
                    // Processing will add a new one in the next iteration if we need more.
                    Trace.Message("Cluster failed to create: {0}", cluster.Address);
                    return cluster.ToRemoveState();

                case ClusterOperationStatus.Deleting:

                    // Cluster is being deleted.
                    return new Cluster(ClusterStatus.Deleting, cluster);

                case ClusterOperationStatus.ClusterNotFound:

                    // Cluster was deleted before it finished being created.
                    return new Cluster(ClusterStatus.Deleted, cluster);

                default:
                    return cluster;
            }
        }

        /// <summary>
        /// Processes clusters in the "Ready" stage.
        /// </summary>
        /// <param name="cluster"></param>
        /// <returns></returns>
        private async Task<Cluster> ProcessReadyClusterAsync(Cluster cluster)
        {
            // Check for expiration. If the cluster has expired, mark it for removal.
            if (DateTimeOffset.UtcNow - cluster.LifetimeStartedOn.ToUniversalTime() >= this.Config.MaximumClusterUptime)
            {
                Trace.Message("Cluster expired: {0}", cluster.Address);
                return cluster.ToRemoveState();
            }

            ClusterOperationStatus readyStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.InternalName);

            switch (readyStatus)
            {
                case ClusterOperationStatus.Deleting:

                    // If the cluster was deleted, mark the state accordingly
                    return new Cluster(ClusterStatus.Deleting, cluster);

                case ClusterOperationStatus.ClusterNotFound:

                    // Cluster was already deleted.
                    return new Cluster(ClusterStatus.Deleted, cluster);
            }

            return cluster;
        }

        /// <summary>
        /// Processes a cluster in the "Remove" stage.
        /// </summary>
        /// <param name="cluster"></param>
        /// <returns></returns>
        private async Task<Cluster> ProcessRemoveClusterAsync(Cluster cluster)
        {
            ClusterOperationStatus removeStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.InternalName);

            switch (removeStatus)
            {
                case ClusterOperationStatus.Creating:
                case ClusterOperationStatus.Ready:
                case ClusterOperationStatus.CreateFailed:
                case ClusterOperationStatus.DeleteFailed:

                    // In any of these cases, instruct the operator to delete the cluster.
                    Trace.Message("Deleting cluster {0}.", cluster.Address);
                    await this.clusterOperator.DeleteClusterAsync(cluster.InternalName);
                    return new Cluster(ClusterStatus.Deleting, cluster);

                case ClusterOperationStatus.Deleting:

                    // If the cluster is now being deleted, update the status accordingly.
                    return new Cluster(ClusterStatus.Deleting, cluster);

                case ClusterOperationStatus.ClusterNotFound:

                    // Cluster was already deleted.
                    return new Cluster(ClusterStatus.Deleted, cluster);
            }

            return cluster;
        }

        /// <summary>
        /// Processes a cluster that is in the "Deleting" stage. 
        /// </summary>
        /// <param name="cluster"></param>
        /// <returns></returns>
        private async Task<Cluster> ProcessDeletingClusterAsync(Cluster cluster)
        {
            ClusterOperationStatus deleteStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.InternalName);

            switch (deleteStatus)
            {
                case ClusterOperationStatus.Creating:
                case ClusterOperationStatus.Ready:

                    // hopefully shouldn't ever get here
                    return cluster.ToRemoveState();

                case ClusterOperationStatus.Deleting:

                    // still deleting, no updates necessary
                    return cluster;

                case ClusterOperationStatus.ClusterNotFound:

                    // If the cluster can't be found, it's been deleted.
                    Trace.Message("Cluster successfully deleted: {0}.", cluster.Address);
                    return new Cluster(ClusterStatus.Deleted, cluster);

                case ClusterOperationStatus.CreateFailed:
                case ClusterOperationStatus.DeleteFailed:

                    // Failed to delete, set its status to "remove" to try again.
                    Trace.Message("Cluster failed to delete: {0}.", cluster.Address);
                    return cluster.ToRemoveState();
            }

            return cluster;
        }

        /// <summary>
        /// Determines how many clusters there should be based on user activity and min/max thresholds.
        /// </summary>
        /// <remarks>
        /// When the user count goes below the low percent threshold, decrease capacity by (high - low)%
        /// When the user count goes above the high percent threshold, increase capacity by (1 - high)%
        /// </remarks>
        /// <returns></returns>
        internal async Task<int> GetTargetClusterCapacityAsync(CancellationToken cancellationToken)
        {
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(this.DictionaryName);

            IEnumerable<KeyValuePair<int, Cluster>> activeClusters = await this.GetActiveClusters(clusterDictionary, cancellationToken);
            int activeClusterCount = activeClusters.Count();

            Trace.Message("{0}.GetTargetClusterCapacityAsync distionaryName {1}, currentActiveClusterCount {2}.", 
                this.Name, this.DictionaryName, activeClusterCount);

            double totalUserCapacity = activeClusterCount * this.Config.MaximumUsersPerCluster;

            double activeUserCount = activeClusters
                .Aggregate(0, (total, next) => total += next.Value.Users.Count());

            double percentFull = totalUserCapacity > 0
                ? activeUserCount / totalUserCapacity
                : 0;

            if (percentFull >= this.Config.UserCapacityHighPercentThreshold)
            {
                return Math.Min(
                    this.Config.MaximumClusterCount,
                    activeClusterCount + (int)Math.Ceiling(activeClusterCount * (1 - this.Config.UserCapacityHighPercentThreshold)));
            }

            if (percentFull <= this.Config.UserCapacityLowPercentThreshold)
            {
                return Math.Max(
                    this.Config.MinimumClusterCount,
                    activeClusterCount -
                    (int)Math.Floor(activeClusterCount * (this.Config.UserCapacityHighPercentThreshold - this.Config.UserCapacityLowPercentThreshold)));
            }

            Trace.Message("{0}.GetTargetClusterCapacityAsync distionaryName {1}, activeClusterCount {2}.", 
                this.Name, this.DictionaryName, activeClusterCount);
            return activeClusterCount;
        }

        /// <summary>
        /// Gets a list of active clusters. Clusters that are new, being created, or ready and not expired are considered active.
        /// </summary>
        /// <param name="clusterDictionary"></param>
        /// <returns></returns>
        private async Task<IEnumerable<KeyValuePair<int, Cluster>>> GetActiveClusters(
            IReliableDictionary<int, Cluster> clusterDictionary, CancellationToken cancellationToken)
        {
            List<KeyValuePair<int, Cluster>> activeClusterList = new List<KeyValuePair<int, Cluster>>();

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                IAsyncEnumerable<KeyValuePair<int, Cluster>> clusterAsyncEnumerable = await clusterDictionary.CreateEnumerableAsync(tx);

                await clusterAsyncEnumerable.ForeachAsync(
                    cancellationToken,
                    x =>
                    {
                        if (x.Value.Status == ClusterStatus.New ||
                            x.Value.Status == ClusterStatus.Creating ||
                            x.Value.Status == ClusterStatus.Ready)
                        {
                            activeClusterList.Add(x);
                        }
                    });
            }

            return activeClusterList;
        }
    }
}