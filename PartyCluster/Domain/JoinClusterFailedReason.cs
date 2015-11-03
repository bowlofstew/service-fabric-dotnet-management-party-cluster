using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public enum JoinClusterFailedReason : byte
    {
        ClusterFull,

        ClusterExpired,

        ClusterNotReady,

        ClusterDoesNotExist,

        UserAlreadyJoined,

        NoPortsAvailable,

        InvalidEmail
    }
}
