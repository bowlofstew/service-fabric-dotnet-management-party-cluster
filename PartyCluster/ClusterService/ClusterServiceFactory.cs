using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data;

namespace ClusterService
{
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
                parameters);
        }
    }
}
