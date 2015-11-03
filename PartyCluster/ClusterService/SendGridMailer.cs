using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Fabric;
using System.Fabric.Description;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Domain;
using SendGrid;

namespace ClusterService
{
    internal class SendGridMailer : ISendMail
    {
        private NetworkCredential credentials;

        public SendGridMailer(ServiceInitializationParameters serviceParameters)
        {
            ConfigurationPackage configPackage = serviceParameters.CodePackageActivationContext.GetConfigurationPackageObject("Config");

            this.UpdateSendMailSettings(configPackage.Settings);

            serviceParameters.CodePackageActivationContext.ConfigurationPackageModifiedEvent += CodePackageActivationContext_ConfigurationPackageModifiedEvent;
        }

        public Task SendMessageAsync(MailAddress from, string to, string subject, string htmlBody)
        {
            // Create an Web transport for sending email.
            Web transportWeb = new Web(credentials);

            SendGridMessage myMessage = new SendGridMessage();

            // Add the message properties.
            myMessage.From = from;// new MailAddress("partycluster@azure.com", "Service Fabric Party Cluster Team");
            myMessage.AddTo(to);

            myMessage.Subject = subject;

            //Add the HTML and Text bodies
            myMessage.Html = htmlBody;

            return transportWeb.DeliverAsync(myMessage);
        }

        private void UpdateSendMailSettings(ConfigurationSettings settings)
        {
            KeyedCollection<string, ConfigurationProperty> sendGridParameters = settings.Sections["SendGridSettings"].Parameters;

            this.credentials = new NetworkCredential(
                sendGridParameters["Username"].Value,
                sendGridParameters["Password"].Value);
        }

        private void CodePackageActivationContext_ConfigurationPackageModifiedEvent(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            this.UpdateSendMailSettings(e.NewPackage.Settings);
        }
    }
}
