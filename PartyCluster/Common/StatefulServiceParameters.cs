// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Common
{
    using System;
    using System.Fabric;

    public class StatefulServiceParameters
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