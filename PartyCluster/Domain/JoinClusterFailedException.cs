// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    public class JoinClusterFailedException : Exception
    {
        public JoinClusterFailedException(JoinClusterFailedReason reason)
            : base(reason.ToString())
        {
            this.Reason = reason;
        }

        public JoinClusterFailedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        [DataMember]
        public JoinClusterFailedReason Reason { get; private set; }
    }

    public enum JoinClusterFailedReason
    {
        ClusterFull,

        ClusterExpired,

        ClusterNotReady,

        UserAlreadyJoined,

        NoPortsAvailable
    }
}