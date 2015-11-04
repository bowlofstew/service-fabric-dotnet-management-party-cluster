// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    using System;
    using System.Runtime.Serialization;

    [Serializable]
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
            this.Reason = (JoinClusterFailedReason) info.GetByte("Reason");
        }

        public JoinClusterFailedReason Reason { get; }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("Reason", this.Reason);
        }
    }
}