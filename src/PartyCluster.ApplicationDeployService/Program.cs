// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ApplicationDeployService
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Services.Runtime;
    using Microsoft.Diagnostics.EventFlow.ServiceFabric;

    internal static class Program
    {
        /// <summary>
        /// This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            //using (var pipeline = ServiceFabricDiagnosticPipelineFactory.CreatePipeline("PartyCluster.ApplicationDeployService"))
            {
                try
                {
                    ServiceRuntime.RegisterServiceAsync(
                    "ApplicationDeployServiceType",
                    context =>
                        new ApplicationDeployService(new ReliableStateManager(context), new FabricClientApplicationOperator(context), context)
                    )
                    .GetAwaiter().GetResult();

                    ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(ApplicationDeployService).Name);

                    Thread.Sleep(Timeout.Infinite); // Prevents this host process from terminating to keep the service host process running.

                }
                catch (Exception e)
                {
                    ServiceEventSource.Current.ServiceHostInitializationFailed(e.ToString());
                    throw;
                }
            }
        }
    }
}