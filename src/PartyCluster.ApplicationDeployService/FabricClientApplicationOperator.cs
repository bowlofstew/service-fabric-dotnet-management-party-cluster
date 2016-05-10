// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ApplicationDeployService
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Description;
    using System.Fabric.Query;
    using System.Linq;
    using System.Runtime.Caching;
    using System.Threading;
    using System.Threading.Tasks;
    using Newtonsoft.Json.Linq;
    using PartyCluster.Domain;

    internal class FabricClientApplicationOperator : IApplicationOperator
    {
        private readonly StatefulServiceContext serviceContext;
        private readonly TimeSpan readOperationTimeout = TimeSpan.FromSeconds(5);
        private readonly TimeSpan writeOperationTimeout = TimeSpan.FromMinutes(1);
        private readonly TimeSpan cacheSlidingExpiration = TimeSpan.FromMinutes(15);
        private bool disposing = false;

        /// <summary>
        /// Mapping from cluster URIs -> active FabricClients.
        /// This doesn't need to be persisted since they can simply be recreated.
        /// </summary>
        private MemoryCache fabricClients = new MemoryCache("fabricClients");

        public FabricClientApplicationOperator(StatefulServiceContext serviceContext)
        {
            this.serviceContext = serviceContext;
        }

        public async Task<string> GetServiceEndpoint(string cluster, Uri serviceInstanceUri, string serviceEndpointName, CancellationToken token)
        {
            FabricClient fabricClient = this.GetClient(cluster);

            // this resolution may return a stale address if the service moved recently.
            // However, for a single-partition stateless services it shouldn't matter because each instance will publish the same address.
            ResolvedServicePartition rsp = await fabricClient.ServiceManager.ResolveServicePartitionAsync(serviceInstanceUri, this.readOperationTimeout, token);

            ResolvedServiceEndpoint endpoint = rsp.GetEndpoint();

            // This assumes the service uses the Reliable Services framework,
            // where the endpoint is always a JSON object that can contain multiple endpoints.
            JObject endpointJson = JObject.Parse(endpoint.Address);

            return endpointJson["Endpoints"][serviceEndpointName].Value<string>();
        }

        /// <summary>
        /// Copies an application package to the given cluster's image store.
        /// This expects the cluster to have the ImageStore service running.
        /// </summary>
        /// <param name="cluster"></param>
        /// <param name="applicationPackagePath"></param>
        /// <param name="applicationTypeName"></param>
        /// <param name="applicationTypeVersion"></param>
        /// <returns></returns>
        public Task<string> CopyPackageToImageStoreAsync(
            string cluster, string applicationPackagePath, string applicationTypeName, string applicationTypeVersion, CancellationToken token)
        {
            FabricClient fabricClient = this.GetClient(cluster);
            FabricClient.ApplicationManagementClient applicationClient = fabricClient.ApplicationManager;

            string imagestorePath = applicationTypeName + "_" + applicationTypeVersion;

            try
            {
                applicationClient.CopyApplicationPackage("fabric:ImageStore", applicationPackagePath, imagestorePath);
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                // CopyApplicationPackages uses MethodInvoke internally to upload.
                // If that throws, we get TargetInvocationException.
                // This will rethrow the actual inner exception for the caller.
                if (e.InnerException != null)
                {
                    throw e.InnerException;
                }

                throw;
            }

            return Task.FromResult(imagestorePath);
        }

        /// <summary>
        /// Creates an instance of an application.
        /// </summary>
        /// <param name="cluster"></param>
        /// <param name="applicationInstanceName"></param>
        /// <param name="applicationTypeName"></param>
        /// <param name="applicationTypeVersion"></param>
        /// <returns></returns>
        public async Task CreateApplicationAsync(
            string cluster, string applicationInstanceName, string applicationTypeName, string applicationTypeVersion, CancellationToken token)
        {
            FabricClient fabricClient = this.GetClient(cluster);
            FabricClient.ApplicationManagementClient applicationClient = fabricClient.ApplicationManager;

            Uri appName = new Uri("fabric:/" + applicationInstanceName);

            if (await this.ApplicationExistsAsync(cluster, applicationInstanceName, token))
            {
                return;
            }

            ApplicationDescription appDescription = new ApplicationDescription(appName, applicationTypeName, applicationTypeVersion);

            await applicationClient.CreateApplicationAsync(appDescription, this.writeOperationTimeout, token);
        }

        /// <summary>
        /// Registers an application type.
        /// </summary>
        /// <param name="cluster"></param>
        /// <param name="imageStorePath"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public Task RegisterApplicationAsync(string cluster, string imageStorePath, CancellationToken token)
        {
            FabricClient fabricClient = this.GetClient(cluster);
            FabricClient.ApplicationManagementClient applicationClient = fabricClient.ApplicationManager;

            // TODO: Error handling
            return applicationClient.ProvisionApplicationAsync(imageStorePath, this.writeOperationTimeout, token);
        }

        /// <summary>
        /// Gets the number of application deployed to the given cluster.
        /// </summary>
        /// <param name="cluster"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<int> GetApplicationCountAsync(string cluster, CancellationToken token)
        {
            FabricClient fabricClient = this.GetClient(cluster);

            ApplicationList applicationList = await fabricClient.QueryManager.GetApplicationListAsync(null, this.readOperationTimeout, token);

            return applicationList.Count;
        }

        /// <summary>
        /// Gets the total number of service deployed to the given cluster.
        /// </summary>
        /// <param name="cluster"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<int> GetServiceCountAsync(string cluster, CancellationToken token)
        {
            FabricClient fabricClient = this.GetClient(cluster);

            ApplicationList applicationList = await fabricClient.QueryManager.GetApplicationListAsync(null, this.readOperationTimeout, token);

            int count = 0;
            foreach (Application application in applicationList)
            {
                ServiceList serviceList =
                    await fabricClient.QueryManager.GetServiceListAsync(application.ApplicationName, null, this.readOperationTimeout, token);
                count += serviceList.Count;
            }

            return count;
        }

        public async Task<bool> ApplicationExistsAsync(string cluster, string applicationInstanceName, CancellationToken token)
        {
            FabricClient fabricClient = this.GetClient(cluster);
            Uri appName = new Uri("fabric:/" + applicationInstanceName);

            ApplicationList applications = await fabricClient.QueryManager.GetApplicationListAsync(appName, TimeSpan.FromSeconds(30), token);

            return applications.Any(x => x.ApplicationName == appName);
        }

        public void Dispose()
        {
            if (!this.disposing)
            {
                this.disposing = true;
                foreach (KeyValuePair<string, object> client in this.fabricClients)
                {
                    try
                    {
                        ((FabricClient) client.Value).Dispose();
                    }
                    catch
                    {
                        // move on
                    }
                }
                this.fabricClients.Dispose();
            }
        }

        private FabricClient GetClient(string cluster)
        {
            FabricClient client = this.fabricClients.Get(cluster) as FabricClient;

            if (client == null)
            {
                string clientName = Environment.GetEnvironmentVariable("COMPUTERNAME");
                if (string.IsNullOrWhiteSpace(clientName))
                {
                    try
                    {
                        clientName = System.Net.Dns.GetHostName();
                    }
                    catch (System.Net.Sockets.SocketException)
                    {
                        clientName = "";
                    }
                }

                clientName = clientName + "_" + this.serviceContext.ReplicaId.ToString();

                client = new FabricClient(
                    new FabricClientSettings
                    {
                        ClientFriendlyName = clientName,
                        ConnectionInitializationTimeout = TimeSpan.FromSeconds(30),
                        KeepAliveInterval = TimeSpan.FromSeconds(15),
                    },
                    cluster);

                this.fabricClients.Add(
                    new CacheItem(cluster, client),
                    new CacheItemPolicy()
                    {
                        SlidingExpiration = this.cacheSlidingExpiration,
                        RemovedCallback = args =>
                        {
                            IDisposable fc = args.CacheItem.Value as IDisposable;
                            if (fc != null)
                            {
                                try
                                {
                                    fc.Dispose();
                                }
                                catch
                                {
                                }
                            }
                        }
                    });
            }

            return client;
        }
    }
}