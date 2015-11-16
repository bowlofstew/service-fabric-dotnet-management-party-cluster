// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ApplicationDeployService
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Description;
    using System.Threading.Tasks;
    using Common;
    using Domain;

    internal class FabricClientApplicationOperator : IApplicationOperator
    {
        private bool disposing = false;
        private StatefulServiceParameters serviceParameters;

        /// <summary>
        /// Mapping from cluster URIs -> active FabricClients.
        /// This doesn't need to be persisted since they can simply be recreated.
        /// </summary>
        private IDictionary<string, FabricClient> fabricClients = new ConcurrentDictionary<string, FabricClient>();

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

        public void Dispose()
        {
            if (!this.disposing)
            {
                this.disposing = true;
                foreach (FabricClient client in this.fabricClients.Values)
                {
                    try
                    {
                        client.Dispose();
                    }
                    catch
                    {
                        // move on
                    }
                }
                this.fabricClients.Clear();
            }
        }

        public void CloseConnection(string cluster)
        {
            FabricClient fabricClient;
            if (this.fabricClients.TryGetValue(cluster, out fabricClient))
            {
                try
                {
                    fabricClient.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }

                this.fabricClients.Remove(cluster);
            }
        }

        private FabricClient GetClient(string cluster)
        {
            if (!this.fabricClients.ContainsKey(cluster))
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

                FabricClient fc = new FabricClient(
                    new FabricClientSettings
                    {
                        ClientFriendlyName = clientName,
                        ConnectionInitializationTimeout = TimeSpan.FromSeconds(30),
                        KeepAliveInterval = TimeSpan.FromSeconds(15),
                    },
                    cluster);

                this.fabricClients[cluster] = fc;
            }

            return this.fabricClients[cluster];
        }
    }
}