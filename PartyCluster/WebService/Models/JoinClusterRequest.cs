// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService
{
    using System.Runtime.Serialization;

    [DataContract]
    public class JoinClusterRequest
    {
        public JoinClusterRequest(string userEmail, string captchaResponse)
        {
            this.CaptchaResponse = captchaResponse;
            this.UserEmail = userEmail;
        }

        [DataMember]
        public string UserEmail { get; private set; }

        [DataMember]
        public string CaptchaResponse { get; private set; }
    }
}