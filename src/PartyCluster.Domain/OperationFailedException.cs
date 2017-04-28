// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.Domain
{
    using System;
    using System.Runtime.Serialization;

    [Serializable]
    public class OperationFailedException : Exception
    {
        public OperationFailedException(OperationFailedReason reason)
            : base(reason.ToString())
        {
            this.Reason = reason;
        }

        public OperationFailedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            this.Reason = (OperationFailedReason) info.GetByte("Reason");
        }

        public OperationFailedReason Reason { get; }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("Reason", this.Reason);
        }
    }
}