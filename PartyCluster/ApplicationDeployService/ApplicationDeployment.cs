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
            string applicationTypeName,
            string applicationTypeVersion,
            string applicationInstanceName,
            string packagePath)
        {
            this.Cluster = cluster;
            this.Status = status;
            this.ImageStorePath = imageStorePath;
            this.ApplicationTypeName = applicationTypeName;
            this.ApplicationTypeVersion = applicationTypeVersion;
            this.ApplicationInstanceName = applicationInstanceName;
            this.PackagePath = packagePath;
        }

        public ApplicationDeployment(ApplicationDeployStatus status, ApplicationDeployment copyFrom)
            : this(
                  copyFrom.Cluster,
                  status,
                  copyFrom.ImageStorePath,
                  copyFrom.ApplicationTypeName,
                  copyFrom.ApplicationTypeVersion,
                  copyFrom.ApplicationInstanceName,
                  copyFrom.PackagePath)
        {
        }
            
        public string Cluster { get; set; }
        
        public ApplicationDeployStatus Status { get; set; }
        
        public string ImageStorePath { get; set; }

        public string ApplicationTypeName { get; private set; }

        public string ApplicationTypeVersion { get; private set; }

        public string ApplicationInstanceName { get; private set; }

        public string PackagePath { get; private set; }
    }
}