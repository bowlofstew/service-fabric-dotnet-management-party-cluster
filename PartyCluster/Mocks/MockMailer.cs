// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Mocks
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Domain;

    public class MockMailer : ISendMail
    {
        public Func<string, string, int, TimeSpan, DateTimeOffset, IEnumerable<HyperlinkView>, Task> SendJoinMailFunc { get; set; }

        public MockMailer()
        {
            SendJoinMailFunc = (receipientAddress, clusterAddress, userPort, timeRemaining, clusterExpiration, links) => Task.FromResult(true);
        }

        public Task SendJoinMail(string receipientAddress, string clusterAddress, int userPort, TimeSpan timeRemaining, DateTimeOffset clusterExpiration, IEnumerable<HyperlinkView> links)
        {
            return SendJoinMailFunc(receipientAddress, clusterAddress, userPort, timeRemaining, clusterExpiration, links);
        }
    }
}