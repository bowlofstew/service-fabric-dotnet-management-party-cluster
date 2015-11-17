// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Mocks
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Domain;

    public class MockApplicationOperator : IApplicationOperator
    {
        public MockApplicationOperator()
        {
            this.CopyPackageToImageStoreAsyncFunc = (cluster, appPackage, appType, appVersion) => Task.FromResult(appType + "_" + appVersion);
            this.CreateApplicationAsyncFunc = (cluster, appName, appType, appVersion) => Task.FromResult(true);
            this.RegisterApplicationAsyncFunc = (cluster, path) => Task.FromResult(true);
        }

        public Func<string, string, string, string, Task<String>> CopyPackageToImageStoreAsyncFunc { get; set; }

        public Func<string, string, string, string, Task> CreateApplicationAsyncFunc { get; set; }

        public Func<string, string, Task> RegisterApplicationAsyncFunc { get; set; }

        public Task<string> CopyPackageToImageStoreAsync(
            string cluster, string applicationPackagePath, string applicationTypeName, string applicationTypeVersion, CancellationToken token)
        {
            return this.CopyPackageToImageStoreAsyncFunc(cluster, applicationPackagePath, applicationTypeName, applicationTypeVersion);
        }

        public Task CreateApplicationAsync(string cluster, string applicationInstanceName, string applicationTypeName, string applicationTypeVersion, CancellationToken token)
        {
            return this.CreateApplicationAsyncFunc(cluster, applicationInstanceName, applicationTypeName, applicationTypeVersion);
        }

        public void Dispose()
        {
        }

        public Task<int> GetApplicationCountAsync(string cluster, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<int> GetServiceCountAsync(string cluster, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task RegisterApplicationAsync(string cluster, string imageStorePath, CancellationToken token)
        {
            return this.RegisterApplicationAsyncFunc(cluster, imageStorePath);
        }

        public void CloseConnection(string cluster)
        {
        }
    }
}