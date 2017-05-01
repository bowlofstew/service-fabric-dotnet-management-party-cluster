// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Microsoft.ServiceFabric.Services.Runtime;
    using PartyCluster.Common;
    using PartyCluster.Domain;
    using Microsoft.Diagnostics.EventFlow.ServiceFabric;

    public class Program
    {
#if LOCAL
        public static void Main(string[] args)
        {
            try
            {
                ServiceRuntime.RegisterServiceAsync(
                    "ClusterServiceType",
                    context =>
                    {
                        IReliableStateManager stateManager = new ReliableStateManager(context);

                        return new ClusterService(
                            new FakeClusterOperator(stateManager),
                            new FakeMailer(),
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

            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e);
                throw;
            }
        }

#else
        public static void Main(string[] args)
        {

            using (var pipeline = ServiceFabricDiagnosticPipelineFactory.CreatePipeline("PartyCluster.ClusterService"))
            {
                try
                {

                    ServiceRuntime.RegisterServiceAsync(
                        "ClusterServiceType",
                        context =>
                        {
                            IReliableStateManager stateManager = new ReliableStateManager(context);

                            return new ClusterService(
                                new ArmClusterOperator(context),
                                new FakeMailer(),
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

                }
                catch (Exception e)
                {
                    ServiceEventSource.Current.ServiceHostInitializationFailed(e);
                    throw;
                }
            }
        }
#endif

    }
}