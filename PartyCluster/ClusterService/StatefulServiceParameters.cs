using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClusterService
{
    internal class StatefulServiceParameters
    {
        public StatefulServiceParameters(
            CodePackageActivationContext codePackageActivationContext,
            byte[] initializationData,
            Guid partitionId,
            Uri serviceName,
            string serviceTypeName,
            long replicaId)
        {
            this.CodePackageActivationContext = codePackageActivationContext;
            this.InitializationData = initializationData;
            this.PartitionId = partitionId;
            this.ServiceName = serviceName;
            this.ServiceTypeName = serviceTypeName;
            this.ReplicaId = replicaId;
        }

        public CodePackageActivationContext CodePackageActivationContext { get; private set; }

        public byte[] InitializationData { get; private set; }

        public Guid PartitionId { get; private set; }

        public Uri ServiceName { get; private set; }

        public string ServiceTypeName { get; private set; }

        public long ReplicaId { get; private set; }
    }
}
