using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    [DataContract]
    public class JoinClusterFailedException : Exception
    {
        [DataMember]
        public JoinClusterFailedReason Reason { get; private set; }

        public JoinClusterFailedException(JoinClusterFailedReason reason)
            :base(reason.ToString())
        {
            this.Reason = reason;
        }

        public JoinClusterFailedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }
    }

    public enum JoinClusterFailedReason
    {
        ClusterFull,

        ClusterExpired,

        ClusterNotReady,

        UserAlreadyJoined,

        NoPortsAvailable

    }
}
