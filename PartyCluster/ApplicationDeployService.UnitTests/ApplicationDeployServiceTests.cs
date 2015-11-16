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
        }

        [TestMethod]
        public async Task ProcessApplicationDeploymentRegister()
        {
        }

        [TestMethod]
        public async Task ProcessApplicationDeploymentCreate()
        {
        }

        private StatefulServiceParameters CreateServiceParameters()
        {
            return new StatefulServiceParameters(null, null, Guid.NewGuid(), null, null, 0);
        }
    }
}