// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ApplicationDeployService
{
    using Domain;

    public struct ApplicationDeployment
    {
        public ApplicationDeployment(
            string cluster,
            ApplicationDeployStatus status,
            string imageStorePath,
            string applicationType,
            string applicationVersion,
            string dataPackagePath)
        {
            this.Cluster = cluster;
            this.Status = status;
            this.ImageStorePath = imageStorePath;
            this.ApplicationType = applicationType;
            this.ApplicationVersion = applicationVersion;
            this.DataPackagePath = dataPackagePath;
        }
            
        public string Cluster { get; set; }
        
        public ApplicationDeployStatus Status { get; set; }
        
        public string ImageStorePath { get; set; }

        public string ApplicationType { get; private set; }

        public string ApplicationVersion { get; private set; }

        public string DataPackagePath { get; private set; }
    }
}