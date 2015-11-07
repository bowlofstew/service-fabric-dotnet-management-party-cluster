using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Domain;

namespace Mocks
{
    public class MockMailer : ISendMail
    {
        public Task SendMessageAsync(MailAddress from, string to, string subject, string htmlBody)
        {
            return Task.FromResult(true);
        }
    }
}
