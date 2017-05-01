// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using PartyCluster.Domain;
    using PartyCluster.WebService.Models;
    using PartyCluster.WebService.Resources;
    using PartyCluster.WebService.ViewModels;
    using Microsoft.AspNetCore.Mvc;

    [Route("api")]
    public class ClusterController : Controller
    {
        private static Resx messageResources = new Resx("PartyCluster.WebService.Resources.Messages");
        private static Resx clusterNameResources = new Resx("PartyCluster.WebService.Resources.ClusterNames");
        
        [HttpGet]
        [Route("clusters")]
        public async Task<IActionResult> Get()
        {
            ServiceUriBuilder builder = new ServiceUriBuilder("ClusterService");
            IClusterService clusterService = ServiceProxy.Create<IClusterService>(builder.ToUri(), new ServicePartitionKey(1));

            IEnumerable<ClusterView> clusters = await clusterService.GetClusterListAsync();

            return this.Ok(
                clusters.Select(
                    x => new
                    {
                        ClusterId = x.ClusterId,
                        Name = this.GetClusterName(x.ClusterId),
                        ApplicationCount = x.ApplicationCount,
                        ServiceCount = x.ServiceCount,
                        Capacity = this.GetUserCapacity(x.UserCount, x.MaxUsers),
                        UserCount = x.UserCount,
                        MaxUsers = x.MaxUsers,
                        TimeRemaining = x.TimeRemaining > TimeSpan.Zero
                            ? String.Format("{0:hh\\:mm\\:ss}", x.TimeRemaining)
                            : "expired"
                    }));
        }

        [HttpPost]
        [Route("clusters/join/{clusterId}")]
        public async Task<IActionResult> Join(int clusterId, [FromBody] JoinClusterRequest user)
        {
            try
            {
                if (user == null || String.IsNullOrWhiteSpace(user.UserEmail))
                {

                    return new JsonResult(new BadRequestViewModel("MissingInput", messageResources.Manager.GetString("MissingInput"), "Missing input."))
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest
                    };
                }
                
                ServiceUriBuilder builder = new ServiceUriBuilder("ClusterService");
                IClusterService clusterService = ServiceProxy.Create<IClusterService>(builder.ToUri(), new ServicePartitionKey(1));

                var userView = await clusterService.JoinClusterAsync(clusterId, user.UserEmail);

                return new JsonResult(userView)
                {
                    StatusCode = (int)HttpStatusCode.Accepted
                };
            }
            catch (AggregateException ae)
            {
                ArgumentException argumentEx = ae.InnerException as ArgumentException;
                if (argumentEx != null)
                {
                    return new JsonResult(new BadRequestViewModel("InvalidEmail", messageResources.Manager.GetString("InvalidEmail"), argumentEx.Message))
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest,
                    };
                }

                OperationFailedException joinFailedEx = ae.InnerException as OperationFailedException;
                if (joinFailedEx != null)
                {
                    return new JsonResult(
                        new BadRequestViewModel(
                            joinFailedEx.Reason.ToString(),
                            messageResources.Manager.GetString(joinFailedEx.Reason.ToString()),
                            joinFailedEx.Message))
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest
                    };
                }

                return new JsonResult(
                    new BadRequestViewModel("ServerError", messageResources.Manager.GetString("ServerError"), ae.InnerException.Message))
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
            catch (Exception e)
            {
                return new JsonResult(
                    new BadRequestViewModel("ServerError", messageResources.Manager.GetString("ServerError"), e.Message))
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
        }

        [HttpPost]
        [Route("clusters/joinRandom/{userId?}")]
        public async Task<IActionResult> JoinRandom(string userId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return new JsonResult(
                        new BadRequestViewModel("MissingInput", messageResources.Manager.GetString("MissingInput"), "Missing input."))
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest
                    };
                }

                ServiceUriBuilder builder = new ServiceUriBuilder("ClusterService");
                IClusterService clusterService = ServiceProxy.Create<IClusterService>(builder.ToUri(), new ServicePartitionKey(1));

                var userView = await clusterService.JoinRandomClusterAsync(userId);

                return new JsonResult(userView)
                {
                    StatusCode = (int)HttpStatusCode.Accepted
                };
            }
            catch (AggregateException ae)
            {
                ArgumentException argumentEx = ae.InnerException as ArgumentException;
                if (argumentEx != null)
                {
                    return new JsonResult(
                        new BadRequestViewModel("InvalidEmail", messageResources.Manager.GetString("InvalidEmail"), argumentEx.Message))
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest
                    };
                }

                OperationFailedException joinFailedEx = ae.InnerException as OperationFailedException;
                if (joinFailedEx != null)
                {
                    return new JsonResult(
                        new BadRequestViewModel(
                            joinFailedEx.Reason.ToString(),
                            messageResources.Manager.GetString(joinFailedEx.Reason.ToString()),
                            joinFailedEx.Message))
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest
                    };

                }

                return new JsonResult(
                    new BadRequestViewModel("ServerError", messageResources.Manager.GetString("ServerError"), ae.InnerException.Message))
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
            catch (Exception e)
            {
                return new JsonResult(
                    new BadRequestViewModel("ServerError", messageResources.Manager.GetString("ServerError"), e.Message))
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
        }

        [HttpPost]
        [Route("clusters/partyStatus/{userId?}")]
        public async Task<IActionResult> GetPartyStatus(string userId = "")
        {
            try
            {
                ServiceUriBuilder builder = new ServiceUriBuilder("ClusterService");
                IClusterService clusterService = ServiceProxy.Create<IClusterService>(builder.ToUri(), new ServicePartitionKey(1));

                var userView = await clusterService.GetPartyStatusAsync(userId);

                return new JsonResult(userView)
                {
                    StatusCode = (int)HttpStatusCode.Accepted
                };
            }
            catch (AggregateException ae)
            {
                ArgumentException argumentEx = ae.InnerException as ArgumentException;
                if (argumentEx != null)
                {
                    return new JsonResult(
                        new BadRequestViewModel("InvalidEmail", messageResources.Manager.GetString("InvalidEmail"), argumentEx.Message))
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest
                    };

                }

                OperationFailedException joinFailedEx = ae.InnerException as OperationFailedException;
                if (joinFailedEx != null)
                {
                    return new JsonResult(
                        new BadRequestViewModel(
                            joinFailedEx.Reason.ToString(),
                            messageResources.Manager.GetString(joinFailedEx.Reason.ToString()),
                            joinFailedEx.Message))
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest
                    };
                }

                return new JsonResult(
                    new BadRequestViewModel("ServerError", messageResources.Manager.GetString("ServerError"), ae.InnerException.Message))
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
            catch (Exception e)
            {
                return new JsonResult(
                    new BadRequestViewModel("ServerError", messageResources.Manager.GetString("ServerError"), e.Message))
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
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