// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;
    using System.Collections.ObjectModel;
    using System.Fabric;
    using System.Fabric.Description;
    using System.IO;
    using System.Net;
    using System.Net.Mail;
    using System.Threading.Tasks;
    using Common;
    using Domain;
    using SendGrid;

    internal class SendGridMailer : ISendMail
    {
        private NetworkCredential credentials;
        private string joinMailTemplate;
        private string mailAddress;
        private string mailFrom;
        private string mailSubject;

        public SendGridMailer(StatefulServiceParameters serviceParameters)
        {
            ConfigurationPackage configPackage = serviceParameters.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            DataPackage dataPackage = serviceParameters.CodePackageActivationContext.GetDataPackageObject("Data");

            this.UpdateSendMailSettings(configPackage.Settings);
            this.UpdateJoinMailTemplateContent(dataPackage.Path);

            serviceParameters.CodePackageActivationContext.ConfigurationPackageModifiedEvent +=
                this.CodePackageActivationContext_ConfigurationPackageModifiedEvent;

            serviceParameters.CodePackageActivationContext.DataPackageModifiedEvent
                += this.CodePackageActivationContext_DataPackageModifiedEvent;
        }

        public Task SendJoinMail(string receipientAddress, string clusterAddress, int userPort, TimeSpan clusterTimeRemaining, DateTimeOffset clusterExpiration)
        {
            string date = String.Format("on {0:MMMM dd} at {1:H:mm:ss UTC}", clusterExpiration, clusterExpiration);
            string time = String.Format("{0} hour{1}, ", clusterTimeRemaining.Hours, clusterTimeRemaining.Hours == 1 ? "" : "s")
                          + String.Format("{0} minute{1}, ", clusterTimeRemaining.Minutes, clusterTimeRemaining.Minutes == 1 ? "" : "s")
                          + String.Format("and {0} second{1}", clusterTimeRemaining.Seconds, clusterTimeRemaining.Seconds == 1 ? "" : "s");


            return this.SendMessageAsync(
                new MailAddress(this.mailAddress, this.mailFrom),
                receipientAddress,
                this.mailSubject,
                this.joinMailTemplate
                    .Replace("__clusterAddress__", clusterAddress)
                    .Replace("__userPort__", userPort.ToString())
                    .Replace("__clusterExpiration__", date)
                    .Replace("__clusterTimeRemaining__", time));
        }

        private Task SendMessageAsync(MailAddress from, string to, string subject, string htmlBody)
        {
            // Create an Web transport for sending email.
            Web transportWeb = new Web(this.credentials);

            SendGridMessage myMessage = new SendGridMessage();

            // Add the message properties.
            myMessage.From = from; // new MailAddress("partycluster@azure.com", "Service Fabric Party Cluster Team");
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
                sendGridParameters["Username"].DecryptValue().ToUnsecureString(),
                sendGridParameters["Password"].DecryptValue());

            this.mailAddress = sendGridParameters["MailAddress"].Value;
            this.mailFrom = sendGridParameters["MailFrom"].Value;
            this.mailSubject = sendGridParameters["MailSubject"].Value;
        }

        private void UpdateJoinMailTemplateContent(string templateDataPath)
        {
            using (StreamReader reader = new StreamReader(Path.Combine(templateDataPath, "joinmail.html")))
            {
                this.joinMailTemplate = reader.ReadToEnd();
            }
        }

        private void CodePackageActivationContext_DataPackageModifiedEvent(object sender, PackageModifiedEventArgs<DataPackage> e)
        {
            this.UpdateJoinMailTemplateContent(e.NewPackage.Path);
        }

        private void CodePackageActivationContext_ConfigurationPackageModifiedEvent(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            this.UpdateSendMailSettings(e.NewPackage.Settings);
        }
    }
}