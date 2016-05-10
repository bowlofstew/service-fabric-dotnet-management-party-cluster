// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService
{
    using System.Threading.Tasks;
    using PartyCluster.Domain;

    internal class FakeCaptcha : ICaptcha
    {
        public Task<bool> VerifyAsync(string captchaResponse)
        {
            return Task.FromResult(true);
        }
    }
}