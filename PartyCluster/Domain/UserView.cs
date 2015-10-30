// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    using System.Runtime.Serialization;

    [DataContract]
    public class UserView
    {
        public UserView(string userEmail)
        {
            this.UserEmail = userEmail;
        }

        [DataMember]
        public string UserEmail { get; private set; }
    }
}
