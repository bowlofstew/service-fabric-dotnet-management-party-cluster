// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IApplicationOperator : IDisposable
    {
        Task CreateApplicationAsync(string cluster, string applicationInstanceName, string applicationTypeName, string applicationTypeVersion, CancellationToken token);
        Task<string> CopyPackageToImageStoreAsync(string cluster, string applicationPackagePath, string applicationTypeName, string applicationTypeVersion, CancellationToken token);
        Task RegisterApplicationAsync(string cluster, string imageStorePath, CancellationToken token);
        Task<int> GetApplicationCountAsync(string cluster, CancellationToken token);
        Task<int> GetServiceCountAsync(string cluster, CancellationToken token);
        Task<string> GetServiceEndpoint(string cluster, Uri serviceInstanceUri, string serviceEndpointName, CancellationToken token);
        Task<bool> ApplicationExistsAsync(string cluster, string applicationInstanceName, CancellationToken token);
    }
}