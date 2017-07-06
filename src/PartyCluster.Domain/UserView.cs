// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.Domain
{
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    public struct UserView
    {
        public UserView(PartyStatus windowsStatus, PartyStatus linuxStatus, string userId)
            : this (windowsStatus, linuxStatus, -1, userId, string.Empty, -1, TimeSpan.Zero, DateTimeOffset.MaxValue)
        {
        }

        public UserView(PartyStatus windowsStatus, PartyStatus linuxStatus, int clusterId, string userId, string endpoint, int userPort, TimeSpan timeRemaining, DateTimeOffset expirationTime)
        {
            this.WindowsStatus = windowsStatus.ToString();
            this.LinuxStatus = linuxStatus.ToString();
            this.ClusterId = clusterId;
            this.UserId = userId;
            this.ConnectionEndpoint = endpoint;
            this.UserPort = userPort;

            this.ExpirationTime = String.Format("{0:MMMM dd} at {1:H:mm:ss UTC}", expirationTime, expirationTime);
            this.TimeRemaining = String.Format("{0} hour{1}, ", timeRemaining.Hours, timeRemaining.Hours == 1 ? "" : "s")
                          + String.Format("{0} minute{1}, ", timeRemaining.Minutes, timeRemaining.Minutes == 1 ? "" : "s")
                          + String.Format("and {0} second{1}", timeRemaining.Seconds, timeRemaining.Seconds == 1 ? "" : "s");
        }

        [DataMember]
        public string WindowsStatus { get; private set; }

        [DataMember]
        public string LinuxStatus { get; private set; }

        [DataMember]
        public int ClusterId { get; private set; }

        [DataMember]
        public string UserId { get; private set; }

        [DataMember]
        public string ConnectionEndpoint { get; private set; }

        [DataMember]
        public int UserPort { get; private set; }

        [DataMember]
        public string TimeRemaining { get; private set; }

        [DataMember]
        public string ExpirationTime { get; private set; }
    }
}
