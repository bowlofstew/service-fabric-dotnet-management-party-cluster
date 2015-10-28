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
    internal class Cluster
    {
        public Cluster()
        {
            this.Status = ClusterStatus.New;
            this.AppCount = 0;
            this.ServiceCount = 0;
            this.Address = String.Empty;
            this.Ports = new List<int>();
            this.Users = new List<ClusterUser>();
            this.CreatedOn = DateTimeOffset.MaxValue;
        }

        [DataMember]
        public ClusterStatus Status { get; set; }

        [DataMember]
        public int AppCount { get; set; }

        [DataMember]
        public int ServiceCount { get; set; }

        [DataMember]
        public string Address { get; set; }

        [DataMember]
        public IEnumerable<int> Ports { get; set; }

        [DataMember]
        public IList<ClusterUser> Users { get; set; }

        [DataMember]
        public DateTimeOffset CreatedOn { get; set; }
    }
}