// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System.Security;

    internal class ArmClusterOperatorSettings
    {
        public ArmClusterOperatorSettings(
            string region,
            SecureString clientId,
            SecureString clientSecret,
            SecureString authority,
            SecureString subscriptionId,
            SecureString username,
            SecureString password)
        {
            this.Region = region;
            this.ClientID = clientId;
            this.ClientSecret = clientSecret;
            this.Authority = authority;
            this.SubscriptionID = subscriptionId;
            this.Username = username;
            this.Password = password;
        }

        public string Region { get; private set; }

        public SecureString ClientID { get; private set; }

        public SecureString ClientSecret { get; private set; }

        public SecureString Authority { get; private set; }

        public SecureString SubscriptionID { get; private set; }

        public SecureString Username { get; private set; }

        public SecureString Password { get; private set; }
    }
}