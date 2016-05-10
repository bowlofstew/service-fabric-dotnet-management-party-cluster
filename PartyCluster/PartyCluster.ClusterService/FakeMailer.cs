// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using PartyCluster.Domain;

    public class FakeMailer : ISendMail
    {
        public Task SendJoinMail(
            string receipientAddress, string clusterAddress, int userPort, TimeSpan timeRemaining, DateTimeOffset clusterExpiration,
            IEnumerable<HyperlinkView> links)
        {
            return Task.FromResult(true);
        }
    }
}