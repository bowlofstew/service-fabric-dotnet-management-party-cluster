// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Domain;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Mocks;

    [TestClass]
    public class ClusterServiceTests
    {
        private Random random = new Random(7);
        private object locker = new object();

        /// <summary>
        /// The cluster list should filter out any clusters that are not ready.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task GetClusterList()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            int readyClusters = 10;
            int deletingCluster = 4;
            int newClusters = 2;
            int removeClusters = 1;

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(tx, dictionary, readyClusters, ClusterStatus.Ready);
                await this.AddClusters(tx, dictionary, deletingCluster, ClusterStatus.Deleting);
                await this.AddClusters(tx, dictionary, newClusters, ClusterStatus.New);
                await this.AddClusters(tx, dictionary, removeClusters, ClusterStatus.Remove);
                await tx.CommitAsync();
            }

            IEnumerable<ClusterView> actual = await target.GetClusterListAsync();

            Assert.AreEqual(readyClusters, actual.Count());
        }

        /// <summary>
        /// First time around there are no clusters. This tests that the minimum number of clusters is created initially.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task BalanceClusters()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            await target.BalanceClustersAsync(config.MinimumClusterCount);

            ConditionalResult<IReliableDictionary<int, Cluster>> result =
                await stateManager.TryGetAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(config.MinimumClusterCount, await result.Value.GetCountAsync());
            Assert.IsTrue(result.Value.All(x => x.Value.Status == ClusterStatus.New));
        }

        /// <summary>
        /// The total amount of active clusters should never go below the min threshold.
        /// When the current active cluster count is below min, and the new target is greater than current but still below min, bump the amount up to min.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task BalanceClustersIncreaseBelowMin()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int readyCount = (int) Math.Floor(config.MinimumClusterCount/5D);
            int newCount = readyCount;
            int creatingCount = readyCount;

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(tx, dictionary, readyCount, ClusterStatus.Ready);
                await this.AddClusters(tx, dictionary, newCount, ClusterStatus.New);
                await this.AddClusters(tx, dictionary, creatingCount, ClusterStatus.Creating);
                await this.AddClusters(tx, dictionary, config.MinimumClusterCount, ClusterStatus.Deleting);
                await tx.CommitAsync();
            }

            await target.BalanceClustersAsync(readyCount*4);

            Assert.AreEqual(
                config.MinimumClusterCount,
                dictionary.Count(
                    x =>
                        x.Value.Status == ClusterStatus.Ready ||
                        x.Value.Status == ClusterStatus.New ||
                        x.Value.Status == ClusterStatus.Creating));
        }

        /// <summary>
        /// The total amount of active clusters should never go below the min threshold.
        /// When the request amount is below the minimum threshold, and the new target is less than current, bump the amount up to min.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task BalanceClustersDecreaseBelowMin()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int readyCount = config.MinimumClusterCount - 1;
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(tx, dictionary, readyCount, ClusterStatus.Ready);
                await tx.CommitAsync();
            }

            await target.BalanceClustersAsync(config.MinimumClusterCount - 2);

            Assert.AreEqual(readyCount, dictionary.Count(x => x.Value.Status == ClusterStatus.Ready));
            Assert.AreEqual(config.MinimumClusterCount - readyCount, dictionary.Count(x => x.Value.Status == ClusterStatus.New));
        }

        /// <summary>
        /// The total amount of active clusters should never go below the min threshold.
        /// When the request amount is above the minimum threshold, and the new target is less than min, only remove down to min.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task BalanceClustersMinThreshold()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(tx, dictionary, config.MinimumClusterCount, ClusterStatus.Ready);
                await tx.CommitAsync();
            }

            await target.BalanceClustersAsync(config.MinimumClusterCount - 1);

            Assert.AreEqual(config.MinimumClusterCount, await dictionary.GetCountAsync());
            Assert.IsTrue(dictionary.All(x => x.Value.Status == ClusterStatus.Ready));
        }

        /// <summary>.
        /// The total amount of active clusters should never go above the max threshold.
        /// Only add clusters up to the limit considering only active clusters.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task BalanceClustersMaxThreshold()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            int readyClusters = 10;
            int deletingClusterCount = 20;
            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(tx, dictionary, readyClusters, ClusterStatus.Ready);
                await this.AddClusters(tx, dictionary, deletingClusterCount, ClusterStatus.Deleting);
                await tx.CommitAsync();
            }

            await target.BalanceClustersAsync(config.MaximumClusterCount + 1);

            Assert.AreEqual(config.MaximumClusterCount + deletingClusterCount, await dictionary.GetCountAsync());
            Assert.AreEqual(config.MaximumClusterCount - readyClusters, dictionary.Count(x => x.Value.Status == ClusterStatus.New));
            Assert.AreEqual(readyClusters, dictionary.Count(x => x.Value.Status == ClusterStatus.Ready));
        }

        /// <summary>.
        /// The total amount of active clusters should never go above the max threshold.
        /// When the total active clusters is above max, and the new total is greater than current, remove clusters down to the minimum.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task BalanceClustersIncreaseAboveMax()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            int aboveMax = 10;
            int readyClusters = config.MaximumClusterCount + aboveMax;
            int deletingClusterCount = 20;
            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(tx, dictionary, readyClusters, ClusterStatus.Ready);
                await this.AddClusters(tx, dictionary, deletingClusterCount, ClusterStatus.Deleting);
                await tx.CommitAsync();
            }

            await target.BalanceClustersAsync(readyClusters + 10);

            Assert.AreEqual(config.MaximumClusterCount, dictionary.Count(x => x.Value.Status == ClusterStatus.Ready));
            Assert.AreEqual(aboveMax, dictionary.Count(x => x.Value.Status == ClusterStatus.Remove));
            Assert.AreEqual(deletingClusterCount, dictionary.Count(x => x.Value.Status == ClusterStatus.Deleting));
        }

        /// <summary>.
        /// The total amount of active clusters should never go above the max threshold.
        /// When the total active clusters is above max, and the new total is greater than current, remove clusters down to the minimum.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task BalanceClustersDecreaseAboveMax()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            int aboveMax = 10;
            int readyClusters = config.MaximumClusterCount + aboveMax;
            int deletingClusterCount = 20;
            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(tx, dictionary, readyClusters, ClusterStatus.Ready);
                await this.AddClusters(tx, dictionary, deletingClusterCount, ClusterStatus.Deleting);
                await tx.CommitAsync();
            }

            await target.BalanceClustersAsync(readyClusters - (aboveMax/2));

            Assert.AreEqual(config.MaximumClusterCount, dictionary.Count(x => x.Value.Status == ClusterStatus.Ready));
            Assert.AreEqual(aboveMax, dictionary.Count(x => x.Value.Status == ClusterStatus.Remove));
            Assert.AreEqual(deletingClusterCount, dictionary.Count(x => x.Value.Status == ClusterStatus.Deleting));
        }

        /// <summary>
        /// Tests that only active clusters are considered for removal without going below the minimum threshold.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task BalanceClusterDecreaseAlreadyDeleting()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int readyCount = 5 + config.MinimumClusterCount;
            int deletingCount = 10;
            int targetCount = config.MinimumClusterCount/2;

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(tx, dictionary, readyCount, ClusterStatus.Ready);
                await this.AddClusters(tx, dictionary, deletingCount, ClusterStatus.Deleting);

                await tx.CommitAsync();
            }

            await target.BalanceClustersAsync(targetCount);

            Assert.AreEqual(readyCount - config.MinimumClusterCount, dictionary.Count(x => x.Value.Status == ClusterStatus.Remove));
            Assert.AreEqual(config.MinimumClusterCount, dictionary.Count(x => x.Value.Status == ClusterStatus.Ready));
            Assert.AreEqual(deletingCount, dictionary.Count(x => x.Value.Status == ClusterStatus.Deleting));
        }

        /// <summary>
        /// BalanceClustersAsync should not flag to remove clusters that still have users in them 
        /// when given a target count below the current count.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task BalanceClustersDecreaseNonEmpty()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int withUsers = config.MinimumClusterCount + 5;
            int withoutUsers = 10;
            int targetCount = (withUsers + withoutUsers) - 11;

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(
                    tx,
                    dictionary,
                    withUsers,
                    () => this.CreateCluster(
                        ClusterStatus.Ready,
                        new List<ClusterUser>() {new ClusterUser()}));

                await this.AddClusters(tx, dictionary, withoutUsers, ClusterStatus.Ready);

                await tx.CommitAsync();
            }

            await target.BalanceClustersAsync(targetCount);

            Assert.AreEqual(withUsers, dictionary.Select(x => x.Value).Count(x => x.Status == ClusterStatus.Ready));
            Assert.AreEqual(withoutUsers, dictionary.Select(x => x.Value).Count(x => x.Status == ClusterStatus.Remove));
        }

        /// <summary>
        /// When the number of users in all the clusters reaches the UserCapacityHighPercentThreshold value, 
        /// increase the number of clusters by (100 - UserCapacityHighPercentThreshold)%
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TargetClusterCapacityIncrease()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int clusterCount = config.MinimumClusterCount;
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(
                    tx,
                    dictionary,
                    clusterCount,
                    () => this.CreateCluster(
                        ClusterStatus.Ready,
                        new List<ClusterUser>(
                            Enumerable.Repeat(
                                new ClusterUser(),
                                (int) Math.Ceiling((double) config.MaximumUsersPerCluster*config.UserCapacityHighPercentThreshold)))));

                await this.AddClusters(
                    tx,
                    dictionary,
                    5,
                    () => this.CreateCluster(
                        ClusterStatus.Remove,
                        new List<ClusterUser>(Enumerable.Repeat(new ClusterUser(), config.MaximumUsersPerCluster))));

                await this.AddClusters(tx, dictionary, 5, ClusterStatus.Deleting);

                await tx.CommitAsync();
            }

            int expected = clusterCount + (int) Math.Ceiling(clusterCount*(1 - config.UserCapacityHighPercentThreshold));
            int actual = await target.GetTargetClusterCapacityAsync();

            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// When the number of users in all the clusters reaches the UserCapacityHighPercentThreshold value, 
        /// increase the number of clusters without going over MaximumClusterCount. 
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TargetClusterCapacityIncreaseAtMaxCount()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int clusterCount = config.MaximumClusterCount - 1;
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(
                    tx,
                    dictionary,
                    clusterCount,
                    () => this.CreateCluster(
                        ClusterStatus.Ready,
                        new List<ClusterUser>(
                            Enumerable.Repeat(
                                new ClusterUser(),
                                (int) Math.Ceiling((double) config.MaximumUsersPerCluster*config.UserCapacityHighPercentThreshold)))));

                await this.AddClusters(
                    tx,
                    dictionary,
                    5,
                    () => this.CreateCluster(
                        ClusterStatus.Remove,
                        new List<ClusterUser>(Enumerable.Repeat(new ClusterUser(), config.MaximumUsersPerCluster))));

                await tx.CommitAsync();
            }

            int actual = await target.GetTargetClusterCapacityAsync();

            Assert.AreEqual(config.MaximumClusterCount, actual);
        }

        /// <summary>
        /// When the number of users in all the clusters reaches the UserCapacityLowPercentThreshold value, 
        /// decrease the number of clusters by high-low% capacity
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TargetClusterCapacityDecrease()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int clusterCount = config.MaximumClusterCount;
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(
                    tx,
                    dictionary,
                    clusterCount,
                    () => this.CreateCluster(
                        ClusterStatus.Ready,
                        new List<ClusterUser>(
                            Enumerable.Repeat(
                                new ClusterUser(),
                                (int) Math.Floor((double) config.MaximumUsersPerCluster*config.UserCapacityLowPercentThreshold)))));

                await this.AddClusters(
                    tx,
                    dictionary,
                    5,
                    () => this.CreateCluster(
                        ClusterStatus.Remove,
                        new List<ClusterUser>(Enumerable.Repeat(new ClusterUser(), config.MaximumUsersPerCluster))));

                await tx.CommitAsync();
            }

            int expected = clusterCount - (int) Math.Floor(clusterCount*(config.UserCapacityHighPercentThreshold - config.UserCapacityLowPercentThreshold));
            int actual = await target.GetTargetClusterCapacityAsync();

            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// When the number of users in all the clusters reaches the UserCapacityLowPercentThreshold value, 
        /// decrease the number of clusters without going below the min threshold.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TargetClusterCapacityDecreaseAtMinCount()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int clusterCount = config.MinimumClusterCount + 1;
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await this.AddClusters(
                    tx,
                    dictionary,
                    clusterCount,
                    () => this.CreateCluster(
                        ClusterStatus.Ready,
                        new List<ClusterUser>(
                            Enumerable.Repeat(
                                new ClusterUser(),
                                (int) Math.Floor((double) config.MaximumUsersPerCluster*config.UserCapacityLowPercentThreshold)))));

                await tx.CommitAsync();
            }

            int expected = config.MinimumClusterCount;
            int actual = await target.GetTargetClusterCapacityAsync();

            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// Make sure ProcessClustersAsync is saving the updated cluster object.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ProcessClustersAsyncSaveChanges()
        {
            int key = 1;
            string nameTemplate = "Test:{0}";
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterConfig config = new ClusterConfig();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                CreateClusterAsyncFunc = name => { return Task.FromResult(String.Format(nameTemplate, name)); }
            };

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            Cluster original = this.CreateCluster(ClusterStatus.New);
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await dictionary.SetAsync(tx, key, original);
            }

            ClusterService target = new ClusterService(clusterOperator, null, stateManager, this.CreateServiceParameters(), config);

            await target.ProcessClustersAsync();

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                Cluster actual = (await dictionary.TryGetValueAsync(tx, key)).Value;

                Assert.AreNotEqual(original, actual);
            }
        }

        /// <summary>
        /// Make sure ProcessClustersAsync is saving the updated cluster object.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ProcessClustersAsyncDelete()
        {
            int key = 1;
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterConfig config = new ClusterConfig();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                GetClusterStatusAsyncFunc = (name) => Task.FromResult(ClusterOperationStatus.ClusterNotFound)
            };

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            Cluster original = this.CreateCluster(ClusterStatus.Deleting);
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await dictionary.SetAsync(tx, key, original);
            }

            ClusterService target = new ClusterService(clusterOperator, null, stateManager, this.CreateServiceParameters(), config);

            await target.ProcessClustersAsync();

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                ConditionalResult<Cluster> actual = await dictionary.TryGetValueAsync(tx, key);

                Assert.IsFalse(actual.HasValue);
            }
        }

        /// <summary>
        /// A new cluster should initiate a create cluster operation and switch its status to "creating" if successful.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ProcessNewCluster()
        {
            bool calledActual = false;
            string nameTemplate = "Test:{0}";
            string nameActual = null;

            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                CreateClusterAsyncFunc = name =>
                {
                    nameActual = name;
                    calledActual = true;
                    return Task.FromResult(String.Format(nameTemplate, name));
                }
            };

            ClusterConfig config = new ClusterConfig();
            ClusterService target = new ClusterService(clusterOperator, null, stateManager, this.CreateServiceParameters(), config);

            Cluster cluster = this.CreateCluster(ClusterStatus.New);

            Cluster actual = await target.ProcessClusterStatusAsync(cluster);

            Assert.IsTrue(calledActual);
            Assert.AreEqual(ClusterStatus.Creating, actual.Status);
            Assert.AreEqual(String.Format(nameTemplate, nameActual), actual.Address);
        }

        /// <summary>
        /// A creating cluster should set its status to ready and populate fields when the cluster creation has completed.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ProcessCreatingClusterSuccess()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                GetClusterStatusAsyncFunc = name => Task.FromResult(ClusterOperationStatus.Ready)
            };

            ClusterConfig config = new ClusterConfig();
            ClusterService target = new ClusterService(clusterOperator, null, stateManager, this.CreateServiceParameters(), config);
            Cluster cluster = this.CreateCluster(ClusterStatus.Creating);

            Cluster actual = await target.ProcessClusterStatusAsync(cluster);

            Assert.AreEqual(ClusterStatus.Ready, actual.Status);
            Assert.IsTrue(actual.CreatedOn.ToUniversalTime() <= DateTimeOffset.UtcNow);
            actual.Ports.SequenceEqual(await clusterOperator.GetClusterPortsAsync(""));
        }

        /// <summary>
        /// A creating cluster should set the status to "remove" if creation failed so that the failed deployment can be deleted.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ProcessCreatingClusterFailed()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                GetClusterStatusAsyncFunc = name => Task.FromResult(ClusterOperationStatus.CreateFailed)
            };

            ClusterConfig config = new ClusterConfig();
            ClusterService target = new ClusterService(clusterOperator, null, stateManager, this.CreateServiceParameters(), config);
            Cluster cluster = this.CreateCluster(ClusterStatus.Creating);

            Cluster actual = await target.ProcessClusterStatusAsync(cluster);

            Assert.AreEqual(ClusterStatus.Remove, actual.Status);
            Assert.AreEqual(0, actual.Ports.Count());
            Assert.AreEqual(0, actual.Users.Count());
        }

        /// <summary>
        /// A cluster marked for removal should initiate a delete cluster operation and switch its status to "deleting" if successful.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ProcessRemove()
        {
            bool calledActual = false;
            string nameActual = null;

            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                DeleteClusterAsyncFunc = name =>
                {
                    nameActual = name;
                    calledActual = true;
                    return Task.FromResult(true);
                }
            };

            ClusterConfig config = new ClusterConfig();
            ClusterService target = new ClusterService(clusterOperator, null, stateManager, this.CreateServiceParameters(), config);

            Cluster cluster = this.CreateCluster(ClusterStatus.Remove);

            Cluster actual = await target.ProcessClusterStatusAsync(cluster);

            Assert.IsTrue(calledActual);
            Assert.AreEqual(ClusterStatus.Deleting, actual.Status);
        }

        /// <summary>
        /// When deleting is complete, set the status to deleted.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ProcessDeletingSuccessful()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                GetClusterStatusAsyncFunc = domain => Task.FromResult(ClusterOperationStatus.ClusterNotFound)
            };

            ClusterConfig config = new ClusterConfig();
            ClusterService target = new ClusterService(clusterOperator, null, stateManager, this.CreateServiceParameters(), config);

            Cluster cluster = this.CreateCluster(ClusterStatus.Deleting);

            Cluster actual = await target.ProcessClusterStatusAsync(cluster);

            Assert.AreEqual(ClusterStatus.Deleted, actual.Status);
        }

        /// <summary>
        /// A cluster should be marked for removal when its time limit has elapsed.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ProcessRemoveTimeLimit()
        {
            ClusterConfig config = new ClusterConfig()
            {
                MaximumClusterUptime = TimeSpan.FromHours(2)
            };

            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            Cluster cluster = new Cluster(
                "test",
                ClusterStatus.Ready,
                0,
                0,
                String.Empty,
                new int[0],
                new ClusterUser[0],
                DateTimeOffset.UtcNow - config.MaximumClusterUptime);

            Cluster actual = await target.ProcessClusterStatusAsync(cluster);

            Assert.AreEqual(ClusterStatus.Remove, actual.Status);
        }

        [TestMethod]
        public async Task JoinClusterSuccessful()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, new MockMailer(), stateManager, this.CreateServiceParameters(), config);

            int id = 5;
            string email = "test@test.com";
            Cluster cluster = new Cluster("test", ClusterStatus.Ready, 0, 0, "", new[] {80}, new ClusterUser[0], DateTimeOffset.UtcNow);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await dictionary.AddAsync(tx, id, cluster);
                await tx.CommitAsync();
            }

            await target.JoinClusterAsync(id, new UserView(email));

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                Cluster actual = (await dictionary.TryGetValueAsync(tx, id)).Value;

                Assert.AreEqual(1, actual.Users.Count(x => x.Email == email));
            }
        }

        [TestMethod]
        public async Task JoinClusterFull()
        {
            ClusterConfig config = new ClusterConfig() {MaximumUsersPerCluster = 1};
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            int id = 5;
            Cluster cluster = new Cluster(
                "test",
                ClusterStatus.Ready,
                0,
                0,
                "",
                new[] {80},
                new[] {new ClusterUser()},
                DateTimeOffset.UtcNow);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await dictionary.AddAsync(tx, id, cluster);
                await tx.CommitAsync();
            }

            try
            {
                await target.JoinClusterAsync(id, new UserView("email"));
                Assert.Fail("JoinClusterFailedException not thrown.");
            }
            catch (JoinClusterFailedException result)
            {
                Assert.AreEqual(JoinClusterFailedReason.ClusterFull, result.Reason);
            }
        }

        [TestMethod]
        public async Task JoinClusterNotReady()
        {
            ClusterConfig config = new ClusterConfig() {MaximumUsersPerCluster = 2};
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            int id = 5;
            Cluster cluster = new Cluster(
                "test",
                ClusterStatus.Creating,
                0,
                0,
                "",
                new[] {80},
                new ClusterUser[0],
                DateTimeOffset.UtcNow);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await dictionary.AddAsync(tx, id, cluster);
                await tx.CommitAsync();
            }

            try
            {
                await target.JoinClusterAsync(id, new UserView("email"));
                Assert.Fail("JoinClusterFailedException not thrown.");
            }
            catch (JoinClusterFailedException result)
            {
                Assert.AreEqual(JoinClusterFailedReason.ClusterNotReady, result.Reason);
            }
        }

        [TestMethod]
        public async Task JoinClusterUserAlreadyExists()
        {
            ClusterConfig config = new ClusterConfig() {MaximumUsersPerCluster = 2};
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            int id = 5;
            string email = "test@test.com";
            Cluster cluster = new Cluster(
                "test",
                ClusterStatus.Ready,
                0,
                0,
                "",
                new[] {80, 81},
                new[] {new ClusterUser(email, 80)},
                DateTimeOffset.UtcNow);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await dictionary.AddAsync(tx, id, cluster);
                await tx.CommitAsync();
            }

            try
            {
                await target.JoinClusterAsync(id, new UserView(email));
                Assert.Fail("JoinClusterFailedException not thrown.");
            }
            catch (JoinClusterFailedException result)
            {
                Assert.AreEqual(JoinClusterFailedReason.UserAlreadyJoined, result.Reason);
            }
        }

        [TestMethod]
        public async Task JoinClusterExpired()
        {
            ClusterConfig config = new ClusterConfig() {MaximumUsersPerCluster = 2};
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            int id = 5;
            Cluster cluster = new Cluster(
                "test",
                ClusterStatus.Ready,
                0,
                0,
                "",
                new[] {80, 81},
                new ClusterUser[0],
                DateTimeOffset.UtcNow - (config.MaximumClusterUptime + TimeSpan.FromSeconds(1)));

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await dictionary.AddAsync(tx, id, cluster);
                await tx.CommitAsync();
            }

            try
            {
                await target.JoinClusterAsync(id, new UserView("email"));
                Assert.Fail("JoinClusterFailedException not thrown.");
            }
            catch (JoinClusterFailedException result)
            {
                Assert.AreEqual(JoinClusterFailedReason.ClusterExpired, result.Reason);
            }
        }

        [TestMethod]
        public async Task JoinClusterNoPortsAvailable()
        {
            ClusterConfig config = new ClusterConfig() {MaximumUsersPerCluster = 2};
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, null, stateManager, this.CreateServiceParameters(), config);

            int id = 5;
            string email = "test@test.com";
            Cluster cluster = new Cluster(
                "test",
                ClusterStatus.Ready,
                0,
                0,
                "",
                new[] {80},
                new[] {new ClusterUser(email, 80)},
                DateTimeOffset.UtcNow);

            IReliableDictionary<int, Cluster> dictionary =
                await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await dictionary.AddAsync(tx, id, cluster);
                await tx.CommitAsync();
            }

            try
            {
                await target.JoinClusterAsync(id, new UserView("test"));
                Assert.Fail("JoinClusterFailedException not thrown.");
            }
            catch (JoinClusterFailedException result)
            {
                Assert.AreEqual(JoinClusterFailedReason.NoPortsAvailable, result.Reason);
            }
        }

        private StatefulServiceParameters CreateServiceParameters()
        {
            return new StatefulServiceParameters(null, null, Guid.NewGuid(), null, null, 0);
        }

        private int GetRandom()
        {
            lock (this.locker)
            {
                return this.random.Next();
            }
        }

        private async Task AddClusters(ITransaction tx, IReliableDictionary<int, Cluster> dictionary, int count, Func<Cluster> newCluster)
        {
            for (int i = 0; i < count; ++i)
            {
                await dictionary.AddAsync(tx, this.GetRandom(), newCluster());
            }
        }

        private Task AddClusters(ITransaction tx, IReliableDictionary<int, Cluster> dictionary, int count, ClusterStatus status)
        {
            return this.AddClusters(tx, dictionary, count, () => this.CreateCluster(status));
        }

        private Cluster CreateCluster(ClusterStatus status)
        {
            return new Cluster(
                status,
                new Cluster("test"));
        }

        private Cluster CreateCluster(ClusterStatus status, IEnumerable<ClusterUser> users)
        {
            return new Cluster(
                "test",
                status,
                0,
                0,
                String.Empty,
                new int[0],
                users,
                DateTimeOffset.MaxValue);
        }
    }
}