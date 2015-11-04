// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Domain;
    using global::WebService.ViewModels;
    using Microsoft.ServiceFabric.Services.Remoting.Client;

    [RoutePrefix("api")]
    public class ClusterController : ApiController
    {
        [HttpGet]
        [Route("clusters")]
        public Task<IEnumerable<ClusterView>> Get()
        {
            ServiceUriBuilder builder = new ServiceUriBuilder("ClusterService");
            IClusterService clusterService = ServiceProxy.Create<IClusterService>(1, builder.ToUri());

            return clusterService.GetClusterListAsync();
        }

        [HttpPost]
        [Route("clusters/join/{clusterId}")]
        public async Task<HttpResponseMessage> Join(int clusterId, [FromBody] UserView user)
        {
            if (user == null || String.IsNullOrWhiteSpace(user.UserEmail))
            {
                return this.Request.CreateResponse(
                    HttpStatusCode.BadRequest,
                    new BadRequestViewModel("InvalidArguments", "Please provide an email address."));
            }

            ServiceUriBuilder builder = new ServiceUriBuilder("ClusterService");
            IClusterService clusterService = ServiceProxy.Create<IClusterService>(1, builder.ToUri());

            try
            {
                await clusterService.JoinClusterAsync(clusterId, user);

                return this.Request.CreateResponse(HttpStatusCode.Accepted);
            }
            catch (AggregateException ae)
            {
                ArgumentException argumentEx = ae.InnerException as ArgumentException;
                if (argumentEx != null)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel("InvalidArguments", argumentEx.Message));
                }

                JoinClusterFailedException joinFailedEx = ae.InnerException as JoinClusterFailedException;
                if (joinFailedEx != null)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel(joinFailedEx.Reason.ToString(), joinFailedEx.Message));
                }

                return this.Request.CreateResponse(
                    HttpStatusCode.InternalServerError,
                    new BadRequestViewModel("ServerError", ae.InnerException.Message));
            }
            catch (Exception e)
            {
                return this.Request.CreateResponse(
                    HttpStatusCode.InternalServerError,
                    new BadRequestViewModel("ServerError", e.Message));
            }
        }
    }
}