// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ApplicationDeployService
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Fabric;
    using System.Fabric.Description;
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
        private readonly StatefulServiceParameters serviceParameters;
        private readonly IApplicationOperator applicationOperator;
        private CancellationToken replicaRunCancellationToken;
        private DirectoryInfo applicationPackageDataPath;
        private DirectoryInfo applicationPackageTempDirectory;

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
                    Guid id = Guid.NewGuid();
                    ApplicationDeployment applicationDeployment = new ApplicationDeployment(
                        GetClusterAddress(clusterAddress, clusterPort),
                        ApplicationDeployStatus.Copy,
                        null,
                        package.ApplicationTypeName,
                        package.ApplicationTypeVersion,
                        GetPackageDirectoryName(package.PackageFileName),
                        Path.Combine(this.applicationPackageDataPath.FullName, package.PackageFileName),
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
                    string address = package.EntryServiceUrl;

                    if (String.IsNullOrWhiteSpace(address))
                    {
                        address = await this.applicationOperator.GetServiceEndpoint(clusterAddress + ":" + clusterPort, new Uri(package.EntryServiceInstanceUri), package.EntryServiceEndpointName);
                    }

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
                        delayTime = performedWork ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(30);
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

                ConditionalResult<ApplicationDeployment> appDeployment = await dictionary.TryGetValueAsync(tx, workItemId, LockMode.Update, this.transactionTimeout, cancellationToken);
                if (!appDeployment.HasValue)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Found queued application deployment request with no associated deployment information. Discarding.");

                    return true;
                }

                ApplicationDeployment processedDeployment = await this.ProcessApplicationDeployment(appDeployment.Value, cancellationToken);
                
                if (processedDeployment.Status != ApplicationDeployStatus.Failed)
                {
                    if (processedDeployment.Status != ApplicationDeployStatus.Complete)
                    {
                        await queue.EnqueueAsync(tx, workItemId, this.transactionTimeout, cancellationToken);
                    }

                    await dictionary.SetAsync(tx, workItemId, processedDeployment, this.transactionTimeout, cancellationToken);

                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Application deployment request successfully processed. Cluster: {0}. Status: {1}",
                        processedDeployment.Cluster,
                        processedDeployment.Status);
                }
                else
                {
                    await dictionary.TryRemoveAsync(tx, workItemId, this.transactionTimeout, cancellationToken);
                }
                
                await tx.CommitAsync();

                return true;
            }
        }

        internal async Task<ApplicationDeployment> ProcessApplicationDeployment(ApplicationDeployment applicationDeployment, CancellationToken cancellationToken)
        {
            try
            {
                switch (applicationDeployment.Status)
                {
                    case ApplicationDeployStatus.Copy:
                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Application deployment: Copying to image store. Cluster: {0}. Application: {1}. Package path: {2}",
                            applicationDeployment.Cluster,
                            applicationDeployment.ApplicationInstanceName,
                            applicationDeployment.PackageZipFilePath);

                        try
                        {
                            // Unzip the package contents to a temporary location on disk.
                           
                            if (!this.applicationPackageTempDirectory.Exists)
                            {
                                this.applicationPackageTempDirectory.Create();
                            }

                            FileInfo packageFile = new FileInfo(applicationDeployment.PackageZipFilePath);
                            DirectoryInfo packageExtractLocation = new DirectoryInfo(Path.Combine(this.applicationPackageTempDirectory.FullName, applicationDeployment.ApplicationInstanceName));
                            
                            if (packageExtractLocation.Exists)
                            {
                                packageExtractLocation.Delete(true);
                            }

                            ServiceEventSource.Current.ServiceMessage(
                                this,
                                "Extracting application package from {0} to {1}",
                                packageFile.FullName,
                                packageExtractLocation.FullName);

                            ZipFile.ExtractToDirectory(packageFile.FullName, packageExtractLocation.FullName);
                            

                            // Copy the unzipped application package to the cluster's imagestore and clean up.

                            string imageStorePath = await this.applicationOperator.CopyPackageToImageStoreAsync(
                                applicationDeployment.Cluster,
                                packageExtractLocation.FullName,
                                applicationDeployment.ApplicationTypeName,
                                applicationDeployment.ApplicationTypeVersion,
                                cancellationToken);

                            packageExtractLocation.Delete(true);
                            
                            return new ApplicationDeployment(
                                applicationDeployment.Cluster,
                                ApplicationDeployStatus.Register,
                                imageStorePath,
                                applicationDeployment.ApplicationTypeName,
                                applicationDeployment.ApplicationTypeVersion,
                                applicationDeployment.ApplicationInstanceName,
                                applicationDeployment.PackageZipFilePath,
                                applicationDeployment.DeploymentTimestamp);
                        }
                        catch (FileNotFoundException fnfe)
                        {
                            ServiceEventSource.Current.ServiceMessage(
                                this,
                                "Found corrupt application package. Package: {0}. Error: {1}",
                                applicationDeployment.PackageZipFilePath,
                                fnfe.Message);

                            return new ApplicationDeployment(ApplicationDeployStatus.Failed, applicationDeployment);
                        }

                    case ApplicationDeployStatus.Register:
                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Application deployment: Registering. Cluster: {0}. Application: {1}, Imagestore path: {2}",
                            applicationDeployment.Cluster,
                            applicationDeployment.ApplicationInstanceName,
                            applicationDeployment.ImageStorePath);

                        try
                        {
                            await this.applicationOperator.RegisterApplicationAsync(
                                applicationDeployment.Cluster,
                                applicationDeployment.ImageStorePath,
                                cancellationToken);

                            return new ApplicationDeployment(ApplicationDeployStatus.Create, applicationDeployment);
                        }
                        catch (FabricElementAlreadyExistsException)
                        {
                            ServiceEventSource.Current.ServiceMessage(this, "Application package is already registered. Package: {0}.", applicationDeployment.PackageZipFilePath);

                            // application already exists, set status to Create it so we don't keep failing on this.
                            return new ApplicationDeployment(ApplicationDeployStatus.Create, applicationDeployment);
                        }

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
            catch (FabricTransientException fte)
            {
                ServiceEventSource.Current.ServiceMessage(
                    this,
                    "A transient error occured during package processing. Package: {0}. Stage: {1}. Error: {2}",
                    applicationDeployment.ApplicationInstanceName,
                    applicationDeployment.Status,
                    fte.Message);

                // return the deployment unchanged. It will be returned to the queue and tried again.
                return applicationDeployment;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(
                    this,
                    "Application package processing failed. Package: {0}. Error: {1}",
                    applicationDeployment.PackageZipFilePath,
                    e.ToString());

                return new ApplicationDeployment(ApplicationDeployStatus.Failed, applicationDeployment);
            }
        }

        private void ConfigureService()
        {
            if (this.serviceParameters.CodePackageActivationContext != null)
            {
                ConfigurationPackage configPackage = this.serviceParameters.CodePackageActivationContext.GetConfigurationPackageObject("Config");
                DataPackage dataPackage = this.serviceParameters.CodePackageActivationContext.GetDataPackageObject("ApplicationPackages");

                this.UpdateSettings(configPackage.Settings);
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
            this.applicationPackageDataPath = new DirectoryInfo(path);
        }

        private void UpdateApplicationPackageConfig(string configPath)
        {
            using (StreamReader reader = new StreamReader(Path.Combine(configPath, "ApplicationPackages.json")))
            {
                this.ApplicationPackages = JsonConvert.DeserializeObject<IEnumerable<ApplicationPackageInfo>>(reader.ReadToEnd());
            }
        }

        private void UpdateSettings(ConfigurationSettings settings)
        {
            KeyedCollection<string, ConfigurationProperty> parameters = settings.Sections["ApplicationDeploySettings"].Parameters;

            string tempDirectoryParam = parameters["PackageTempDirectory"]?.Value;
            
            this.applicationPackageTempDirectory = new DirectoryInfo(String.IsNullOrEmpty(tempDirectoryParam)
                ? Path.GetTempPath()
                : tempDirectoryParam);
        }

        private void CodePackageActivationContext_DataPackageModifiedEvent(object sender, PackageModifiedEventArgs<DataPackage> e)
        {
            this.UpdateApplicationPackageData(e.NewPackage.Path);
        }

        private void CodePackageActivationContext_ConfigurationPackageModifiedEvent(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            this.UpdateApplicationPackageConfig(e.NewPackage.Path);
            this.UpdateSettings(e.NewPackage.Settings);
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