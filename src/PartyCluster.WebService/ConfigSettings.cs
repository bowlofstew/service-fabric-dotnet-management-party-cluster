// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService
{
    using System.Collections.ObjectModel;
    using System.Fabric.Description;
    using System.Security;
    using PartyCluster.Common;

    public class ConfigSettings
    {
        public ConfigSettings(KeyedCollection<string, ConfigurationProperty> webServiceConfig)
        {
#if LOCAL
#else
            this.FacebookAppId = webServiceConfig["Facebook.AppId"].DecryptValue();
            this.FacebookAppSecret = webServiceConfig["Facebook.AppSecret"].DecryptValue();
            this.GithubClientId = webServiceConfig["Github.ClientId"].DecryptValue();
            this.GithubClientSecret = webServiceConfig["Github.ClientSecret"].DecryptValue();
            this.CsrfSalt = webServiceConfig["Csrf.Salt"].DecryptValue();
#endif
            this.AuthenticationTypeName = webServiceConfig["AuthenticationTypeName"].Value;
        }

        public SecureString FacebookAppId { get; private set; }

        public SecureString FacebookAppSecret { get; private set; }

        public SecureString GithubClientId { get; private set; }

        public SecureString GithubClientSecret { get; private set; }

        public SecureString CsrfSalt { get; private set; }

        public string AuthenticationTypeName { get; private set; }
    }
}