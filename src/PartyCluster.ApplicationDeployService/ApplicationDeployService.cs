// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ApplicationDeployService
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
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using Newtonsoft.Json;
    using PartyCluster.Common;
    using PartyCluster.Domain;
    using System.Diagnostics;

    /// <summary>
    /// The Service Fabric runtime creates an instance of this class for each service type instance.
    /// </summary>
    internal class ApplicationDeployService : StatefulService, IApplicationDeployService
    {
        /// <summary>
        /// The work queue is used to schedule deployment jobs.
        /// Application deployments have multiple steps: copy, register, create.
        /// Each step is a job that is scheduled for execution on the queue.
        /// </summary>
        private const string QueueName = "WorkQueue";

        /// <summary>
        /// The work table keeps track of an application deployment and the step its currently on.
        /// Each step of deployment is processed when it is its turn in the job queue.
        /// </summary>
        private const string DictionaryName = "WorkTable";

        /// <summary>
        /// A timeout value for Reliable Collection operations.
        /// The default value is 4 seconds.
        /// </summary>
        private readonly TimeSpan transactionTimeout = TimeSpan.FromSeconds(4);

        /// <summary>
        /// An application deployment service
        /// </summary>
        private readonly IApplicationOperator applicationOperator;

        /// <summary>
        /// This is the cancellation token given to the service replica when RunAsync is invoked.
        /// This service does work in RunAsync but it also handles user requests.
        /// We keep track of the cancellation token here so that we can cancel any work the service is doing when the system needs us to shut down.
        /// </summary>
        private CancellationToken replicaRunCancellationToken;

        private DirectoryInfo applicationPackageDataPath;
        private DirectoryInfo applicationPackageTempDirectory;

        /// <summary>
        /// Creates a new service class instance with the given state manager and service parameters.
        /// </summary>
        /// <param name="stateManager"></param>
        /// <param name="serviceContext"></param>
        public ApplicationDeployService(
            IReliableStateManager stateManager, IApplicationOperator applicationOperator, StatefulServiceContext serviceContext)
            : base(serviceContext, stateManager as IReliableStateManagerReplica)
        {
            this.applicationOperator = applicationOperator;
        }

        /// <summary>
        /// A list of application packages that this service deployes to other clusters.
        /// </summary>
        internal IEnumerable<ApplicationPackageInfo> ApplicationPackages { get; set; }

        /// <summary>
        /// Queues deployment of application packages to the given cluster.
        /// </summary>
        /// <param name="clusterAddress"></param>
        /// <param name="clusterPort"></param>
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
                // Grab each application package that's included with the service
                // and create an ApplicationDeployment record of it.
                // Then queue a job to begin processing each one.
                foreach (ApplicationPackageInfo package in this.ApplicationPackages)
                {
                    Guid id = Guid.NewGuid();
                    ApplicationDeployment applicationDeployment = new ApplicationDeployment(
                        cluster: GetClusterAddress(clusterAddress, clusterPort),
                        status: ApplicationDeployStatus.Copy,
                        imageStorePath: null,
                        applicationTypeName: package.ApplicationTypeName,
                        applicationTypeVersion: package.ApplicationTypeVersion,
                        applicationInstanceName: GetApplicationInstanceName(package.PackageFileName),
                        packageZipFilePath: Path.Combine(this.applicationPackageDataPath.FullName, package.PackageFileName),
                        timestamp: DateTimeOffset.UtcNow);

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

            string clusterEndpoint = clusterAddress + ":" + clusterPort;
            foreach (ApplicationPackageInfo package in this.ApplicationPackages)
            {
                try
                {
                    // skip applications that aren't deployed
                    if (
                        !(await
                            this.applicationOperator.ApplicationExistsAsync(
                                clusterEndpoint,
                                GetApplicationInstanceName(package.PackageFileName),
                                this.replicaRunCancellationToken)))
                    {
                        continue;
                    }

                    // if an info URL is provided in the package info, use that.
                    string address = package.ServiceInfoUrl;

                    // otherwise, get a URL for the service entry point.
                    if (String.IsNullOrWhiteSpace(address))
                    {
                        string endpoint =
                            await
                                this.applicationOperator.GetServiceEndpoint(
                                    clusterEndpoint,
                                    new Uri(package.EntryServiceInstanceUri),
                                    package.EntryServiceEndpointName,
                                    this.replicaRunCancellationToken);

                        // the host in the service URL will be the internal VM IP, FQDN, or "+", which isn't useful to external users.
                        // replace it with the cluster address that external users can access.
                        UriBuilder builder = new UriBuilder(endpoint);
                        builder.Host = clusterAddress;

                        address = builder.ToString();
                    }

                    applications.Add(
                        new ApplicationView(
                            new HyperlinkView(
                                address,
                                GetApplicationInstanceName(package.PackageFileName),
                                package.ApplicationDescription)));
                }
                catch (FabricServiceNotFoundException)
                {
                    // service isn't deployed yet, skip it.
                }
                catch (Exception e)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
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
                ConditionalValue<ApplicationDeployment> resultStatus = await dictionary.TryGetValueAsync(tx, deployId);
                if (resultStatus.HasValue)
                {
                    return resultStatus.Value.Status;
                }

                throw new KeyNotFoundException(String.Format("Deployment {0} does not exist.", deployId));
            }
        }

        /// <summary>
        /// Gets the total number of application instances currently deployed to the given cluster.
        /// </summary>
        /// <param name="clusterAddress"></param>
        /// <param name="clusterPort"></param>
        /// <returns></returns>
        public Task<int> GetApplicationCountAsync(string clusterAddress, int clusterPort)
        {
            return this.applicationOperator.GetApplicationCountAsync(GetClusterAddress(clusterAddress, clusterPort), this.replicaRunCancellationToken);
        }

        /// <summary>
        /// Gets the total number of service instances currently deployed to the given cluster.
        /// </summary>
        /// <param name="clusterAddress"></param>
        /// <param name="clusterPort"></param>
        /// <returns></returns>
        public Task<int> GetServiceCountAsync(string clusterAddress, int clusterPort)
        {
            return this.applicationOperator.GetServiceCountAsync(GetClusterAddress(clusterAddress, clusterPort), this.replicaRunCancellationToken);
        }

        /// <summary>
        /// Executes when a replica of this service opens.
        /// Performing configuration operations here.
        /// </summary>
        /// <param name="openMode"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override Task OnOpenAsync(ReplicaOpenMode openMode, CancellationToken cancellationToken)
        {
            this.ConfigureService();
            return base.OnOpenAsync(openMode, cancellationToken);
        }

        /// <summary>
        /// Creates listeners for clients.
        /// </summary>
        /// <returns></returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[] {new ServiceReplicaListener(context => this.CreateServiceRemotingListener<ApplicationDeployService>(context))};
        }

        /// <summary>
        /// Main entry point for the service.
        /// Runs on a continuous loop, pulling an item from a queue for processing on each iteration.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            TimeSpan delayTime = TimeSpan.FromSeconds(5);

            // Save the cancellation token so that it can be used to cancel client-invoked methods on the service when the service needs to shut down.
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

                        delayTime = performedWork
                            ? TimeSpan.FromSeconds(1)
                            : TimeSpan.FromSeconds(30);
                    }
                    catch (TimeoutException te)
                    {
                        // Log this and continue processing the next cluster.
                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "TimeoutException while processing application deployment queue. {0}",
                            te.ToString());
                    }
                    catch (FabricTransientException fte)
                    {
                        // Log this and continue processing the next cluster.
                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "FabricTransientException while processing application deployment queue. {0}",
                            fte.ToString());
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

            Stopwatch sw = Stopwatch.StartNew();

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalValue<Guid> workItem = await queue.TryDequeueAsync(tx, this.transactionTimeout, cancellationToken);
                if (!workItem.HasValue)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "No new application deployment requests.");
                    return false;
                }

                Guid workItemId = workItem.Value;

                ConditionalValue<ApplicationDeployment> appDeployment =
                    await dictionary.TryGetValueAsync(tx, workItemId, LockMode.Update, this.transactionTimeout, cancellationToken);
                if (!appDeployment.HasValue)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "Found queued application deployment request with no associated deployment information. Discarding.");

                    return true;
                }

                ApplicationDeployment processedDeployment = await this.ProcessApplicationDeployment(appDeployment.Value, cancellationToken);

                if (processedDeployment.Status == ApplicationDeployStatus.Complete ||
                    processedDeployment.Status == ApplicationDeployStatus.Failed)
                {
                    // Remove deployments that completed or failed
                    await dictionary.TryRemoveAsync(tx, workItemId, this.transactionTimeout, cancellationToken);

                    // Log completion.
                    ServiceEventSource.Current.ApplicationDeploymentCompleted(
                                sw.ElapsedMilliseconds,
                                ApplicationDeployStatus.Complete == processedDeployment.Status,
                                processedDeployment.Cluster,
                                processedDeployment.ApplicationTypeName,
                                processedDeployment.ApplicationTypeVersion,
                                processedDeployment.ApplicationInstanceName);
                }
                else
                {
                    // The deployment hasn't completed or failed, so queue up the next stage of deployment
                    await queue.EnqueueAsync(tx, workItemId, this.transactionTimeout, cancellationToken);

                    // And update the deployment record with the new status
                    await dictionary.SetAsync(tx, workItemId, processedDeployment, this.transactionTimeout, cancellationToken);

                    ServiceEventSource.Current.ApplicationDeploymentSuccessStatus(
                                processedDeployment.Cluster,
                                processedDeployment.ApplicationTypeName,
                                processedDeployment.ApplicationTypeVersion,
                                processedDeployment.ApplicationInstanceName,
                                Enum.GetName(typeof(ApplicationDeployStatus),processedDeployment.Status));
                }

                await tx.CommitAsync();
            }

            return true;
        }

        internal async Task<ApplicationDeployment> ProcessApplicationDeployment(
            ApplicationDeployment applicationDeployment, CancellationToken cancellationToken)
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
                            DirectoryInfo packageExtractLocation =
                                new DirectoryInfo(Path.Combine(this.applicationPackageTempDirectory.FullName, applicationDeployment.ApplicationInstanceName));

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
                        catch (FabricServiceNotFoundException fsnfe)
                        {
                            // image store service isn't ready yet. 
                            // This is retry-able, just need to wait a bit for it to come up.
                            // This can happen when an application deployment is attempted immediately after a cluster comes up.

                            ServiceEventSource.Current.ApplicationDeploymentFailureCopyFailure(
                                applicationDeployment.Cluster,
                                applicationDeployment.ApplicationTypeName,
                                applicationDeployment.ApplicationTypeVersion,
                                applicationDeployment.ApplicationInstanceName,
                                applicationDeployment.PackageZipFilePath,
                                fsnfe.Message);

                            return applicationDeployment;
                        }
                        catch (FileNotFoundException fnfe)
                        {
                            ServiceEventSource.Current.ApplicationDeploymentFailureCorruptPackage(
                                applicationDeployment.Cluster,
                                applicationDeployment.ApplicationTypeName,
                                applicationDeployment.ApplicationTypeVersion,
                                applicationDeployment.ApplicationInstanceName,
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
                            ServiceEventSource.Current.ApplicationDeploymentFailureAlreadyRegistered(
                                applicationDeployment.Cluster,
                                applicationDeployment.ApplicationTypeName,
                                applicationDeployment.ApplicationTypeVersion,
                                applicationDeployment.ApplicationInstanceName,
                                applicationDeployment.PackageZipFilePath);

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
                ServiceEventSource.Current.ApplicationDeploymentFailureTransientError(
                                applicationDeployment.Cluster,
                                applicationDeployment.ApplicationTypeName,
                                applicationDeployment.ApplicationTypeVersion,
                                applicationDeployment.ApplicationInstanceName,
                                applicationDeployment.PackageZipFilePath,
                                Enum.GetName(typeof(ApplicationDeployStatus), applicationDeployment.Status),
                                fte.Message);

                // return the deployment unchanged. It will be returned to the queue and tried again.
                return applicationDeployment;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ApplicationDeploymentFailureUnknownError(
                                applicationDeployment.Cluster,
                                applicationDeployment.ApplicationTypeName,
                                applicationDeployment.ApplicationTypeVersion,
                                applicationDeployment.ApplicationInstanceName,
                                applicationDeployment.PackageZipFilePath,
                                e.ToString());

                return new ApplicationDeployment(ApplicationDeployStatus.Failed, applicationDeployment);
            }
        }

        private void ConfigureService()
        {
            if (this.Context.CodePackageActivationContext != null)
            {
                ConfigurationPackage configPackage = this.Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
                DataPackage dataPackage = this.Context.CodePackageActivationContext.GetDataPackageObject("ApplicationPackages");

                this.UpdateSettings(configPackage.Settings);
                this.UpdateApplicationPackageConfig(configPackage.Path);
                this.UpdateApplicationPackageData(dataPackage.Path);

                this.Context.CodePackageActivationContext.DataPackageModifiedEvent +=
                    this.CodePackageActivationContext_DataPackageModifiedEvent;

                this.Context.CodePackageActivationContext.ConfigurationPackageModifiedEvent +=
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

            this.applicationPackageTempDirectory = new DirectoryInfo(
                String.IsNullOrEmpty(tempDirectoryParam)
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

        private static string GetApplicationInstanceName(string fileName)
        {
            return fileName.Replace(".zip", String.Empty);
        }

        private static string GetClusterAddress(string address, int port)
        {
            return address + ":" + port;
        }
    }
}