// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.Domain
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services.Remoting;

    public interface IClusterService : IService
    {
        Task<IEnumerable<ClusterView>> GetClusterListAsync();

        Task<UserView> JoinClusterAsync(int clusterId, string userEmail);

        /// <summary>
        /// Gets the status of the party for given user. 
        /// If user has joined a cluster the cluster information is returned. 
        /// Else it returns if there is room in the party.
        /// </summary>
        /// <param name="userEmail">The email of the user.</param>
        /// <returns>ClusterConnectionView instance.</returns>
        Task<UserView> GetPartyStatusAsync(string userEmail);

        /// <summary>
        /// Joins a random cluster from the list of available clusters.
        /// </summary>
        /// <param name="userEmail">The email of the user.</param>
        /// <returns>ClusterConnectionView instance.</returns>
        Task<UserView> JoinRandomClusterAsync(string userEmail);
    }
}