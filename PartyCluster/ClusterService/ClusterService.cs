// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Fabric;
    using System.Fabric.Description;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Common;
    using Domain;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;

    /// <summary>
    /// Stateful service that manages the lifetime of the party clusters.
    /// </summary>
    internal class ClusterService : StatefulService, IClusterService
    {
        internal const string ClusterDictionaryName = "clusterDictionary";
        internal const string SickClusterDictionaryName = "sickClusterDictionary";
        internal const int ClusterConnectionPort = 19000;
        internal const int ClusterHttpGatewayPort = 19080;

        private readonly Random random = new Random();

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
        /// A set of service parameters. Similar to StatefulServiceInitializationParameters, but we use this type
        /// here because StatefulServiceInitializationParameters doesn't have public setters.
        /// </summary>
        private readonly StatefulServiceParameters serviceParameters;

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
        /// <param name="serviceParameters"></param>
        /// <param name="config"></param>
        public ClusterService(
            IClusterOperator clusterOperator,
            ISendMail mailer,
            IApplicationDeployService applicationDeployService,
            IReliableStateManager stateManager,
            StatefulServiceParameters serviceParameters,
            ClusterConfig config)
        {
            this.config = config;
            this.clusterOperator = clusterOperator;
            this.applicationDeployService = applicationDeployService;
            this.mailer = mailer;
            this.StateManager = stateManager;
            this.serviceParameters = serviceParameters;

            this.ConfigureService();
        }

        /// <summary>
        /// Gets a list of all currently active clusters.
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<ClusterView>> GetClusterListAsync()
        {
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            return from item in clusterDictionary
                where item.Value.Status == ClusterStatus.Ready
                orderby item.Value.CreatedOn descending
                select new ClusterView(
                    item.Key,
                    item.Value.AppCount,
                    item.Value.ServiceCount,
                    item.Value.Users.Count(),
                    this.config.MaximumUsersPerCluster,
                    this.config.MaximumClusterUptime - (DateTimeOffset.UtcNow - item.Value.CreatedOn.ToUniversalTime()));
        }

        /// <summary>
        /// Processes a request to join a cluster. 
        /// </summary>
        /// <param name="clusterId"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        public async Task JoinClusterAsync(int clusterId, string userEmail)
        {
            if (String.IsNullOrWhiteSpace(userEmail))
            {
                throw new ArgumentNullException("userEmail");
            }

            ServiceEventSource.Current.ServiceMessage(this, "Join cluster request. Cluster: {0}.", clusterId);

            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            foreach (KeyValuePair<int, Cluster> item in clusterDictionary)
            {
                if (item.Value.Users.Any(x => String.Equals(x.Email, userEmail, StringComparison.OrdinalIgnoreCase)))
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Join cluster request failed. User already exists on cluster: {0}.",
                        item.Key);

                    throw new JoinClusterFailedException(JoinClusterFailedReason.UserAlreadyJoined);
                }
            }

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalResult<Cluster> result = await clusterDictionary.TryGetValueAsync(tx, clusterId, LockMode.Update);

                if (!result.HasValue)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Join cluster request failed. Cluster does not exist. Cluster ID: {0}.",
                        clusterId);

                    throw new JoinClusterFailedException(JoinClusterFailedReason.ClusterDoesNotExist);
                }

                Cluster cluster = result.Value;

                // make sure the cluster isn't about to be deleted.
                if ((DateTimeOffset.UtcNow - cluster.CreatedOn.ToUniversalTime()) > (this.config.MaximumClusterUptime))
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Join cluster request failed. Cluster has expired. Cluster: {0}. Cluster creation time: {1}",
                        clusterId,
                        cluster.CreatedOn.ToUniversalTime());

                    throw new JoinClusterFailedException(JoinClusterFailedReason.ClusterExpired);
                }

                // make sure the cluster is ready
                if (cluster.Status != ClusterStatus.Ready)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Join cluster request failed. Cluster is not ready. Cluster: {0}. Status: {1}",
                        clusterId,
                        cluster.Status);

                    throw new JoinClusterFailedException(JoinClusterFailedReason.ClusterNotReady);
                }

                if (cluster.Users.Count() >= this.config.MaximumUsersPerCluster)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Join cluster request failed. Cluster is full. Cluster: {0}. Users: {1}",
                        clusterId,
                        cluster.Users.Count());

                    throw new JoinClusterFailedException(JoinClusterFailedReason.ClusterFull);
                }

                int userPort;
                string clusterAddress = cluster.Address;
                TimeSpan clusterTimeRemaining = this.config.MaximumClusterUptime - (DateTimeOffset.UtcNow - cluster.CreatedOn);
                DateTimeOffset clusterExpiration = cluster.CreatedOn + this.config.MaximumClusterUptime;

                try
                {
                    userPort = cluster.Ports.First(port => !cluster.Users.Any(x => x.Port == port));
                }
                catch (InvalidOperationException)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Join cluster request failed. No available ports. Cluster: {0}. Users: {1}. Ports: {2}",
                        clusterId,
                        cluster.Users.Count(),
                        cluster.Ports.Count());

                    throw new JoinClusterFailedException(JoinClusterFailedReason.NoPortsAvailable);
                }

                try
                {
                    ServiceEventSource.Current.ServiceMessage(this, "Sending join mail. Cluster: {0}.", clusterId);
                    List<HyperlinkView> links = new List<HyperlinkView>();
                    links.Add(new HyperlinkView("http://" + clusterAddress + ":" + ClusterHttpGatewayPort + "/Explorer/index.html", "Service Fabric Explorer", "explore what's on the cluster with the built-in Service Fabric Explorer."));

                    try
                    {
                        IEnumerable<ApplicationView> applications = await this.applicationDeployService.GetApplicationDeploymentsAsync(cluster.Address, ClusterConnectionPort);
                        links.AddRange(applications.Select(x => x.EntryServiceInfo));
                    }
                    catch(Exception e)
                    {
                        ServiceEventSource.Current.ServiceMessage(this, "Failed to get application deployment info. {0}.", e.GetActualMessage());
                    }
                    
                    await this.mailer.SendJoinMail(
                        userEmail,
                        clusterAddress + ":" + ClusterConnectionPort,
                        userPort,
                        clusterTimeRemaining,
                        clusterExpiration, 
                        links);
                }
                catch (Exception e)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "Failed to send join mail. {0}.", e.GetActualMessage());

                    throw new JoinClusterFailedException(JoinClusterFailedReason.SendMailFailed);
                }

                List<ClusterUser> newUserList = new List<ClusterUser>(cluster.Users);
                newUserList.Add(new ClusterUser(userEmail, userPort));

                Cluster updatedCluster = new Cluster(
                    cluster.InternalName,
                    cluster.Status,
                    cluster.AppCount,
                    cluster.ServiceCount,
                    cluster.Address,
                    cluster.Ports,
                    newUserList,
                    cluster.CreatedOn);

                await clusterDictionary.SetAsync(tx, clusterId, updatedCluster);
                await tx.CommitAsync();
            }

            ServiceEventSource.Current.ServiceMessage(this, "Join cluster request completed. Cluster: {0}.", clusterId);
        }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[] {new ServiceReplicaListener(parameters => new ServiceRemotingListener<IClusterService>(parameters, this))};
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await this.ProcessClustersAsync();

                    int target = await this.GetTargetClusterCapacityAsync();

                    await this.BalanceClustersAsync(target);
                }
                catch (TimeoutException te)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "TimeoutException in RunAsync: {0}.", te.Message);
                }

                await Task.Delay(this.config.RefreshInterval, cancellationToken);
            }
        }

        /// <summary>
        /// Adds clusters by the given amount without going over the max threshold and without resulting in below the min threshold.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        internal async Task BalanceClustersAsync(int target)
        {
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                IEnumerable<KeyValuePair<int, Cluster>> activeClusters = this.GetActiveClusters(clusterDictionary);
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
                        await clusterDictionary.AddAsync(tx, this.CreateClusterId(), new Cluster(this.CreateClusterInternalName()));
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
                        await clusterDictionary.SetAsync(tx, item.Key, new Cluster(ClusterStatus.Remove, item.Value));
                        ++ix;
                    }

                    await tx.CommitAsync();

                    ServiceEventSource.Current.ServiceMessage(this, "Balancing clusters completed. Marked for removal: {0}", ix);
                }
            }
        }

        /// <summary>
        /// Removes clusters that have been deleted from the list.
        /// </summary>
        /// <returns></returns>
        internal async Task ProcessClustersAsync()
        {
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);
            
            foreach (KeyValuePair<int, Cluster> item in clusterDictionary)
            {
                using (ITransaction tx = this.StateManager.CreateTransaction())
                {
                    try
                    {
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
                    catch (Exception e)
                    {
                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Failed to process cluster: {0}. {1}",
                            item.Value.Address,
                            e.GetActualMessage());
                    }
                }
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
        internal async Task<int> GetTargetClusterCapacityAsync()
        {
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            IEnumerable<KeyValuePair<int, Cluster>> activeClusters = this.GetActiveClusters(clusterDictionary);
            int activeClusterCount = activeClusters.Count();

            double totalCapacity = activeClusterCount*this.config.MaximumUsersPerCluster;

            double totalUsers = activeClusters
                .Aggregate(0, (total, next) => total += next.Value.Users.Count());

            double percentFull = totalUsers/totalCapacity;

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

        private async Task<Cluster> ProcessNewClusterAsync(Cluster cluster)
        {
            try
            {
                string address = await this.clusterOperator.CreateClusterAsync(cluster.InternalName);

                ServiceEventSource.Current.ServiceMessage(this, "Creating cluster: {0}", address);

                return new Cluster(
                    cluster.InternalName,
                    ClusterStatus.Creating,
                    cluster.AppCount,
                    cluster.ServiceCount,
                    address,
                    new List<int>(cluster.Ports),
                    new List<ClusterUser>(cluster.Users),
                    cluster.CreatedOn);
            }
            catch (InvalidOperationException e)
            {
                // cluster with this name might already exist, so remove this one.
                ServiceEventSource.Current.ServiceMessage(this, "Cluster failed to create: {0}. {1}", cluster.Address, e.Message);

                // mark as deleted so it gets removed from the list.
                return new Cluster(ClusterStatus.Deleted, cluster);
            }
        }

        private async Task<Cluster> ProcessCreatingClusterAsync(Cluster cluster)
        {
            ClusterOperationStatus creatingStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.InternalName);
            switch (creatingStatus)
            {
                case ClusterOperationStatus.Creating:
                    return cluster;

                case ClusterOperationStatus.Ready:
                    IEnumerable<int> ports = await this.clusterOperator.GetClusterPortsAsync(cluster.InternalName);

                    try
                    {
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
                        "Cluster is ready: {0} with ports: {1}",
                        cluster.Address,
                        String.Join(",", cluster.Ports));

                    return new Cluster(
                        cluster.InternalName,
                        ClusterStatus.Ready,
                        cluster.AppCount,
                        cluster.ServiceCount,
                        cluster.Address,
                        new List<int>(ports),
                        new List<ClusterUser>(cluster.Users),
                        DateTimeOffset.UtcNow);

                case ClusterOperationStatus.CreateFailed:
                    ServiceEventSource.Current.ServiceMessage(this, "Cluster failed to create: {0}", cluster.Address);
                    return new Cluster(ClusterStatus.Remove, cluster);

                case ClusterOperationStatus.Deleting:
                    return new Cluster(ClusterStatus.Deleting, cluster);

                default:
                    return cluster;
            }
        }

        private async Task<Cluster> ProcessReadyClusterAsync(Cluster cluster)
        {
            if (DateTimeOffset.UtcNow - cluster.CreatedOn.ToUniversalTime() >= this.config.MaximumClusterUptime)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Cluster expired: {0}", cluster.Address);
                return new Cluster(ClusterStatus.Remove, cluster);
            }

            ClusterOperationStatus readyStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.InternalName);
            switch (readyStatus)
            {
                case ClusterOperationStatus.Deleting:
                    return new Cluster(ClusterStatus.Deleting, cluster);
            }

            try
            {
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
                    cluster.CreatedOn);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Unable to determine application and service count. Cluster: {0}. Error: {1}",
                    cluster.Address,
                    e.GetActualMessage());
            }

            return cluster;
        }

        private async Task<Cluster> ProcessRemoveClusterAsync(Cluster cluster)
        {
            ClusterOperationStatus removeStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.InternalName);
            switch (removeStatus)
            {
                case ClusterOperationStatus.Creating:
                case ClusterOperationStatus.Ready:
                case ClusterOperationStatus.CreateFailed:
                case ClusterOperationStatus.DeleteFailed:
                    ServiceEventSource.Current.ServiceMessage(this, "Deleting cluster {0}.", cluster.Address);
                    await this.clusterOperator.DeleteClusterAsync(cluster.InternalName);
                    return new Cluster(ClusterStatus.Deleting, cluster);

                case ClusterOperationStatus.Deleting:
                    return new Cluster(ClusterStatus.Deleting, cluster);

                case ClusterOperationStatus.ClusterNotFound:
                    return new Cluster(ClusterStatus.Deleted, cluster);
            }

            return cluster;
        }

        private async Task<Cluster> ProcessDeletingClusterAsync(Cluster cluster)
        {
            ClusterOperationStatus deleteStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.InternalName);

            switch (deleteStatus)
            {
                case ClusterOperationStatus.Creating:
                case ClusterOperationStatus.Ready:
                    return new Cluster(ClusterStatus.Remove, cluster); // hopefully shouldn't ever get here

                case ClusterOperationStatus.Deleting:
                    return cluster;

                case ClusterOperationStatus.ClusterNotFound:
                    ServiceEventSource.Current.ServiceMessage(this, "Cluster successfully deleted: {0}.", cluster.Address);
                    return new Cluster(ClusterStatus.Deleted, cluster);

                case ClusterOperationStatus.CreateFailed:
                case ClusterOperationStatus.DeleteFailed:
                    ServiceEventSource.Current.ServiceMessage(this, "Cluster failed to delete: {0}.", cluster.Address);
                    return new Cluster(ClusterStatus.Remove, cluster);
            }

            return cluster;
        }

        private IEnumerable<KeyValuePair<int, Cluster>> GetActiveClusters(IReliableDictionary<int, Cluster> clusterDictionary)
        {
            return clusterDictionary.Where(
                x =>
                    x.Value.Status == ClusterStatus.New ||
                    x.Value.Status == ClusterStatus.Creating ||
                    x.Value.Status == ClusterStatus.Ready);
        }

        private void ConfigureService()
        {
            if (this.serviceParameters.CodePackageActivationContext != null)
            {
                this.serviceParameters.CodePackageActivationContext.ConfigurationPackageModifiedEvent
                    += this.CodePackageActivationContext_ConfigurationPackageModifiedEvent;

                ConfigurationPackage configPackage = this.serviceParameters.CodePackageActivationContext.GetConfigurationPackageObject("Config");

                this.UpdateClusterConfigSettings(configPackage.Settings);
            }
        }

        private void UpdateClusterConfigSettings(ConfigurationSettings settings)
        {
            KeyedCollection<string, ConfigurationProperty> clusterConfigParameters = settings.Sections["ClusterConfigSettings"].Parameters;

            this.config = new ClusterConfig();
            this.config.RefreshInterval = TimeSpan.Parse(clusterConfigParameters["RefreshInterval"].Value);
            this.config.MinimumClusterCount = Int32.Parse(clusterConfigParameters["MinimumClusterCount"].Value);
            this.config.MaximumClusterCount = Int32.Parse(clusterConfigParameters["MaximumClusterCount"].Value);
            this.config.MaximumUsersPerCluster = Int32.Parse(clusterConfigParameters["MaximumUsersPerCluster"].Value);
            this.config.MaximumClusterUptime = TimeSpan.Parse(clusterConfigParameters["MaximumClusterUptime"].Value);
            this.config.UserCapacityHighPercentThreshold = Double.Parse(clusterConfigParameters["UserCapacityHighPercentThreshold"].Value);
            this.config.UserCapacityLowPercentThreshold = Double.Parse(clusterConfigParameters["UserCapacityLowPercentThreshold"].Value);
        }

        private void CodePackageActivationContext_ConfigurationPackageModifiedEvent(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            this.UpdateClusterConfigSettings(e.NewPackage.Settings);
        }

        private int CreateClusterId()
        {
            return this.random.Next();
        }
        
        private string CreateClusterInternalName()
        {
            return "party" + (ushort) this.random.Next();
        }
    }
}