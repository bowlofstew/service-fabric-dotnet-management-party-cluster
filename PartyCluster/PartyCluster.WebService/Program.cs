// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using Microsoft.Diagnostics.EventListeners;
    using Microsoft.ServiceFabric.Services.Runtime;
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

                ServiceRuntime.RegisterServiceAsync("WebServiceType", context => new WebService(context)).GetAwaiter().GetResult();

                ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(WebService).Name);

                Thread.Sleep(Timeout.Infinite);
                GC.KeepAlive(esListener);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e);
                throw;
            }
        }
    }
}