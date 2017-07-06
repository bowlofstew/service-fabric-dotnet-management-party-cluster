// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService
{
    using System;
    using Common;

    public class ClusterConfig
    {
        public ClusterConfig()
        {
            Trace.Message("ClusterConfig ctor.");

            this.RefreshInterval = TimeSpan.FromSeconds(1);
            this.MinimumClusterCount = 1;
            this.MaximumClusterCount = 2;
            this.MaximumUsersPerCluster = 1;
            this.MaximumClusterUptime = TimeSpan.FromHours(1);
            this.UserCapacityHighPercentThreshold = 0.75;
            this.UserCapacityLowPercentThreshold = 0.25;
        }

        public TimeSpan RefreshInterval { get; set; }

        public int MinimumClusterCount { get; set; }

        public int MaximumClusterCount { get; set; }

        public int MaximumUsersPerCluster { get; set; }

        public TimeSpan MaximumClusterUptime { get; set; }

        public double UserCapacityHighPercentThreshold { get; set; }

        public double UserCapacityLowPercentThreshold { get; set; }

        public double CapacityThresholdIncrement { get; set; }

        public string ArmTemplateFile { get; set; }

        public string ArmTemplateParameterFile { get; set; }
    }
}