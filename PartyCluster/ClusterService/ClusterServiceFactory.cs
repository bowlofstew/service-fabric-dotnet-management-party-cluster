// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;
    using System.Fabric;
    using Microsoft.ServiceFabric.Data;

    internal class ClusterServiceFactory : IStatefulServiceFactory
    {
        public IStatefulServiceReplica CreateReplica(string serviceTypeName, Uri serviceName, byte[] initializationData, Guid partitionId, long replicaId)
        {
            StatefulServiceParameters parameters = new StatefulServiceParameters(
                FabricRuntime.GetActivationContext(),
                initializationData,
                partitionId,
                serviceName,
                serviceTypeName,
                replicaId);

            IReliableStateManager stateManager = new ReliableStateManager();

            return new ClusterService(
                new FakeClusterOperator(stateManager),
                new SendGridMailer(parameters),
                stateManager,
                parameters,
                new ClusterConfig());
        }
    }
}