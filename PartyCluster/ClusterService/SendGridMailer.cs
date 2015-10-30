using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Domain;

namespace ClusterService
{
    internal class SendGridMailer : ISendMail
    {
        private readonly NetworkCredential credentials;

        public SendGridMailer(string username, string password)
        {
            this.credentials = new NetworkCredential(username, password);
        }

        public Task SendMessageAsync(MailAddress from, string to, string subject, string htmlBody)
        {
            throw new NotImplementedException();
        }
    }
}
