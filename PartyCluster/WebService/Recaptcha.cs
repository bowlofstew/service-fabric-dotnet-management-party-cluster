using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Fabric;
using System.Fabric.Description;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Domain;
using Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebService
{
    internal class Recaptcha : ICaptcha
    {
        private SecureString key;
        private Uri verifyUrl;

        public Recaptcha(ServiceInitializationParameters serviceParameters)
        {
            serviceParameters.CodePackageActivationContext.ConfigurationPackageModifiedEvent
                += this.CodePackageActivationContext_ConfigurationPackageModifiedEvent;

            ConfigurationPackage configPackage = serviceParameters.CodePackageActivationContext.GetConfigurationPackageObject("Config");

            this.UpdateConfigSettings(configPackage.Settings);
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

        public async Task<bool> VerifyAsync(string captchaResponse)
        {
            if (String.IsNullOrEmpty(captchaResponse))
            {
                throw new ArgumentNullException("captchaResponse");
            }

            HttpClient client = new HttpClient();

            Dictionary<string, string> parameters = new Dictionary<string, string>()
            {
                { "secret", this.key.ToUnsecureString() },
                { "response", captchaResponse }
            };

            FormUrlEncodedContent content = new FormUrlEncodedContent(parameters);
            HttpResponseMessage httpResponseMessage = await client.PostAsync(this.verifyUrl, content);
            
            JObject responseObject = JObject.Parse(await httpResponseMessage.Content.ReadAsStringAsync());
            return responseObject["success"].Value<bool>();
        }
    }
}
