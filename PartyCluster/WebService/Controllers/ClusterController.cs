// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Domain;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Resources;
    using ViewModels;

    [RoutePrefix("api")]
    public class ClusterController : ApiController
    {
        private static Resx messageResources = new Resx("WebService.Resources.Messages");

        private static Resx clusterNameResources = new Resx("WebService.Resources.ClusterNames");

        private readonly ICaptcha captcha;

        public ClusterController(ICaptcha captcha)
        {
            this.captcha = captcha;
        }

        [HttpGet]
        [Route("clusters")]
        public async Task<IHttpActionResult> Get()
        {
            ServiceUriBuilder builder = new ServiceUriBuilder("ClusterService");
            IClusterService clusterService = ServiceProxy.Create<IClusterService>(1, builder.ToUri());

            IEnumerable<ClusterView> clusters = await clusterService.GetClusterListAsync();

            return Ok(clusters.Select(x => new
            {
                ClusterId = x.ClusterId,
                Name = GetClusterName(x.ClusterId),
                ApplicationCount = x.ApplicationCount,
                ServiceCount = x.ServiceCount,
                Capacity = GetUserCapacity(x.UserCount, x.MaxUsers),
                UserCount = x.UserCount,
                MaxUsers = x.MaxUsers,
                TimeRemaining = x.TimeRemaining > TimeSpan.Zero
                    ? String.Format("{0:hh\\:mm\\:ss}", x.TimeRemaining)
                    : "expired"
            }));
        }

        [HttpPost]
        [Route("clusters/join/{clusterId}")]
        public async Task<HttpResponseMessage> Join(int clusterId, [FromBody] JoinClusterRequest user)
        {
            try
            {
                if (user == null || String.IsNullOrWhiteSpace(user.UserEmail) || String.IsNullOrWhiteSpace(user.CaptchaResponse))
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel("MissingInput", messageResources.Manager.GetString("MissingInput"), "Missing input."));
                }

                // validate captcha.
                bool captchaValid = await this.captcha.VerifyAsync(user.CaptchaResponse);
                if (!captchaValid)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.Forbidden,
                        new BadRequestViewModel("InvalidCaptcha", messageResources.Manager.GetString("InvalidCaptcha"), "Invalid parameter: captcha"));
                }

                ServiceUriBuilder builder = new ServiceUriBuilder("ClusterService");
                IClusterService clusterService = ServiceProxy.Create<IClusterService>(1, builder.ToUri());

                await clusterService.JoinClusterAsync(clusterId, user.UserEmail);

                return this.Request.CreateResponse(HttpStatusCode.Accepted);
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

                JoinClusterFailedException joinFailedEx = ae.InnerException as JoinClusterFailedException;
                if (joinFailedEx != null)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel(joinFailedEx.Reason.ToString(), messageResources.Manager.GetString(joinFailedEx.Reason.ToString()), joinFailedEx.Message));
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

        private string GetClusterName(int key)
        {

            return clusterNameResources.Manager.GetString("Name" + (key % clusterNameResources.Count));
        }

        private string GetUserCapacity(int count, int max)
        {

            if (count == max)
            {
                return messageResources.Manager.GetString("CapacityFull");
            }

            double p = (double)count / (double)max;

            if (p < 0.3)
            {
                return messageResources.Manager.GetString("CapacityEmpty");
            }

            return messageResources.Manager.GetString("CapacityCrowded");
        }
    }
}