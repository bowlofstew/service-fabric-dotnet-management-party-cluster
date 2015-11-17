// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    using System;
    using System.Threading.Tasks;

    public interface IApplicationOperator : IDisposable
    {
        Task CreateApplicationAsync(string cluster, string applicationInstanceName, string applicationTypeName, string applicationTypeVersion);
        Task<string> CopyPackageToImageStoreAsync(string cluster, string applicationPackagePath, string applicationTypeName, string applicationTypeVersion);
        Task RegisterApplicationAsync(string cluster, string imageStorePath);
        Task<int> GetApplicationCountAsync(string cluster);
        Task<int> GetServiceCountAsync(string cluster);
    }
}