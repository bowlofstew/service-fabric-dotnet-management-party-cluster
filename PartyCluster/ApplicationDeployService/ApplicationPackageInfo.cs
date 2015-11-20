// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ApplicationDeployService
{
    internal struct ApplicationPackageInfo
    {
        public ApplicationPackageInfo(
            string applicationTypeName,
            string applicationTypeVersion,
            string packageFileName,
            string entryServiceInstanceUri,
            string entryServiceEndpointName,
            string applicationDescription)
        {
            this.ApplicationTypeName = applicationTypeName;
            this.ApplicationTypeVersion = applicationTypeVersion;
            this.PackageFileName = packageFileName;
            this.ApplicationDescription = applicationDescription;
            this.EntryServiceInstanceUri = entryServiceInstanceUri;
            this.EntryServiceEndpointName = entryServiceEndpointName;
        }

        public string ApplicationTypeName { get; set; }

        public string ApplicationTypeVersion { get; set; }

        public string PackageFileName { get; set; }

        public string ApplicationDescription { get; set; }

        public string EntryServiceInstanceUri { get; set; }

        public string EntryServiceEndpointName { get; set; }
    }
}