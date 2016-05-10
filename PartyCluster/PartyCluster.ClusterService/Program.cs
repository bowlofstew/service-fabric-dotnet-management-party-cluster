// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using Microsoft.Diagnostics.EventListeners;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Microsoft.ServiceFabric.Services.Runtime;
    using PartyCluster.Common;
    using PartyCluster.Domain;
    using FabricEventListeners = Microsoft.Diagnostics.EventListeners.Fabric;

    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                const string ElasticSearchEventListenerId = "ElasticSearchEventListener";
                FabricEventListeners.FabricConfigurationProvider configProvider =
                    new FabricEventListeners.FabricConfigurationProvider(ElasticSearchEventListenerId);

                ElasticSearchListener esListener = null;
                if (configProvider.HasConfiguration)
                {
                    esListener = new ElasticSearchListener(configProvider, new FabricEventListeners.FabricHealthReporter(ElasticSearchEventListenerId));
                }

                ServiceRuntime.RegisterServiceAsync(
                    "ClusterServiceType",
                    context =>
                    {
                        IReliableStateManager stateManager = new ReliableStateManager(context);

                        return new ClusterService(
#if LOCAL
                            new FakeClusterOperator(stateManager),
                            new FakeMailer(),
#else
                        new ArmClusterOperator(context),
                        new SendGridMailer(context),
#endif
                            ServiceProxy.Create<IApplicationDeployService>(
                                new ServiceUriBuilder("ApplicationDeployService").ToUri(),
                                new ServicePartitionKey(0)),
                            stateManager,
                            context,
                            new ClusterConfig());
                    })
                    .GetAwaiter().GetResult();

                ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(ClusterService).Name);

                Thread.Sleep(Timeout.Infinite);
                GC.KeepAlive(esListener);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e);
                throw;
            }
        }

        private void SetUpDiagnosticListeners()
        {
        }
    }
}