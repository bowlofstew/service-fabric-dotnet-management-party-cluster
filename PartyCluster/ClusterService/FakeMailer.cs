// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;
    using System.Threading.Tasks;
    using Domain;

    public class FakeMailer : ISendMail
    {
        public Task SendJoinMail(string receipientAddress, string clusterAddress, int userPort, TimeSpan timeRemaining, DateTimeOffset clusterExpiration)
        {
            return Task.FromResult(true);
        }
    }
}