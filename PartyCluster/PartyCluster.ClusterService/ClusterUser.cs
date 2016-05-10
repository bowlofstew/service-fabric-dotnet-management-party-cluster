// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService
{
    using System.Runtime.Serialization;

    [DataContract]
    internal struct ClusterUser
    {
        public ClusterUser(string email, int port)
        {
            this.Email = email;
            this.Port = port;
        }

        [DataMember]
        public string Email { get; private set; }

        [DataMember]
        public int Port { get; private set; }
    }
}