// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService.Models
{
    using System.Runtime.Serialization;

    [DataContract]
    public class JoinClusterRequest
    {
        public JoinClusterRequest(string userEmail)
        {
            this.UserEmail = userEmail;
        }

        [DataMember]
        public string UserEmail { get; private set; }
    }
}