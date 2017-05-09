// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.Serialization;

    /// <summary>
    /// Represents a party cluster and its current status.
    /// </summary>
    /// <remarks>
    /// This struct is immutable so that it cannot be partially updated in local memory before a transaction completes.
    /// </remarks>
    [DataContract]
    internal struct Cluster
    {
        private static readonly string emptyClusterName = "_empty_";
        public static readonly Cluster Empty = new Cluster(emptyClusterName);

        /// <summary>
        /// Creates a cluster in the "New" state with the given name.
        /// </summary>
        /// <param name="internalName"></param>
        public Cluster(string internalName)
            : this(internalName,
                ClusterStatus.New,
                0,
                0,
                String.Empty,
                new int[0],
                new ClusterUser[0],
                DateTimeOffset.MaxValue,
                DateTimeOffset.MaxValue)
        {
        }

        /// <summary>
        /// Create a new instance copied from the given Cluster with the given status.
        /// </summary>
        /// <param name="status"></param>
        /// <param name="copyFrom"></param>
        public Cluster(ClusterStatus status, Cluster copyFrom)
            : this(copyFrom.InternalName,
                status,
                copyFrom.AppCount,
                copyFrom.ServiceCount,
                copyFrom.Address,
                new List<int>(copyFrom.Ports),
                new List<ClusterUser>(copyFrom.Users),
                copyFrom.CreatedOn,
                copyFrom.LifetimeStartedOn)
        {
        }

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        /// <param name="internalName"></param>
        /// <param name="status"></param>
        /// <param name="appCount"></param>
        /// <param name="serviceCount"></param>
        /// <param name="address"></param>
        /// <param name="ports"></param>
        /// <param name="users"></param>
        /// <param name="createdOn"></param>
        /// <param name="lifetimeStartedOn"></param>
        public Cluster(
            string internalName,
            ClusterStatus status,
            int appCount,
            int serviceCount,
            string address,
            IEnumerable<int> ports,
            IEnumerable<ClusterUser> users,
            DateTimeOffset createdOn,
            DateTimeOffset lifetimeStartedOn)
        {
            this.InternalName = internalName;
            this.Status = status;
            this.AppCount = appCount;
            this.ServiceCount = serviceCount;
            this.Address = address;
            this.Ports = ports;
            this.Users = users;
            this.CreatedOn = createdOn;
            this.LifetimeStartedOn = lifetimeStartedOn;
        }

        /// <summary>
        /// The name of the cluster (also the name of the of cluster resource in Azure).
        /// This name is part of the cluster address so it should not be exposed directly to users.
        /// </summary>
        [DataMember]
        public string InternalName { get; private set; }

        /// <summary>
        /// The current status of the cluster.
        /// </summary>
        [DataMember]
        public ClusterStatus Status { get; private set; }

        /// <summary>
        /// Number of application currently deployed to the cluster.
        /// </summary>
        [DataMember]
        public int AppCount { get; private set; }

        /// <summary>
        /// Number of services currently deployed to the cluster.
        /// </summary>
        [DataMember]
        public int ServiceCount { get; private set; }

        /// <summary>
        /// The cluster's public address. 
        /// </summary>
        [DataMember]
        public string Address { get; private set; }

        /// <summary>
        /// External ports exposed by the cluster.
        /// </summary>
        [DataMember]
        public IEnumerable<int> Ports { get; private set; }

        /// <summary>
        /// Users currently on the cluster.
        /// </summary>
        [DataMember]
        public IEnumerable<ClusterUser> Users { get; private set; }

        /// <summary>
        /// Timestamp of when the cluster was created.
        /// </summary>
        [DataMember]
        public DateTimeOffset CreatedOn { get; private set; }

        /// <summary>
        /// Timestamp of when the cluster lifetime starts. 
        /// The cluster lives for a fixed duration. 
        /// The lifetime may start at a point other than creation time, 
        /// for ex. it may start when a user joins the cluster.
        /// </summary>
        [DataMember]
        public DateTimeOffset LifetimeStartedOn { get; private set; }

        public bool IsEmpty { get { return this.InternalName == emptyClusterName; } }

        public bool IsJoinedBy(string userId)
        {
            return this.Users.Count() > 0
                && this.Users.Any(x => String.Equals(x.Email, userId, StringComparison.OrdinalIgnoreCase));
        }

        public TimeSpan RemainingLifetime()
        {
            if (this.LifetimeStartedOn == DateTimeOffset.MaxValue)
            {
                return TimeSpan.MaxValue;
            }

            return DateTimeOffset.UtcNow - this.LifetimeStartedOn.ToUniversalTime();
        }

        public Cluster ToRemoveState()
        {
            // clone the cluster and clear the assigned user and ports.
            return new Cluster(
                       this.InternalName,
                       ClusterStatus.Remove,
                       this.AppCount,
                       this.ServiceCount,
                       this.Address,
                       Enumerable.Empty<int>(),
                       Enumerable.Empty<ClusterUser>(),
                       this.CreatedOn,
                       this.LifetimeStartedOn);
        }
    }
}