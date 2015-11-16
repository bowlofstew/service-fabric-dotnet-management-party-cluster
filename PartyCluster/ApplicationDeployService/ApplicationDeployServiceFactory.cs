// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ApplicationDeployService
{
    using System;
    using System.Fabric;
    using Common;
    using Microsoft.ServiceFabric.Data;

    internal class ApplicationDeployServiceFactory : IStatefulServiceFactory
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

            return new ApplicationDeployService(stateManager, new FabricClientApplicationOperator(parameters), parameters);
        }
    }
}