// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ApplicationDeployService
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Description;
    using System.Fabric.Query;
    using System.Runtime.Caching;
    using System.Threading.Tasks;
    using Common;
    using Domain;

    internal class FabricClientApplicationOperator : IApplicationOperator
    {
        private readonly StatefulServiceParameters serviceParameters;
        private readonly TimeSpan cacheSlidingExpiration = TimeSpan.FromMinutes(15);
        private bool disposing = false;

        /// <summary>
        /// Mapping from cluster URIs -> active FabricClients.
        /// This doesn't need to be persisted since they can simply be recreated.
        /// </summary>
        private MemoryCache fabricClients = new MemoryCache("fabricClients");

        public FabricClientApplicationOperator(StatefulServiceParameters serviceParameters)
        {
            this.serviceParameters = serviceParameters;
        }

        public Task<string> CopyPackageToImageStoreAsync(
            string cluster, string applicationPackagePath, string applicationTypeName, string applicationTypeVersion)
        {
            FabricClient fabricClient = this.GetClient(cluster);
            FabricClient.ApplicationManagementClient applicationClient = fabricClient.ApplicationManager;

            string imagestorePath = applicationTypeName + "_" + applicationTypeVersion;

            // TODO: Error handling
            applicationClient.CopyApplicationPackage("fabric:ImageStore", applicationPackagePath, imagestorePath);

            return Task.FromResult(imagestorePath);
        }

        public Task CreateApplicationAsync(string cluster, string applicationInstanceName, string applicationTypeName, string applicationTypeVersion)
        {
            FabricClient fabricClient = this.GetClient(cluster);
            FabricClient.ApplicationManagementClient applicationClient = fabricClient.ApplicationManager;

            Uri appName = new Uri("fabric:/" + applicationInstanceName);

            ApplicationDescription appDescription = new ApplicationDescription(appName, applicationTypeName, applicationTypeVersion);

            return applicationClient.CreateApplicationAsync(appDescription);
        }

        public Task RegisterApplicationAsync(string cluster, string imageStorePath)
        {
            FabricClient fabricClient = this.GetClient(cluster);
            FabricClient.ApplicationManagementClient applicationClient = fabricClient.ApplicationManager;

            // TODO: Error handling
            return applicationClient.ProvisionApplicationAsync(imageStorePath);
        }

        public async Task<int> GetApplicationCountAsync(string cluster)
        {
            FabricClient fabricClient = this.GetClient(cluster);

            ApplicationList applicationList = await fabricClient.QueryManager.GetApplicationListAsync();

            return applicationList.Count;
        }

        public async Task<int> GetServiceCountAsync(string cluster)
        {
            FabricClient fabricClient = this.GetClient(cluster);

            ApplicationList applicationList = await fabricClient.QueryManager.GetApplicationListAsync();

            int count = 0;
            foreach (Application application in applicationList)
            {
                ServiceList serviceList = await fabricClient.QueryManager.GetServiceListAsync(application.ApplicationName);
                count += serviceList.Count;
            }

            return count;
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

                clientName = clientName + "_" + this.serviceParameters.ReplicaId.ToString();

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