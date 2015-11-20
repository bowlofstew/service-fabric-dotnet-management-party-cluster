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
    using System.IO.Compression;
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
        private readonly TimeSpan DeploymentRetainment = TimeSpan.FromMinutes(30);
        private readonly StatefulServiceParameters serviceParameters;
        private readonly IApplicationOperator applicationOperator;
        private CancellationToken replicaRunCancellationToken;
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
        /// <param name="clusterAddress"></param>
        /// <param name="connectionPort"></param>
        /// <returns></returns>
        public async Task<IEnumerable<Guid>> QueueApplicationDeploymentAsync(string clusterAddress, int clusterPort)
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
                    string packageDirectory = GetPackageDirectoryName(package.PackageFileName);

                    Guid id = Guid.NewGuid();
                    ApplicationDeployment applicationDeployment = new ApplicationDeployment(
                        GetClusterAddress(clusterAddress, clusterPort),
                        ApplicationDeployStatus.Copy,
                        null,
                        package.ApplicationTypeName,
                        package.ApplicationTypeVersion,
                        packageDirectory,
                        Path.Combine(this.applicationPackagePath, packageDirectory),
                        DateTimeOffset.UtcNow);

                    await dictionary.AddAsync(tx, id, applicationDeployment);
                    await queue.EnqueueAsync(tx, id);

                    workIds.Add(id);
                }

                await tx.CommitAsync();

                return workIds;
            }
        }

        /// <summary>
        /// Gets information about all the applications that are deployed by this service.
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<ApplicationView>> GetApplicationDeploymentsAsync(string clusterAddress, int clusterPort)
        {
            List<ApplicationView> applications = new List<ApplicationView>(this.ApplicationPackages.Count());

            foreach (ApplicationPackageInfo package in this.ApplicationPackages)
            {
                try
                {
                    string address = await this.applicationOperator.GetServiceEndpoint(clusterAddress + ":19000", new Uri(package.EntryServiceInstanceUri), package.EntryServiceEndpointName);

                    UriBuilder builder = new UriBuilder(address);
                    builder.Host = clusterAddress;

                    applications.Add(new ApplicationView(
                                       new HyperlinkView(
                                           builder.ToString(),
                                           GetPackageDirectoryName(package.PackageFileName),
                                           package.ApplicationDescription)));
                }
                catch (FabricServiceNotFoundException)
                {
                    // service isn't deployed yet, skip it.
                }
                catch(Exception e)
                {
                    ServiceEventSource.Current.ServiceMessage(this,
                        "Failed to get an endpoint address. Application: {0}. Service: {1} Cluster: {2}. Error: {3}.",
                        package.PackageFileName,
                        package.EntryServiceInstanceUri,
                        clusterAddress,
                        e.GetActualMessage());
                }
            }

            return applications;
        }

        /// <summary>
        /// Gets the status of a deployment.
        /// </summary>
        /// <param name="deployId"></param>
        /// <returns></returns>
        public async Task<ApplicationDeployStatus> GetStatusAsync(Guid deployId)
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

        public Task<int> GetApplicationCountAsync(string clusterAddress, int clusterPort)
        {
            return this.applicationOperator.GetApplicationCountAsync(GetClusterAddress(clusterAddress, clusterPort), this.replicaRunCancellationToken);
        }

        public Task<int> GetServiceCountAsync(string clusterAddress, int clusterPort)
        {
            return this.applicationOperator.GetServiceCountAsync(GetClusterAddress(clusterAddress, clusterPort), this.replicaRunCancellationToken);
        }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[] { new ServiceReplicaListener(parameters => new ServiceRemotingListener<IApplicationDeployService>(parameters, this)) };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            TimeSpan delayTime = TimeSpan.FromSeconds(5);
            this.replicaRunCancellationToken = cancellationToken;

            try
            {
                ServiceEventSource.Current.ServiceMessage(this, "Application deployment request processing started.");

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

                    //TODO: periodically remove completed deployments so the dictionary doesn't grow indefinitely.
                }
            }
            finally
            {
                this.applicationOperator.Dispose();

                ServiceEventSource.Current.ServiceMessage(this, "Application deployment request processing ended.");
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
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Found queued application deployment request with no associated deployment information. Discarding.");
                    return true;
                }

                try
                {
                    ApplicationDeployment processedDeployment = await this.ProcessApplicationDeployment(appDeployment.Value, cancellationToken);

                    if (processedDeployment.Status != ApplicationDeployStatus.Complete)
                    {
                        await queue.EnqueueAsync(tx, workItemId);
                    }

                    await dictionary.SetAsync(tx, workItemId, processedDeployment);

                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Application deployment request successfully processed. Cluster: {0}. Status: {1}",
                        processedDeployment.Cluster,
                        processedDeployment.Status);
                }
                catch (FileNotFoundException fnfe)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Found corrupt application package. Package: {0}. Error: {1}",
                        appDeployment.Value.PackagePath,
                        fnfe.Message);

                    // corrupt application, remove it so we don't keep trying to reprocess it.
                    await dictionary.TryRemoveAsync(tx, workItemId);
                }
                catch (FabricElementAlreadyExistsException)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "Application package is already registered. Package: {0}.", appDeployment.Value.PackagePath);

                    // application already exists, remove it so we don't keep failing on this.
                    await dictionary.TryRemoveAsync(tx, workItemId);
                }
                catch (Exception e)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Application package processing failed. Package: {0}. Error: {1}",
                        appDeployment.Value.PackagePath,
                        e.ToString());

                    //TODO: for now, remove any requests that fail to deploy so the service doesn't get stuck on any one deployment.
                    await dictionary.TryRemoveAsync(tx, workItemId);
                }

                await tx.CommitAsync();

                return true;
            }
        }

        internal async Task<ApplicationDeployment> ProcessApplicationDeployment(ApplicationDeployment applicationDeployment, CancellationToken cancellationToken)
        {
            switch (applicationDeployment.Status)
            {
                case ApplicationDeployStatus.Copy:
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Application deployment: Copying to image store. Cluster: {0}. Application: {1}. Package path: {2}",
                        applicationDeployment.Cluster,
                        applicationDeployment.ApplicationInstanceName,
                        applicationDeployment.PackagePath);

                    string imageStorePath = await this.applicationOperator.CopyPackageToImageStoreAsync(
                        applicationDeployment.Cluster,
                        applicationDeployment.PackagePath,
                        applicationDeployment.ApplicationTypeName,
                        applicationDeployment.ApplicationTypeVersion,
                        cancellationToken);

                    return new ApplicationDeployment(
                        applicationDeployment.Cluster,
                        ApplicationDeployStatus.Register,
                        imageStorePath,
                        applicationDeployment.ApplicationTypeName,
                        applicationDeployment.ApplicationTypeVersion,
                        applicationDeployment.ApplicationInstanceName,
                        applicationDeployment.PackagePath,
                        applicationDeployment.DeploymentTimestamp);

                case ApplicationDeployStatus.Register:
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Application deployment: Registering. Cluster: {0}. Application: {1}, Imagestore path: {2}",
                        applicationDeployment.Cluster,
                        applicationDeployment.ApplicationInstanceName,
                        applicationDeployment.ImageStorePath);

                    await this.applicationOperator.RegisterApplicationAsync(
                        applicationDeployment.Cluster,
                        applicationDeployment.ImageStorePath,
                        cancellationToken);

                    return new ApplicationDeployment(ApplicationDeployStatus.Create, applicationDeployment);

                case ApplicationDeployStatus.Create:
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Application deployment: Creating. Cluster: {0}. Application: {1}",
                        applicationDeployment.Cluster,
                        applicationDeployment.ApplicationInstanceName);

                    await this.applicationOperator.CreateApplicationAsync(
                        applicationDeployment.Cluster,
                        applicationDeployment.ApplicationInstanceName,
                        applicationDeployment.ApplicationTypeName,
                        applicationDeployment.ApplicationTypeVersion,
                        cancellationToken);

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

                this.UpdateApplicationPackageConfig(configPackage.Path);
                this.UpdateApplicationPackageData(dataPackage.Path);

                this.serviceParameters.CodePackageActivationContext.DataPackageModifiedEvent +=
                    this.CodePackageActivationContext_DataPackageModifiedEvent;

                this.serviceParameters.CodePackageActivationContext.ConfigurationPackageModifiedEvent +=
                    this.CodePackageActivationContext_ConfigurationPackageModifiedEvent;
            }
        }

        private void UpdateApplicationPackageData(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            DirectoryInfo packageLocation = new DirectoryInfo(Path.Combine(this.serviceParameters.CodePackageActivationContext.WorkDirectory, "Packages"));

            this.applicationPackagePath = packageLocation.FullName;

            // delete all the things.
            // we'll copy everything over from the new data package.
            if (packageLocation.Exists)
            {
                packageLocation.Delete(true);
            }

            foreach (FileInfo file in directory.EnumerateFiles("*.zip", SearchOption.TopDirectoryOnly))
            {
                string extractPath =
                    Path.Combine(packageLocation.FullName, GetPackageDirectoryName(file.Name));

                ZipFile.ExtractToDirectory(file.FullName, extractPath);
            }
        }

        private void UpdateApplicationPackageConfig(string configPath)
        {
            using (StreamReader reader = new StreamReader(Path.Combine(configPath, "ApplicationPackages.json")))
            {
                this.ApplicationPackages = JsonConvert.DeserializeObject<IEnumerable<ApplicationPackageInfo>>(reader.ReadToEnd());
            }
        }

        private void CodePackageActivationContext_DataPackageModifiedEvent(object sender, PackageModifiedEventArgs<DataPackage> e)
        {
            this.UpdateApplicationPackageData(e.NewPackage.Path);
        }

        private void CodePackageActivationContext_ConfigurationPackageModifiedEvent(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            this.UpdateApplicationPackageConfig(e.NewPackage.Path);
        }

        private static string GetPackageDirectoryName(string fileName)
        {
            return fileName.Replace(".zip", String.Empty);
        }

        private static string GetClusterAddress(string address, int port)
        {
            return address + ":" + port;
        }
    }
}