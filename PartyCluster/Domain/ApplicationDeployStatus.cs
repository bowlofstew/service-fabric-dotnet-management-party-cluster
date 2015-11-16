// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    public enum ApplicationDeployStatus
    {
        None,

        Copy,

        Register,

        Create,

        Complete
    }
}