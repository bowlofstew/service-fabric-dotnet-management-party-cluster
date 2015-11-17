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
            string packageFileName)
        {
            this.ApplicationTypeName = applicationTypeName;
            this.ApplicationTypeVersion = applicationTypeVersion;
            this.PackageFileName = packageFileName;
        }

        public string ApplicationTypeName { get; set; }

        public string ApplicationTypeVersion { get; set; }

        public string PackageFileName { get; set; }
    }
}