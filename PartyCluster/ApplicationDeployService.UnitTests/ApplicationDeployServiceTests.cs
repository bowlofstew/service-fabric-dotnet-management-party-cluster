// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ApplicationDeployService.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Common;
    using Domain;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Mocks;
    using System.Fabric;

    [TestClass]
    public class ApplicationDeployServiceTests
    {
        [TestMethod]
        public async Task ProcessApplicationDeploymentRegister()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockApplicationOperator applicationOperator = new MockApplicationOperator
            {
                CopyPackageToImageStoreAsyncFunc = (cluster, appPackage, appType, appVersion) => Task.FromResult(appType + "_" + appVersion)
            };

            ApplicationDeployService target = new ApplicationDeployService(stateManager, applicationOperator, this.CreateServiceContext());
            ApplicationDeployment appDeployment = new ApplicationDeployment(
                "",
                ApplicationDeployStatus.Register,
                "",
                "type1",
                "1.0.0",
                "",
                "",
                DateTimeOffset.UtcNow);
            ApplicationDeployment actual = await target.ProcessApplicationDeployment(appDeployment, CancellationToken.None);

            Assert.AreEqual(ApplicationDeployStatus.Create, actual.Status);
        }

        [TestMethod]
        public async Task ProcessApplicationDeploymentCreate()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockApplicationOperator applicationOperator = new MockApplicationOperator
            {
                CopyPackageToImageStoreAsyncFunc = (cluster, appPackage, appType, appVersion) => Task.FromResult(appType + "_" + appVersion)
            };

            ApplicationDeployService target = new ApplicationDeployService(stateManager, applicationOperator, this.CreateServiceContext());
            ApplicationDeployment appDeployment = new ApplicationDeployment(
                "",
                ApplicationDeployStatus.Create,
                "",
                "type1",
                "1.0.0",
                "",
                "",
                DateTimeOffset.UtcNow);
            ApplicationDeployment actual = await target.ProcessApplicationDeployment(appDeployment, CancellationToken.None);

            Assert.AreEqual(ApplicationDeployStatus.Complete, actual.Status);
        }

        [TestMethod]
        public async Task GetServiceEndpointWithDomain()
        {
            string expectedDomain = "test.cloudapp.azure.com";
            string serviceAddress = "http://23.45.67.89/service/api";
            string expected = "http://test.cloudapp.azure.com:80/service/api";

            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockApplicationOperator applicationOperator = new MockApplicationOperator()
            {
                GetServiceEndpointFunc = (cluster, service) => Task.FromResult(serviceAddress)
            };

            ApplicationDeployService target = new ApplicationDeployService(stateManager, applicationOperator, this.CreateServiceContext());
            target.ApplicationPackages = new List<ApplicationPackageInfo>()
            {
                new ApplicationPackageInfo("type1", "1.0", "path/to/type1", "fabric:/app/service", "", "description", "")
            };

            IEnumerable<ApplicationView> actual = await target.GetApplicationDeploymentsAsync(expectedDomain, 19000);

            Assert.AreEqual(expected, actual.First().EntryServiceInfo.Address); 
        }

        private StatefulServiceContext CreateServiceContext()
        {
            return new StatefulServiceContext(
                new NodeContext(String.Empty, new NodeId(0, 0), 0, String.Empty, String.Empty),
                new MockCodePackageActivationContext(),
                String.Empty,
                new Uri("fabric:/Mock"),
                null,
                Guid.NewGuid(),
                0);
        }
    }
}