// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService
{
    internal enum ClusterStatus
    {
        New,

        Creating,

        Ready,

        Remove,

        Deleting,

        Deleted
    }
}