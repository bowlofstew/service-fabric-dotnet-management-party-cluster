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
        public static void Main(string[] args)
        {
            try
            {
                using (var pipeline = ServiceFabricDiagnosticPipelineFactory.CreatePipeline("PartyCluster.ClusterService"))
                {
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
                            ////new SendGridMailer(context),
                            new FakeMailer(),
#endif
                            new EmptyApplicationDeployService(),
                            stateManager,
                            context,
                            new ClusterConfig());
                        })
                        .GetAwaiter().GetResult();

                    ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(ClusterService).Name);

                    Thread.Sleep(Timeout.Infinite);
                }
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e);
                throw;
            }
        }
    }
}