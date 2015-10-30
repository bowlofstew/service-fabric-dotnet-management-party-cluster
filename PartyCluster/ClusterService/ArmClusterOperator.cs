using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ClusterService
{
    using System.Configuration;
    using System.IO;
    using System.Net;
    using Domain;
    using Microsoft.Azure;
    using Microsoft.Azure.Management.Resources;
    using Microsoft.Azure.Management.Resources.Models;
    using Microsoft.IdentityModel.Clients.ActiveDirectory;

    internal class ArmClusterOperator : IClusterOperator
    {
   
        public Task<IEnumerable<int>> GetClusterPortsAsync(string domain)
        {
            //Hardcoding this with template values for now.
            return Task.FromResult<IEnumerable<int>>(new[] { 80, 8505, 8506, 8507, 8081, 8086 });
        }

        public static async Task<string> GetAuthorizationTokenAsync()
        {
            ClientCredential cc = new ClientCredential(ConfigurationManager.AppSettings["clientID"].ToString(), ConfigurationManager.AppSettings["clientSecret"].ToString());

            AuthenticationContext context = new AuthenticationContext(ConfigurationManager.AppSettings["authority"].ToString());
            AuthenticationResult result = await context.AcquireTokenAsync("https://management.azure.com/", cc);

            if (result == null)
            {
                throw new InvalidOperationException("Failed to obtain the JWT token");
            }

            string token = result.AccessToken;

            return token;
        }



        private async Task<string> CreateResourceGroupAsync(TokenCloudCredentials credential, string rgName)
        {
            ResourceGroup resourceGroup = new ResourceGroup { Location = "westus" };

            using (ResourceManagementClient resourceManagementClient = new ResourceManagementClient(credential))
            {
                ResourceGroupExistsResult exists = await resourceManagementClient.ResourceGroups.CheckExistenceAsync(rgName);

                if (exists.Exists)
                    return "Exists";


                ResourceGroupCreateOrUpdateResult rgResult = await resourceManagementClient.ResourceGroups.CreateOrUpdateAsync(rgName, resourceGroup);

                return rgResult.StatusCode.ToString();
            }
        }

        /// <summary>
        /// Initiates creation of a new cluster.
        /// </summary>
        /// <remarks>
        /// If a cluster with the given domain could not be created, an exception should be thrown indicating the failure reason.
        /// </remarks>
        /// <param name="name">A unique name for the cluster.</param>
        /// <returns>The FQDN of the new cluster.</returns>
        public async Task<string> CreateClusterAsync(string name)
        {
            string rgName = name;
            string parameterContent;
            string templateContent;
            string token = await GetAuthorizationTokenAsync();
            TokenCloudCredentials credential = new TokenCloudCredentials(ConfigurationManager.AppSettings["subscriptionID"].ToString(), token);

            string rgStatus = await this.CreateResourceGroupAsync(credential, rgName);

            if (rgStatus == "Exists")
            {
                throw new System.InvalidOperationException("ResourceGroup/Cluster already exists. Please try passing a different name, or delete the ResourceGroup/Cluster first.");
            }

            this.GetTemplates(rgName, out templateContent, out parameterContent);
            await this.CreateTemplateDeploymentAsync(credential, name, templateContent, parameterContent);

            return (rgName + ".westus.cloudapp.azure.com");
        }

       public async Task DeleteClusterAsync(string name)
       {
            string rgName = name;

            string token = await GetAuthorizationTokenAsync();
            TokenCloudCredentials credential = new TokenCloudCredentials(ConfigurationManager.AppSettings["subscriptionID"].ToString(), token);

            using (ResourceManagementClient resourceGroupClient = new ResourceManagementClient(credential))
            {
                AzureOperationResponse deleteResult = await resourceGroupClient.ResourceGroups.BeginDeletingAsync(rgName);
            }
        }

        
        public async Task<ClusterOperationStatus> GetClusterStatusAsync(string name)
        {
            string token = await GetAuthorizationTokenAsync();
            TokenCloudCredentials credential = new TokenCloudCredentials(ConfigurationManager.AppSettings["subscriptionID"].ToString(), token);

            DeploymentGetResult dpResult;
            ResourceGroupGetResult rgResult;

            using (ResourceManagementClient templateDeploymentClient = new ResourceManagementClient(credential))
            {
                DeploymentExistsResult exists = templateDeploymentClient.Deployments.CheckExistence(name, name + "dp");
                if (!exists.Exists)
                {
                    // This might also imply that the cluster never existed in the first place.
                    return ClusterOperationStatus.ClusterNotFound;
                }


                dpResult = templateDeploymentClient.Deployments.Get(name, name + "dp");
                rgResult = templateDeploymentClient.ResourceGroups.Get(name);
            }

            //Either the resource group might exists, but resources are being added or deleted via the template, or the RG itself might be getting created or deleted.
            //This means we have to seaparate out the provisioning states of both the RG along with teh template deployment to get a cluster status. 
            //string result = dpResult.Deployment.Properties.ProvisioningState + rgResult.ResourceGroup.ProvisioningState;
            //result = result.Replace("Succeeded", "");
            if (rgResult.ResourceGroup.ProvisioningState.Contains("Failed"))
                return ClusterOperationStatus.DeleteFailed;

            if (rgResult.ResourceGroup.ProvisioningState.Contains("Deleting"))
                return ClusterOperationStatus.Deleting;

            if (dpResult.Deployment.Properties.ProvisioningState.Contains("Accepted") || dpResult.Deployment.Properties.ProvisioningState.Contains("Running"))
                return ClusterOperationStatus.Creating;

            if (dpResult.Deployment.Properties.ProvisioningState.Contains("Failed"))
                return ClusterOperationStatus.CreateFailed;

            if (dpResult.Deployment.Properties.ProvisioningState.Contains("Succeeded"))
                return ClusterOperationStatus.Ready;


            return ClusterOperationStatus.Unknown;
        }
 

        private void GetTemplates(string name, out string templateContent, out string parameterContent)
        {
            string rgName = name;

            string username = ConfigurationManager.AppSettings["username"].ToString();
            string adminpwd = ConfigurationManager.AppSettings["adminpwd"].ToString();//This needs to have three types of character groups. PRovisioning will fail otherwise.

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ConfigurationManager.AppSettings["templateLoc"].ToString());
            
            using (HttpWebResponse resp = (HttpWebResponse) req.GetResponse())
            {
                StreamReader sr = new StreamReader(resp.GetResponseStream());
                templateContent = sr.ReadToEnd();
                sr.Close();
            }

            HttpWebRequest req2 = (HttpWebRequest)WebRequest.Create(ConfigurationManager.AppSettings["parametersLoc"].ToString());


            using (HttpWebResponse resp2 = (HttpWebResponse) req2.GetResponse())
            {
                StreamReader sr2 = new StreamReader(resp2.GetResponseStream());
                parameterContent = sr2.ReadToEnd();
                sr2.Close();
            }

            parameterContent = parameterContent.Replace("_CLUSTER_NAME_", name);
            parameterContent = parameterContent.Replace("_USER_", username);
            parameterContent = parameterContent.Replace("_PWD_", adminpwd);
        }

        private async Task CreateTemplateDeploymentAsync(TokenCloudCredentials credential, string rgName, string templateContent, string parameterContent)
        {
            Deployment deployment = new Deployment();

            string deploymentname = rgName + "dp";
            deployment.Properties = new DeploymentProperties
            {
                Mode = DeploymentMode.Incremental,
                Template = templateContent,
                Parameters = parameterContent,
            };

            using (ResourceManagementClient templateDeploymentClient = new ResourceManagementClient(credential))
            {
                try
                {
                    DeploymentOperationsCreateResult dpResult = await templateDeploymentClient.Deployments.CreateOrUpdateAsync(rgName, deploymentname, deployment);
                    ServiceEventSource.Current.Message("ArmClusterOperator: Deployment in RG {0}: {1} ({2})", rgName, dpResult.RequestId, dpResult.StatusCode);
                }
                catch (Exception e)
                {
                    ServiceEventSource.Current.Message("ArmClusterOperator: Failed deploying ARM template to create a cluster in RG {0}. {1}", rgName, e.Message);

                    throw;
                }
            }
        }
    }
}
