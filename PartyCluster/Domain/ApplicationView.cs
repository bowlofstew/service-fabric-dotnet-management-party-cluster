using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    [DataContract]
    public class ApplicationView
    {
        
        public ApplicationView(HyperlinkView link)
        {
            this.EntryServiceInfo = link;
        }
        
        [DataMember]
        public HyperlinkView EntryServiceInfo { get; private set; }
    }
}
