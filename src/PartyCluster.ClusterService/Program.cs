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
                //using (var pipeline = ServiceFabricDiagnosticPipelineFactory.CreatePipeline("PartyCluster.ClusterService"))
                //{
                    ServiceRuntime.RegisterServiceAsync(
                        "ClusterServiceType",
                        context =>
                        {
                            IReliableStateManager stateManager = new ReliableStateManager(context);

                            return new ClusterService(
#if LOCAL
                            new FakeClusterOperator(stateManager),
#else
                            new ArmClusterOperator(context),
#endif
                            stateManager,
                            context,
                            new ClusterConfig(),
                            new ClusterConfig());
                        })
                        .GetAwaiter().GetResult();

                //Common.Trace.Message("ServiceTypeRegistered {0} {1}", Process.GetCurrentProcess().Id, typeof(ClusterService).Name);
                //ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(ClusterService).Name);

                    Thread.Sleep(Timeout.Infinite);
                //}
            }
            catch (Exception e)
            {
                Common.Trace.Error(e.Message + e.StackTrace);
                //ServiceEventSource.Current.ServiceHostInitializationFailed(e);
                throw;
            }
        }
    }
}