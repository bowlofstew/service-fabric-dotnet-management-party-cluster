// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    public struct ClusterView
    {
        public ClusterView(int clusterId, string name, int appCount, int serviceCount, int userCount, int capacity, TimeSpan timeRemaining)
        {
            this.ClusterId = clusterId;
            this.Name = name;
            this.ApplicationCount = appCount;
            this.ServiceCount = serviceCount;
            this.UserCount = userCount;
            this.Capacity = capacity;

            this.TimeRemaining = timeRemaining > TimeSpan.Zero
                ? String.Format("{0:hh\\:mm\\:ss}", timeRemaining)
                : "expired";
        }

        [DataMember]
        public int ClusterId { get; private set; }

        [DataMember]
        public string Name { get; private set; }

        [DataMember]
        public int ApplicationCount { get; private set; }

        [DataMember]
        public int ServiceCount { get; private set; }

        [DataMember]
        public int UserCount { get; private set; }

        [DataMember]
        public int Capacity { get; private set; }

        [DataMember]
        public string TimeRemaining { get; private set; }
    }
}