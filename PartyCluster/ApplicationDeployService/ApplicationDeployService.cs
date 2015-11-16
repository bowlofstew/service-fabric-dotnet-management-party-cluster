// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ApplicationDeployService
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.IO;
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
    using Newtonsoft.Json;

    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance.
    /// </summary>
    internal class ApplicationDeployService : StatefulService, IApplicationDeployService
    {
        private const string QueueName = "WorkQueue";
        private const string DictionaryName = "WorkTable";
        private readonly TimeSpan transactionTimeout = TimeSpan.FromSeconds(4);
        private readonly StatefulServiceParameters serviceParameters;
        private readonly IApplicationOperator applicationOperator;
        private string applicationPackagePath;

        /// <summary>
        /// Creates a new service class instance with the given state manager and service parameters.
        /// </summary>
        /// <param name="stateManager"></param>
        /// <param name="serviceParameters"></param>
        public ApplicationDeployService(
            IReliableStateManager stateManager, IApplicationOperator applicationOperator, StatefulServiceParameters serviceParameters)
        {
            this.StateManager = stateManager;
            this.serviceParameters = serviceParameters;
            this.applicationOperator = applicationOperator;

            this.ConfigureService();
        }

        /// <summary>
        /// A list of application packages that this service deployes to other clusters.
        /// </summary>
        internal IEnumerable<ApplicationPackageInfo> ApplicationPackages { get; set; }

        /// <summary>
        /// Queues deployment of application packages to the given cluster.
        /// </summary>
        /// <param name="cluster"></param>
        /// <returns></returns>
        public async Task<IEnumerable<Guid>> QueueApplicationDeployment(string cluster)
        {
            IReliableQueue<Guid> queue =
                await this.StateManager.GetOrAddAsync<IReliableQueue<Guid>>(QueueName);

            IReliableDictionary<Guid, ApplicationDeployment> dictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, ApplicationDeployment>>(DictionaryName);

            List<Guid> workIds = new List<Guid>(this.ApplicationPackages.Count());

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                foreach (ApplicationPackageInfo package in this.ApplicationPackages)
                {
                    Guid id = Guid.NewGuid();
                    ApplicationDeployment applicationDeployment = new ApplicationDeployment(
                        cluster,
                        ApplicationDeployStatus.Copy,
                        null,
                        package.ApplicationType,
                        package.ApplicationVersion,
                        package.DataPackageDirectoryName,
                        Path.Combine(this.applicationPackagePath, package.DataPackageDirectoryName));

                    await dictionary.AddAsync(tx, id, applicationDeployment);
                    await queue.EnqueueAsync(tx, id);

                    workIds.Add(id);
                }

                await tx.CommitAsync();

                return workIds;
            }
        }

        /// <summary>
        /// Gets the status of a deployment.
        /// </summary>
        /// <param name="deployId"></param>
        /// <returns></returns>
        public async Task<ApplicationDeployStatus> Status(Guid deployId)
        {
            IReliableDictionary<Guid, ApplicationDeployment> dictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, ApplicationDeployment>>(DictionaryName);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalResult<ApplicationDeployment> resultStatus = await dictionary.TryGetValueAsync(tx, deployId);
                if (resultStatus.HasValue)
                {
                    return resultStatus.Value.Status;
                }

                throw new KeyNotFoundException(String.Format("Deployment {0} does not exist.", deployId));
            }
        }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[] {new ServiceReplicaListener(parameters => new ServiceRemotingListener<IApplicationDeployService>(parameters, this))};
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            TimeSpan delayTime = TimeSpan.FromSeconds(5);

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        bool performedWork = await this.TryDequeueAndProcessAsync(cancellationToken);
                        delayTime = performedWork ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5);
                    }
                    catch (TimeoutException)
                    {
                        // retry-able, go again.
                    }

                    // The queue was empty, delay for a little while before looping again
                    await Task.Delay(delayTime, cancellationToken);
                }
            }
            finally
            {
                this.applicationOperator.Dispose();
            }
        }

        internal async Task<bool> TryDequeueAndProcessAsync(CancellationToken cancellationToken)
        {
            IReliableQueue<Guid> queue =
                await this.StateManager.GetOrAddAsync<IReliableQueue<Guid>>(QueueName);

            IReliableDictionary<Guid, ApplicationDeployment> dictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, ApplicationDeployment>>(DictionaryName);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalResult<Guid> workItem = await queue.TryDequeueAsync(tx, this.transactionTimeout, cancellationToken);
                if (!workItem.HasValue)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "No new application deployment requests.");
                    return false;
                }

                Guid workItemId = workItem.Value;

                ConditionalResult<ApplicationDeployment> appDeployment = await dictionary.TryGetValueAsync(tx, workItemId);
                if (!appDeployment.HasValue)
                {
                    return false;
                }

                ApplicationDeployment processedDeployment = await this.ProcessApplicationDeployment(appDeployment.Value);

                if (processedDeployment.Status != ApplicationDeployStatus.Complete)
                {
                    await queue.EnqueueAsync(tx, workItemId);
                }

                await dictionary.SetAsync(tx, workItemId, processedDeployment);
                await tx.CommitAsync();

                return true;
            }
        }

        internal async Task<ApplicationDeployment> ProcessApplicationDeployment(ApplicationDeployment applicationDeployment)
        {
            switch (applicationDeployment.Status)
            {
                case ApplicationDeployStatus.Copy:
                    string imageStorePath = await this.applicationOperator.CopyPackageToImageStoreAsync(
                        applicationDeployment.Cluster,
                        applicationDeployment.PackagePath,
                        applicationDeployment.ApplicationTypeName,
                        applicationDeployment.ApplicationTypeVersion);

                    return new ApplicationDeployment(
                        applicationDeployment.Cluster,
                        ApplicationDeployStatus.Register,
                        imageStorePath,
                        applicationDeployment.ApplicationTypeName,
                        applicationDeployment.ApplicationTypeVersion,
                        applicationDeployment.ApplicationInstanceName,
                        applicationDeployment.PackagePath);

                case ApplicationDeployStatus.Register:
                    await this.applicationOperator.RegisterApplicationAsync(
                        applicationDeployment.Cluster,
                        applicationDeployment.ImageStorePath);

                    return new ApplicationDeployment(ApplicationDeployStatus.Create, applicationDeployment);

                case ApplicationDeployStatus.Create:
                    await this.applicationOperator.CreateApplicationAsync(
                        applicationDeployment.Cluster,
                        applicationDeployment.ApplicationInstanceName,
                        applicationDeployment.ApplicationTypeName,
                        applicationDeployment.ApplicationTypeVersion);

                    return new ApplicationDeployment(ApplicationDeployStatus.Complete, applicationDeployment);

                default:
                    return applicationDeployment;
            }
        }

        private void ConfigureService()
        {
            if (this.serviceParameters.CodePackageActivationContext != null)
            {
                ConfigurationPackage configPackage = this.serviceParameters.CodePackageActivationContext.GetConfigurationPackageObject("Config");
                DataPackage dataPackage = this.serviceParameters.CodePackageActivationContext.GetDataPackageObject("ApplicationPackages");

                this.applicationPackagePath = dataPackage.Path;
                this.UpdateApplicationPackageConfig(configPackage.Path);

                this.serviceParameters.CodePackageActivationContext.ConfigurationPackageModifiedEvent +=
                    this.CodePackageActivationContext_ConfigurationPackageModifiedEvent;
            }
        }

        private void UpdateApplicationPackageConfig(string configPath)
        {
            using (StreamReader reader = new StreamReader(Path.Combine(configPath, "RequestSettings.json")))
            {
                this.ApplicationPackages = JsonConvert.DeserializeObject<IEnumerable<ApplicationPackageInfo>>(reader.ReadToEnd());
            }
        }

        private void CodePackageActivationContext_ConfigurationPackageModifiedEvent(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            this.UpdateApplicationPackageConfig(e.NewPackage.Path);
        }
    }
}