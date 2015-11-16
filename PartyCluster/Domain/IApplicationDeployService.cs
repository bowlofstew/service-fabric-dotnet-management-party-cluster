// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services.Remoting;

    public interface IApplicationDeployService : IService
    {
        Task<IEnumerable<Guid>> QueueApplicationDeployment(string cluster);
        Task<ApplicationDeployStatus> Status(Guid deployId);
    }
}