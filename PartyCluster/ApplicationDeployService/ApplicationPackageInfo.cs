// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ApplicationDeployService
{
    internal struct ApplicationPackageInfo
    {
        public ApplicationPackageInfo(
            string applicationType,
            string applicationVersion,
            string dataPackagePath)
        {
            this.ApplicationType = applicationType;
            this.ApplicationVersion = applicationVersion;
            this.DataPackageDirectoryName = dataPackagePath;
        }

        public string ApplicationType { get; private set; }

        public string ApplicationVersion { get; private set; }

        public string DataPackageDirectoryName { get; private set; }
    }
}