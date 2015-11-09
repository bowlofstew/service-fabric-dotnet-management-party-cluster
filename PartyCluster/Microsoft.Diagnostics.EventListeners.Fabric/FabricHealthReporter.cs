// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Fabric;
using System.Fabric.Health;

namespace Microsoft.Diagnostics.EventListeners.Fabric
{
    public class FabricHealthReporter : IHealthReporter
    {
        private FabricClient fabricClient;
        private Uri applicatioName;
        private string serviceManifestName;
        private string nodeName;
        private string entityIdentifier;
        private HealthState problemHealthState;

        public FabricHealthReporter(string entityIdentifier, HealthState problemHealthState = HealthState.Warning)
        {
            if (string.IsNullOrWhiteSpace(entityIdentifier))
            {
                throw new ArgumentException("entityIdentifier cannot be null or empty", "entityIdentifier");
            }
            this.entityIdentifier = entityIdentifier;

            this.problemHealthState = problemHealthState;

            this.fabricClient = new FabricClient(
                new FabricClientSettings()
                {
                    HealthReportSendInterval = TimeSpan.FromSeconds(5)
                }
            );

            var activationContext = FabricRuntime.GetActivationContext();
            this.applicatioName = new Uri(activationContext.ApplicationName);
            this.serviceManifestName = activationContext.GetServiceManifestName();
            var nodeContext = FabricRuntime.GetNodeContext();
            this.nodeName = nodeContext.NodeName;
        }
        public void ReportHealthy()
        {
            ReportHealth(HealthState.Ok, string.Empty);
        }

        public void ReportProblem(string problemDescription)
        {
            ReportHealth(HealthState.Warning, problemDescription);
        }

        private void ReportHealth(HealthState healthState, string problemDescription)
        {
            var healthInformation = new HealthInformation(this.entityIdentifier, "Connectivity", healthState);
            healthInformation.Description = problemDescription;

            var healthReport = new DeployedServicePackageHealthReport(
                this.applicatioName,
                this.serviceManifestName,
                this.nodeName,
                healthInformation);

            this.fabricClient.HealthManager.ReportHealth(healthReport);
        }
    }
}
