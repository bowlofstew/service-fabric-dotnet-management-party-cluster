// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Fabric;
    using System.Fabric.Description;
    using System.Net.Http;
    using System.Security;
    using System.Threading.Tasks;
    using Common;
    using Domain;
    using Newtonsoft.Json.Linq;

    internal class Recaptcha : ICaptcha
    {
        private SecureString key;
        private Uri verifyUrl;

        public Recaptcha(StatelessServiceContext serviceContext)
        {
            serviceContext.CodePackageActivationContext.ConfigurationPackageModifiedEvent
                += this.CodePackageActivationContext_ConfigurationPackageModifiedEvent;

            ConfigurationPackage configPackage = serviceContext.CodePackageActivationContext.GetConfigurationPackageObject("Config");

            this.UpdateConfigSettings(configPackage.Settings);
        }

        public async Task<bool> VerifyAsync(string captchaResponse)
        {
            if (String.IsNullOrEmpty(captchaResponse))
            {
                throw new ArgumentNullException("captchaResponse");
            }

            HttpClient client = new HttpClient();

            Dictionary<string, string> parameters = new Dictionary<string, string>()
            {
                {"secret", this.key.ToUnsecureString()},
                {"response", captchaResponse}
            };

            FormUrlEncodedContent content = new FormUrlEncodedContent(parameters);
            HttpResponseMessage httpResponseMessage = await client.PostAsync(this.verifyUrl, content);

            JObject responseObject = JObject.Parse(await httpResponseMessage.Content.ReadAsStringAsync());
            return responseObject["success"].Value<bool>();
        }

        private void CodePackageActivationContext_ConfigurationPackageModifiedEvent(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            this.UpdateConfigSettings(e.NewPackage.Settings);
        }

        private void UpdateConfigSettings(ConfigurationSettings settings)
        {
            KeyedCollection<string, ConfigurationProperty> parameters = settings.Sections["RecaptchaSettings"].Parameters;

            this.key = parameters["Key"].DecryptValue();
            this.verifyUrl = new Uri(parameters["VerifyUrl"].Value);
        }
    }
}