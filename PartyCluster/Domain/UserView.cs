using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    [DataContract]
    public class UserView
    {
        public UserView(string userName, string userEmail)
        {
            this.UserEmail = userEmail;
            this.UserName = userName;
        }

        [DataMember]
        public string UserName { get; private set; }

        [DataMember]
        public string UserEmail { get; private set; }
    }
}
