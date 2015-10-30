using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public interface ISendMail
    {
        Task SendMessageAsync(MailAddress from, string to, string subject, string htmlBody);
    }
}
