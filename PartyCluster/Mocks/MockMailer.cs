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
        public Task SendJoinMail(string receipientAddress, string clusterAddress, int userPort, TimeSpan timeRemaining, DateTimeOffset clusterExpiration)
        {
            return Task.FromResult(true);
        }
        
    }
}
