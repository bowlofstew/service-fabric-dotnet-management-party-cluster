// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService.Controllers
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Security.Claims;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using PartyCluster.Domain;
    using PartyCluster.WebService.Resources;
    using PartyCluster.WebService.ViewModels;

    [RoutePrefix("api")]
    public class ClusterController : ApiController
    {
        private static Resx messageResources = new Resx("PartyCluster.WebService.Resources.Messages");
        private static Resx clusterNameResources = new Resx("PartyCluster.WebService.Resources.ClusterNames");
        
        [HttpPost]
        [Route("clusters/{platform}/joinRandom")]
        [Authorize]
        public async Task<HttpResponseMessage> JoinRandom(Platform platform)
        {
            try
            {
                var userId = this.ExtractUserIdClaim();

                if (string.IsNullOrWhiteSpace(userId))
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.Unauthorized,
                        new BadRequestViewModel("Unauthorized", messageResources.Manager.GetString("Unauthorized"), "Unauthorized."));
                }

                ServiceUriBuilder builder = new ServiceUriBuilder("ClusterService");
                IClusterService clusterService = ServiceProxy.Create<IClusterService>(builder.ToUri(), new ServicePartitionKey(1));

                var userView = await clusterService.JoinRandomClusterAsync(userId, platform);

                return this.Request.CreateResponse<UserView>(HttpStatusCode.OK, userView);
            }
            catch (AggregateException ae)
            {
                ArgumentException argumentEx = ae.InnerException as ArgumentException;
                if (argumentEx != null)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel("InvalidEmail", messageResources.Manager.GetString("InvalidEmail"), argumentEx.Message));
                }

                OperationFailedException joinFailedEx = ae.InnerException as OperationFailedException;
                if (joinFailedEx != null)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel(
                            joinFailedEx.Reason.ToString(),
                            messageResources.Manager.GetString(joinFailedEx.Reason.ToString()),
                            joinFailedEx.Message));
                }

                return this.Request.CreateResponse(
                    HttpStatusCode.InternalServerError,
                    new BadRequestViewModel("ServerError", messageResources.Manager.GetString("ServerError"), ae.InnerException.Message));
            }
            catch (Exception e)
            {
                return this.Request.CreateResponse(
                    HttpStatusCode.InternalServerError,
                    new BadRequestViewModel("ServerError", messageResources.Manager.GetString("ServerError"), e.Message));
            }
        }

        [HttpPost]
        [Route("clusters/partyStatus")]
        [Authorize]
        public async Task<HttpResponseMessage> GetPartyStatus()
        {
            try
            {
                var userId = this.ExtractUserIdClaim();

                if (string.IsNullOrEmpty(userId))
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.Unauthorized,
                        new BadRequestViewModel("Unauthorized", messageResources.Manager.GetString("Unauthorized"), "Unauthorized."));
                }

                ServiceUriBuilder builder = new ServiceUriBuilder("ClusterService");
                IClusterService clusterService = ServiceProxy.Create<IClusterService>(builder.ToUri(), new ServicePartitionKey(1));

                var userView = await clusterService.GetPartyStatusAsync(userId);

                return this.Request.CreateResponse<UserView>(HttpStatusCode.OK, userView);
            }
            catch (AggregateException ae)
            {
                ArgumentException argumentEx = ae.InnerException as ArgumentException;
                if (argumentEx != null)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel("InvalidEmail", messageResources.Manager.GetString("InvalidEmail"), argumentEx.Message));
                }

                OperationFailedException joinFailedEx = ae.InnerException as OperationFailedException;
                if (joinFailedEx != null)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel(
                            joinFailedEx.Reason.ToString(),
                            messageResources.Manager.GetString(joinFailedEx.Reason.ToString()),
                            joinFailedEx.Message));
                }

                return this.Request.CreateResponse(
                    HttpStatusCode.InternalServerError,
                    new BadRequestViewModel("ServerError", messageResources.Manager.GetString("ServerError"), ae.InnerException.Message));
            }
            catch (Exception e)
            {
                return this.Request.CreateResponse(
                    HttpStatusCode.InternalServerError,
                    new BadRequestViewModel("ServerError", messageResources.Manager.GetString("ServerError"), e.Message));
            }
        }

        private string ExtractUserIdClaim()
        {
            var userId = string.Empty;
            var principal = this.User as ClaimsPrincipal;
            if (principal != null)
            {
                var identity = principal.Identity as ClaimsIdentity;
                if (identity != null)
                {
                    foreach (var claim in identity.Claims)
                    {
                        if (claim.Type == ClaimTypes.Name)
                        {
                            userId = claim.Value;
                            break;
                        }
                        else if (claim.Type == ClaimTypes.Email)
                        {
                            userId = claim.Value;
                            break;
                        }
                    }
                }
            }

            return userId;
        }

        private string GetClusterName(int key)
        {
            return clusterNameResources.Manager.GetString("Name" + (key%clusterNameResources.Count));
        }

        private string GetUserCapacity(int count, int max)
        {
            if (count == max)
            {
                return messageResources.Manager.GetString("CapacityFull");
            }

            double p = (double) count/(double) max;

            if (p < 0.3)
            {
                return messageResources.Manager.GetString("CapacityEmpty");
            }

            return messageResources.Manager.GetString("CapacityCrowded");
        }
    }
}