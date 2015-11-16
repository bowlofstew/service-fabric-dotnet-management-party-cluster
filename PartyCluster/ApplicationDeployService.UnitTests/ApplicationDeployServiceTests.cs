// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ApplicationDeployService.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Common;
    using Domain;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Mocks;

    [TestClass]
    public class ApplicationDeployServiceTests
    {
        [TestMethod]
        public async Task QueueApplicationDeploymentSuccessful()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockApplicationOperator applicationOperator = new MockApplicationOperator();
            ApplicationDeployService target = new ApplicationDeployService(stateManager, applicationOperator, this.CreateServiceParameters());

            target.ApplicationPackages = new List<ApplicationPackageInfo>()
            {
                new ApplicationPackageInfo("type1", "1.0", "path/to/type1"),
                new ApplicationPackageInfo("type2", "2.0", "path/to/type2")
            };

            string clusterAddress = "test";

            IEnumerable<Guid> result = await target.QueueApplicationDeployment(clusterAddress);

            Assert.AreEqual(2, result.Count());

            foreach (Guid actual in result)
            {
                Assert.AreEqual<ApplicationDeployStatus>(ApplicationDeployStatus.Copy, await target.Status(actual));
            }
        }

        [TestMethod]
        public async Task ProcessApplicationDeploymentCopy()
        {
            string type = "type1";
            string version = "1.0";
            string expected = "type1_1.0";

            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockApplicationOperator applicationOperator = new MockApplicationOperator
            {
                CopyPackageToImageStoreAsyncFunc = (cluster, appPackage, appType, appVersion) => Task.FromResult(appType + "_" + appVersion)
            };

            ApplicationDeployService target = new ApplicationDeployService(stateManager, applicationOperator, this.CreateServiceParameters());
            ApplicationDeployment appDeployment = new ApplicationDeployment("", ApplicationDeployStatus.Copy, "", type, version, "", "");
            ApplicationDeployment actual = await target.ProcessApplicationDeployment(appDeployment);

            Assert.AreEqual(expected, actual.ImageStorePath);
            Assert.AreEqual(ApplicationDeployStatus.Register, actual.Status);
        }

        [TestMethod]
        public async Task ProcessApplicationDeploymentRegister()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockApplicationOperator applicationOperator = new MockApplicationOperator
            {
                CopyPackageToImageStoreAsyncFunc = (cluster, appPackage, appType, appVersion) => Task.FromResult(appType + "_" + appVersion)
            };

            ApplicationDeployService target = new ApplicationDeployService(stateManager, applicationOperator, this.CreateServiceParameters());
            ApplicationDeployment appDeployment = new ApplicationDeployment("", ApplicationDeployStatus.Register, "", "type1", "1.0.0", "", "");
            ApplicationDeployment actual = await target.ProcessApplicationDeployment(appDeployment);
            
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

            ApplicationDeployService target = new ApplicationDeployService(stateManager, applicationOperator, this.CreateServiceParameters());
            ApplicationDeployment appDeployment = new ApplicationDeployment("", ApplicationDeployStatus.Create, "", "type1", "1.0.0", "", "");
            ApplicationDeployment actual = await target.ProcessApplicationDeployment(appDeployment);

            Assert.AreEqual(ApplicationDeployStatus.Complete, actual.Status);
        }

        private StatefulServiceParameters CreateServiceParameters()
        {
            return new StatefulServiceParameters(null, null, Guid.NewGuid(), null, null, 0);
        }
    }
}