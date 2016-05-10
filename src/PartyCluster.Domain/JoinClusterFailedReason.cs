// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.Domain
{
    public enum JoinClusterFailedReason : byte
    {
        ClusterFull,

        ClusterExpired,

        ClusterNotReady,

        ClusterDoesNotExist,

        UserAlreadyJoined,

        NoPortsAvailable,

        SendMailFailed
    }
}