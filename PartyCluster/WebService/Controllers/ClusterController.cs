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
            Resx messages = new Resx("WebService.Resources.Messages");

            try
            {
                if (user == null || String.IsNullOrWhiteSpace(user.UserEmail) || String.IsNullOrWhiteSpace(user.CaptchaResponse))
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel("MissingInput", messages.Manager.GetString("MissingInput"), "Missing input."));
                }

                // validate captcha.
                bool captchaValid = await this.captcha.VerifyAsync(user.CaptchaResponse);
                if (!captchaValid)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.Forbidden,
                        new BadRequestViewModel("InvalidCaptcha", messages.Manager.GetString("InvalidCaptcha"), "Invalid parameter: captcha"));
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
                        new BadRequestViewModel("InvalidEmail", messages.Manager.GetString("InvalidEmail"), argumentEx.Message));
                }

                JoinClusterFailedException joinFailedEx = ae.InnerException as JoinClusterFailedException;
                if (joinFailedEx != null)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel(joinFailedEx.Reason.ToString(), messages.Manager.GetString(joinFailedEx.Reason.ToString()), joinFailedEx.Message));
                }

                return this.Request.CreateResponse(
                    HttpStatusCode.InternalServerError,
                    new BadRequestViewModel("ServerError", messages.Manager.GetString("ServerError"), ae.InnerException.Message));
            }
            catch (Exception e)
            {
                return this.Request.CreateResponse(
                    HttpStatusCode.InternalServerError,
                    new BadRequestViewModel("ServerError", messages.Manager.GetString("ServerError"), e.Message));
            }
        }

        private string GetClusterName(int key)
        {
            Resx messages = new Resx("WebService.Resources.ClusterNames");

            return messages.Manager.GetString("Name" + (messages.Count % key));
        }

        private string GetUserCapacity(int count, int max)
        {
            Resx messages = new Resx("WebService.Resources.Messages");

            if (count == max)
            {
                return messages.Manager.GetString("CapacityFull");
            }

            double p = (double)count / (double)max;

            if (p < 0.3)
            {
                return messages.Manager.GetString("CapacityEmpty");
            }

            return messages.Manager.GetString("CapacityCrowded");
        }
    }
}