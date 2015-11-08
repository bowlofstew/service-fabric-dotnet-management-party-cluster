// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    [DataContract]
    internal struct Cluster
    {
        private static Random random = new Random();

        public Cluster(string internalName)
            : this(internalName,
                  ClusterStatus.New,
                  0,
                  0,
                  String.Empty,
                  new int[0],
                  new ClusterUser[0],
                  DateTimeOffset.MaxValue)
        {
        }

        public Cluster(ClusterStatus status, Cluster copyFrom)
            : this (copyFrom.InternalName,
                  status, 
                  copyFrom.AppCount, 
                  copyFrom.ServiceCount, 
                  copyFrom.Address, 
                  new List<int>(copyFrom.Ports),
                  new List<ClusterUser>(copyFrom.Users),
                  copyFrom.CreatedOn)
        {
        }

        public Cluster(
            string internalName,
            ClusterStatus status,
            int appCount,
            int serviceCount,
            string address,
            IEnumerable<int> ports,
            IEnumerable<ClusterUser> users,
            DateTimeOffset createdOn)
        {
            this.InternalName = internalName;
            this.Status = status;
            this.AppCount = appCount;
            this.ServiceCount = serviceCount;
            this.Address = address;
            this.Ports = ports;
            this.Users = users;
            this.CreatedOn = createdOn;
        }

        [DataMember]
        public string InternalName { get; private set; }

        [DataMember]
        public ClusterStatus Status { get; private set; }

        [DataMember]
        public int AppCount { get; private set; }

        [DataMember]
        public int ServiceCount { get; private set; }

        [DataMember]
        public string Address { get; private set; }

        [DataMember]
        public IEnumerable<int> Ports { get; private set; }

        [DataMember]
        public IEnumerable<ClusterUser> Users { get; private set; }

        [DataMember]
        public DateTimeOffset CreatedOn { get; private set; }
    }
}