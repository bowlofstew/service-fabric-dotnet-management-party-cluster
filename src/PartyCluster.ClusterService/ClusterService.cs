// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Fabric;
    using System.Fabric.Description;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting.FabricTransport.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using PartyCluster.Common;
    using PartyCluster.Domain;

    /// <summary>
    /// Stateful service that manages the lifetime of the party clusters.
    /// </summary>
    internal class ClusterService : StatefulService, IClusterService
    {
        internal const string ClusterDictionaryName = "clusterDictionary";
        internal const int ClusterConnectionPort = 19000;
        internal const int ClusterHttpGatewayPort = 19080;

        /// <summary>
        /// List of ports that are opened up on each cluster
        /// </summary>
        private static readonly IEnumerable<int> ports = new ReadOnlyCollection<int>(new int[] { 443, 8080, 8081, 8082, 8083 });

        /// <summary>
        /// A timeout value for Reliable Collection operations.
        /// The default value is 4 seconds.
        /// </summary>
        private static readonly TimeSpan transactionTimeout = TimeSpan.FromSeconds(4);

        /// <summary>
        /// Does cluster-related functions. 
        /// This could could also be written as a separate service for better modularity.
        /// </summary>
        private readonly IClusterOperator clusterOperator;

        /// <summary>
        /// A component that sends mail.
        /// </summary>
        private readonly ISendMail mailer;

        /// <summary>
        /// The service that performs application deployments for the sample apps that are automatically deployed to each cluster.
        /// This referece is a proxy that's created when the service instance is created.
        /// Note that this really only works because the application deploy service only has one partition.
        /// If that service had multiple partitions, we'd need to store a factory here to create a new proxy for each partition.
        /// </summary>
        private readonly IApplicationDeployService applicationDeployService;

        /// <summary>
        /// Config options for managing clusters.
        /// </summary>
        private ClusterConfig config;

        /// <summary>
        /// Creates a new instance of the service class.
        /// </summary>
        /// <param name="clusterOperator"></param>
        /// <param name="mailer"></param>
        /// <param name="stateManager"></param>
        /// <param name="serviceContext"></param>
        /// <param name="config"></param>
        public ClusterService(
            IClusterOperator clusterOperator,
            ISendMail mailer,
            IApplicationDeployService applicationDeployService,
            IReliableStateManager stateManager,
            StatefulServiceContext serviceContext,
            ClusterConfig config)
            : base(serviceContext, stateManager as IReliableStateManagerReplica)
        {
            this.config = config;
            this.clusterOperator = clusterOperator;
            this.applicationDeployService = applicationDeployService;
            this.mailer = mailer;
        }

        /// <summary>
        /// Gets a list of all currently active clusters.
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<ClusterView>> GetClusterListAsync()
        {
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                IEnumerable<ClusterView> x = from item in (await clusterDictionary.CreateEnumerableAsync(tx)).ToEnumerable()
                    where item.Value.Status == ClusterStatus.Ready
                    orderby item.Value.LifetimeStartedOn descending
                    select new ClusterView(
                        item.Key,
                        item.Value.AppCount,
                        item.Value.ServiceCount,
                        item.Value.Users.Count(),
                        this.config.MaximumUsersPerCluster,
                        this.config.MaximumClusterUptime - (DateTimeOffset.UtcNow - item.Value.LifetimeStartedOn.ToUniversalTime()));

                // the enumerable that's created must be enumerated within the transaction.
                return x.ToList();
            }
        }

        /// <summary>
        /// Processes a request to join a cluster. 
        /// </summary>
        /// <param name="clusterId"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        public async Task<UserView> JoinClusterAsync(int clusterId, string userId)
        {
            if (String.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentNullException("userId");
            }

            ServiceEventSource.Current.ServiceMessage(this, "Join cluster request. Cluster: {0}.", clusterId);

            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                IAsyncEnumerable<KeyValuePair<int, Cluster>> clusterAsyncEnumerable = await clusterDictionary.CreateEnumerableAsync(tx);

                var joinedCluster = await this.GetClusterByUserAsync(clusterDictionary, userId, tx);
                if (!joinedCluster.Item2.IsEmpty)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "Join cluster request failed. User already exists on cluster: {0}.", joinedCluster.Item1);
                    throw new OperationFailedException(OperationFailedReason.UserAlreadyJoined);
                }

                ConditionalValue<Cluster> result = await clusterDictionary.TryGetValueAsync(tx, clusterId, LockMode.Update);

                if (!result.HasValue)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "Join cluster request failed. Cluster does not exist. Cluster ID: {0}.", clusterId);
                    throw new OperationFailedException(OperationFailedReason.ClusterDoesNotExist);
                }

                Cluster cluster = result.Value;

                // make sure the cluster isn't about to be deleted.
                if ((DateTimeOffset.UtcNow - cluster.LifetimeStartedOn.ToUniversalTime()) > (this.config.MaximumClusterUptime))
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Join cluster request failed. Cluster has expired. Cluster: {0}. Cluster creation time: {1}",
                        clusterId,
                        cluster.LifetimeStartedOn.ToUniversalTime());

                    throw new OperationFailedException(OperationFailedReason.ClusterExpired);
                }

                // make sure the cluster is ready
                if (cluster.Status != ClusterStatus.Ready)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Join cluster request failed. Cluster is not ready. Cluster: {0}. Status: {1}",
                        clusterId,
                        cluster.Status);

                    throw new OperationFailedException(OperationFailedReason.ClusterNotReady);
                }

                if (cluster.Users.Count() >= this.config.MaximumUsersPerCluster)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Join cluster request failed. Cluster is full. Cluster: {0}. Users: {1}",
                        clusterId,
                        cluster.Users.Count());

                    throw new OperationFailedException(OperationFailedReason.ClusterFull);
                }

                List<ClusterUser> newUserList = new List<ClusterUser>(cluster.Users);
                var userPort = this.GetFreeUserPort(userId, clusterId, cluster);
                newUserList.Add(new ClusterUser(userId, userPort));

                var updatedCluster = new Cluster(
                    cluster.InternalName,
                    cluster.Status,
                    cluster.AppCount,
                    cluster.ServiceCount,
                    cluster.Address,
                    cluster.Ports,
                    newUserList,
                    cluster.CreatedOn,
                    DateTimeOffset.UtcNow);

                await clusterDictionary.SetAsync(tx, clusterId, updatedCluster);
                await tx.CommitAsync();

                ServiceEventSource.Current.ServiceMessage(this, "Join cluster request completed. Cluster: {0}.", clusterId);

                return this.GetUserView(PartyStatus.Joined, userId, clusterId, updatedCluster);
            }
        }

        public async Task<UserView> GetPartyStatusAsync(string userId)
        {
            ServiceEventSource.Current.ServiceMessage(this, "GetPartyStatus for userId: {0}.", userId);

            IReliableDictionary<int, Cluster> clusterDictionary =
               await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            using (ITransaction transaction = this.StateManager.CreateTransaction())
            {
                if (!string.IsNullOrEmpty(userId))
                {
                    var clusterTuple = await this.GetClusterByUserAsync(clusterDictionary, userId, transaction);
                    var joinedClusterId = clusterTuple.Item1;
                    var joinedCluster = clusterTuple.Item2;

                    if (!joinedCluster.IsEmpty)
                    {
                        return this.GetUserView(PartyStatus.Joined, userId, joinedClusterId, joinedCluster);
                    }
                }

                var openClusterIdList = await this.GetOpenClusterListAsync(clusterDictionary, transaction);
                var result = openClusterIdList == null || openClusterIdList.Count() == 0
                    ? new UserView(PartyStatus.Closed, userId)
                    : new UserView(PartyStatus.Open, userId);

                ServiceEventSource.Current.ServiceMessage(this, "GetPartyStatusAsync request completed. User: {0}.", userId);
                return result;
            }
        }

        public async Task<UserView> JoinRandomClusterAsync(string userId)
        {
            if (String.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentNullException("userId");
            }

            ServiceEventSource.Current.ServiceMessage(this, "Join random cluster request for userid: {0}.", userId);

            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                var result = await this.GetClusterByUserAsync(clusterDictionary, userId, tx);
                if (!result.Item2.IsEmpty)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "Join cluster request failed. User already exists on cluster: {0}.", result.Item1);
                    return this.GetUserView(PartyStatus.Joined, userId, result.Item1, result.Item2);
                }

                var keyList = await this.GetOpenClusterListAsync(clusterDictionary, tx);
                var openCluster = Cluster.Empty;
                var openClusterId = -1;
                if (keyList != null)
                {
                    foreach (var key in keyList)
                    {
                        var tuple = await this.GetClusterById(clusterDictionary, tx, key);
                        if (tuple != null && !tuple.Item2.IsEmpty)
                        {
                            openClusterId = tuple.Item1;
                            openCluster = tuple.Item2;
                            break;
                        }
                    }
                }

                if (openCluster.IsEmpty)
                {
                    ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Join cluster request failed. Cluster is full. Cluster: {0}. Users: {1}",
                            openClusterId,
                            openCluster.Users.Count());

                    return new UserView(PartyStatus.Closed, userId);
                }

                List<ClusterUser> newUserList = new List<ClusterUser>(openCluster.Users);
                var userPort = this.GetFreeUserPort(userId, openClusterId, openCluster);
                newUserList.Add(new ClusterUser(userId, userPort));

                var updatedCluster = new Cluster(
                    openCluster.InternalName,
                    openCluster.Status,
                    openCluster.AppCount,
                    openCluster.ServiceCount,
                    openCluster.Address,
                    openCluster.Ports,
                    newUserList,
                    openCluster.CreatedOn,
                    DateTimeOffset.UtcNow);

                await clusterDictionary.SetAsync(tx, openClusterId, updatedCluster);
                await tx.CommitAsync();

                ServiceEventSource.Current.ServiceMessage(this, "Join cluster request completed. User: {0}, Cluster: {1}.", userId, openClusterId);

                return this.GetUserView(PartyStatus.Joined, userId, openClusterId, updatedCluster);
            }
        }

        private UserView GetUserView(PartyStatus status, string userId, int joinedClusterId, Cluster joinedCluster)
        {
            int userPort;
            string clusterAddress = joinedCluster.Address;
            TimeSpan clusterTimeRemaining = this.config.MaximumClusterUptime - (DateTimeOffset.UtcNow - joinedCluster.LifetimeStartedOn);
            DateTimeOffset clusterExpiration = joinedCluster.LifetimeStartedOn + this.config.MaximumClusterUptime;
            userPort = joinedCluster.Users.FirstOrDefault(x => x.Email == userId).Port;

            return new UserView(status, joinedClusterId, userId, clusterAddress + ":" + ClusterConnectionPort, userPort, clusterTimeRemaining, clusterExpiration);
        }

        private int GetFreeUserPort(string userId, int joinedClusterId, Cluster joinedCluster)
        {
            try
            {
                return joinedCluster.Ports.First(port => !joinedCluster.Users.Any(x => x.Port == port));
            }
            catch (InvalidOperationException)
            {
                ServiceEventSource.Current.ServiceMessage(
                    this,
                    "No available ports. Cluster: {0}. User: {1}. Ports: {2}",
                    joinedClusterId,
                    userId,
                    joinedCluster.Ports.Count());

                throw new OperationFailedException(OperationFailedReason.NoPortsAvailable);
            }
        }

        private async Task<Tuple<int, Cluster>> GetClusterByUserAsync(IReliableDictionary<int, Cluster> clusterDictionary, string userId, ITransaction transaction)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return new Tuple<int, Cluster>(-1, Cluster.Empty);
            }

            IAsyncEnumerable<KeyValuePair<int, Cluster>> clusterAsyncEnumerable = await clusterDictionary.CreateEnumerableAsync(transaction);

            var joinedClusterId = -1;
            foreach (var item in clusterAsyncEnumerable.ToEnumerable())
            {
                if (item.Value.IsJoinedBy(userId))
                {
                    joinedClusterId = item.Key;
                    break;
                }
            }

            return await GetClusterById(clusterDictionary, transaction, joinedClusterId);
        }

        private async Task<Tuple<int, Cluster>> GetClusterById(IReliableDictionary<int, Cluster> clusterDictionary, ITransaction transaction, int clusterId)
        {
            if (clusterId == -1)
            {
                return new Tuple<int, Cluster>(-1, Cluster.Empty);
            }

            ConditionalValue<Cluster> result = await clusterDictionary.TryGetValueAsync(transaction, clusterId, LockMode.Update);

            if (!result.HasValue)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Cluster does not exist. Cluster ID: {0}.", clusterId);
                throw new OperationFailedException(OperationFailedReason.ClusterDoesNotExist);
            }

            return new Tuple<int, Cluster>(clusterId, result.Value);
        }

        private async Task<IEnumerable<int>> GetOpenClusterListAsync(IReliableDictionary<int, Cluster> clusterDictionary, ITransaction transaction)
        {
            var keys = (await clusterDictionary.CreateEnumerableAsync(transaction))
                .ToEnumerable()
                .Where(item =>
                    item.Value.Status == ClusterStatus.Ready
                    && item.Value.Users.Count() < this.config.MaximumUsersPerCluster
                    && item.Value.RemainingLifetime() >= this.config.MaximumClusterUptime)
                .Select(item => item.Key);

            // the enumerable that's created must be enumerated within the transaction.
            return keys.ToList();
        }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[]
            {
                new ServiceReplicaListener(context => new FabricTransportServiceRemotingListener(context, this))
            };
        }

        /// <summary>
        /// Main entry point for the service.
        /// This runs a continuous loop that manages the party clusters.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                ServiceEventSource.Current.ServiceMessage(this, "Cluster Service RunAsync started.");
                this.LoadConfigPackageAndSubscribe();

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        await this.ProcessClustersAsync(cancellationToken);

                        int target = await this.GetTargetClusterCapacityAsync(cancellationToken);

                        await this.BalanceClustersAsync(target, cancellationToken);
                    }
                    catch (FabricNotPrimaryException)
                    {
                        // This replica is no longer primary, so we can exit gracefully here.
                        ServiceEventSource.Current.ServiceMessage(this, "RunAsync is exiting because the replica is no longer primary.");
                        return;
                    }
                    catch (TimeoutException te)
                    {
                        // An operation timed out which is generally transient.
                        // Move on to the next iteration after a delay (set below).
                        ServiceEventSource.Current.ServiceMessage(this, "TimeoutException in RunAsync: {0}.", te.Message);
                    }
                    catch (FabricTransientException fte)
                    {
                        // Transient exceptions can be retried without faulting the replica.
                        // Instead of retrying here, simply move on to the next iteration after a delay (set below).
                        ServiceEventSource.Current.ServiceMessage(this, "FabricTransientException in RunAsync: {0}.", fte.Message);
                    }

                    await Task.Delay(this.config.RefreshInterval, cancellationToken);
                }
            }
            finally
            {
                ServiceEventSource.Current.ServiceMessage(this, "Cluster Service RunAsync ended.");
            }
        }

        /// <summary>
        /// Adds clusters by the given amount without going over the max threshold and without resulting in below the min threshold.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        internal async Task BalanceClustersAsync(int target, CancellationToken cancellationToken)
        {
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                IEnumerable<KeyValuePair<int, Cluster>> activeClusters = await this.GetActiveClusters(clusterDictionary, cancellationToken);
                int activeClusterCount = activeClusters.Count();

                if (target < this.config.MinimumClusterCount)
                {
                    target = this.config.MinimumClusterCount;
                }

                if (target > this.config.MaximumClusterCount)
                {
                    target = this.config.MaximumClusterCount;
                }

                ServiceEventSource.Current.ServiceMessage(
                    this,
                    "Balancing clusters started. Target: {0} Total active: {1}. New: {2}. Creating: {3}. Ready: {4}.",
                    target,
                    activeClusterCount,
                    activeClusters.Count(x => x.Value.Status == ClusterStatus.New),
                    activeClusters.Count(x => x.Value.Status == ClusterStatus.Creating),
                    activeClusters.Count(x => x.Value.Status == ClusterStatus.Ready));

                if (activeClusterCount < target)
                {
                    int limit = Math.Min(target, this.config.MaximumClusterCount) - activeClusterCount;

                    for (int i = 0; i < limit; ++i)
                    {
                        await
                            clusterDictionary.AddAsync(
                                tx,
                                RandomNameGenerator.GetRandomId(),
                                new Cluster(RandomNameGenerator.GetRandomNameString()),
                                transactionTimeout,
                                cancellationToken);
                    }

                    await tx.CommitAsync();

                    ServiceEventSource.Current.ServiceMessage(this, "Balancing clusters completed. Added: {0}", limit);
                }
                else if (activeClusterCount > target)
                {
                    IEnumerable<KeyValuePair<int, Cluster>> removeList = activeClusters
                        .Where(x => x.Value.Users.Count() == 0)
                        .Take(Math.Min(activeClusterCount - this.config.MinimumClusterCount, activeClusterCount - target));

                    int ix = 0;
                    foreach (KeyValuePair<int, Cluster> item in removeList)
                    {
                        await clusterDictionary.SetAsync(tx, item.Key, new Cluster(ClusterStatus.Remove, item.Value), transactionTimeout, cancellationToken);
                        ++ix;
                    }

                    await tx.CommitAsync();

                    ServiceEventSource.Current.ServiceMessage(this, "Balancing clusters completed. Marked for removal: {0}", ix);
                }
            }
        }

        /// <summary>
        /// Processes each cluster that is currently in the list of clusters managed by this service.
        /// </summary>
        /// <returns></returns>
        internal async Task ProcessClustersAsync(CancellationToken cancellationToken)
        {
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            using (ITransaction enumTx = this.StateManager.CreateTransaction())
            {
                IAsyncEnumerable<KeyValuePair<int, Cluster>> clusterAsyncEnumerable = await clusterDictionary.CreateEnumerableAsync(enumTx);

                await clusterAsyncEnumerable.ForeachAsync(
                    cancellationToken,
                    async (item) =>
                    {
                        // use a separate transaction to process each cluster,
                        // otherwise operations will begin to timeout.
                        using (ITransaction tx = this.StateManager.CreateTransaction())
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

                                await tx.CommitAsync();
                            }
                            catch (TimeoutException te)
                            {
                                // Log this and continue processing the next cluster.
                                ServiceEventSource.Current.ServiceMessage(
                                    this,
                                    "TimeoutException while processing cluster {0}. {1}",
                                    item.Value.Address,
                                    te.ToString());
                            }
                            catch (FabricTransientException fte)
                            {
                                // Log this and continue processing the next cluster.
                                ServiceEventSource.Current.ServiceMessage(
                                    this,
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
                                ServiceEventSource.Current.ServiceMessage(
                                    this,
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
                await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            IEnumerable<KeyValuePair<int, Cluster>> activeClusters = await this.GetActiveClusters(clusterDictionary, cancellationToken);
            int activeClusterCount = activeClusters.Count();

            double totalUserCapacity = activeClusterCount*this.config.MaximumUsersPerCluster;

            double activeUserCount = activeClusters
                .Aggregate(0, (total, next) => total += next.Value.Users.Count());

            double percentFull = totalUserCapacity > 0
                ? activeUserCount / totalUserCapacity
                : 0;
            
            if (percentFull >= this.config.UserCapacityHighPercentThreshold)
            {
                return Math.Min(
                    this.config.MaximumClusterCount,
                    activeClusterCount + (int) Math.Ceiling(activeClusterCount*(1 - this.config.UserCapacityHighPercentThreshold)));
            }

            if (percentFull <= this.config.UserCapacityLowPercentThreshold)
            {
                return Math.Max(
                    this.config.MinimumClusterCount,
                    activeClusterCount -
                    (int) Math.Floor(activeClusterCount*(this.config.UserCapacityHighPercentThreshold - this.config.UserCapacityLowPercentThreshold)));
            }

            return activeClusterCount;
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
                string address = await this.clusterOperator.CreateClusterAsync(cluster.InternalName, ports);

                ServiceEventSource.Current.ServiceMessage(this, "Creating cluster: {0} with ports {1}", 
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
                ServiceEventSource.Current.ServiceMessage(this, "Cluster failed to create: {0}. {1}", cluster.Address, e.Message);

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

                    // Cluster is ready to go.
                    try
                    {
                        // Queue up sample application deployment
                        await this.applicationDeployService.QueueApplicationDeploymentAsync(cluster.Address, ClusterConnectionPort);
                    }
                    catch (Exception e)
                    {
                        // couldn't queue samples for deployment, but that shouldn't prevent starting the cluster.
                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Failed to queue sample deployment. Cluster: {0} Error: {1}",
                            cluster.Address,
                            e.GetActualMessage());
                    }

                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Cluster is ready: {0}",
                        cluster.Address);

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
                    ServiceEventSource.Current.ServiceMessage(this, "Cluster failed to create: {0}", cluster.Address);
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
            if (DateTimeOffset.UtcNow - cluster.LifetimeStartedOn.ToUniversalTime() >= this.config.MaximumClusterUptime)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Cluster expired: {0}", cluster.Address);
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

            try
            {
                // Update the application and service count for the cluster.
                int deployedApplications = await this.applicationDeployService.GetApplicationCountAsync(cluster.Address, ClusterConnectionPort);
                int deployedServices = await this.applicationDeployService.GetServiceCountAsync(cluster.Address, ClusterConnectionPort);

                return new Cluster(
                    cluster.InternalName,
                    cluster.Status,
                    deployedApplications,
                    deployedServices,
                    cluster.Address,
                    cluster.Ports,
                    cluster.Users,
                    cluster.CreatedOn,
                    cluster.LifetimeStartedOn);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(
                    this,
                    "Unable to determine application and service count. Cluster: {0}. Error: {1}",
                    cluster.Address,
                    e.GetActualMessage());
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
                    ServiceEventSource.Current.ServiceMessage(this, "Deleting cluster {0}.", cluster.Address);
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
                    ServiceEventSource.Current.ServiceMessage(this, "Cluster successfully deleted: {0}.", cluster.Address);
                    return new Cluster(ClusterStatus.Deleted, cluster);

                case ClusterOperationStatus.CreateFailed:
                case ClusterOperationStatus.DeleteFailed:

                    // Failed to delete, set its status to "remove" to try again.
                    ServiceEventSource.Current.ServiceMessage(this, "Cluster failed to delete: {0}.", cluster.Address);
                    return cluster.ToRemoveState();
            }

            return cluster;
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

            using (ITransaction tx = this.StateManager.CreateTransaction())
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

        /// <summary>
        /// Loads configuration settings from a config package named "Config" and subscribes to update events.
        /// </summary>
        private void LoadConfigPackageAndSubscribe()
        {
            this.Context.CodePackageActivationContext.ConfigurationPackageModifiedEvent
                += this.CodePackageActivationContext_ConfigurationPackageModifiedEvent;

            // This service expects a config package, so fail fast if it's missing.
            // This way, if the config package is ever missing in a service upgrade, the upgrade will fail and roll back automatically.
            ConfigurationPackage configPackage = this.Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");

            this.UpdateClusterConfigSettings(configPackage.Settings);
        }

        /// <summary>
        /// Updates the service's ClusterConfig instance with new settings from the given ConfigurationSettings.
        /// </summary>
        /// <param name="settings">
        /// The ConfigurationSettings object comes from the Settings.xml file in the service's Config package.
        /// </param>
        private void UpdateClusterConfigSettings(ConfigurationSettings settings)
        {
            // All of these settings are required. 
            // If anything is missing, fail fast.
            // If a setting is missing after a service upgrade, allow this to throw so the upgrade will fail and roll back.
            KeyedCollection<string, ConfigurationProperty> clusterConfigParameters = settings.Sections["ClusterConfigSettings"].Parameters;

            ClusterConfig newConfig = new ClusterConfig();
            newConfig.RefreshInterval = TimeSpan.Parse(clusterConfigParameters["RefreshInterval"].Value);
            newConfig.MinimumClusterCount = Int32.Parse(clusterConfigParameters["MinimumClusterCount"].Value);
            newConfig.MaximumClusterCount = Int32.Parse(clusterConfigParameters["MaximumClusterCount"].Value);
            newConfig.MaximumUsersPerCluster = Int32.Parse(clusterConfigParameters["MaximumUsersPerCluster"].Value);
            newConfig.MaximumClusterUptime = TimeSpan.Parse(clusterConfigParameters["MaximumClusterUptime"].Value);
            newConfig.UserCapacityHighPercentThreshold = Double.Parse(clusterConfigParameters["UserCapacityHighPercentThreshold"].Value);
            newConfig.UserCapacityLowPercentThreshold = Double.Parse(clusterConfigParameters["UserCapacityLowPercentThreshold"].Value);

            this.config = newConfig;
        }

        /// <summary>
        /// Handler for config package updates.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CodePackageActivationContext_ConfigurationPackageModifiedEvent(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            this.UpdateClusterConfigSettings(e.NewPackage.Settings);
        }
    }
}