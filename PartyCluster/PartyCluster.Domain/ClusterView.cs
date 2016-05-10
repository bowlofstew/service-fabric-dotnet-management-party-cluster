// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.Domain
{
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    public struct ClusterView
    {
        public ClusterView(int clusterId, int appCount, int serviceCount, int userCount, int maxUsers, TimeSpan timeRemaining)
        {
            this.ClusterId = clusterId;
            this.ApplicationCount = appCount;
            this.ServiceCount = serviceCount;
            this.UserCount = userCount;
            this.MaxUsers = maxUsers;
            this.TimeRemaining = timeRemaining;
        }

        [DataMember]
        public int ClusterId { get; private set; }

        [DataMember]
        public int ApplicationCount { get; private set; }

        [DataMember]
        public int ServiceCount { get; private set; }

        [DataMember]
        public int UserCount { get; private set; }

        [DataMember]
        public int MaxUsers { get; private set; }

        [DataMember]
        public TimeSpan TimeRemaining { get; private set; }
    }
}