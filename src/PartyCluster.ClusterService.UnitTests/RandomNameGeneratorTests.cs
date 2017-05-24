// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService.UnitTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class RandomNameGeneratorTests
    {
        public TestContext TestContext
        {
            get; set;
        }


        [TestMethod]
        public void GetClusterInternalNameTest()
        {
            var name1 = RandomNameGenerator.GetRandomNameString();
            var name2 = RandomNameGenerator.GetRandomNameString();
            this.TestContext.WriteLine("name1: {0}", name1);
            this.TestContext.WriteLine("name2: {0}", name2);
            Assert.IsFalse(string.IsNullOrWhiteSpace(name1));
            Assert.IsFalse(string.IsNullOrWhiteSpace(name2));
            Assert.AreEqual(8, name1.Length);
            Assert.AreEqual(8, name2.Length);
            Assert.IsFalse(name1.Equals(name2));
        }
    }
}
