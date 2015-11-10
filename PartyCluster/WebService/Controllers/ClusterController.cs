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
    using System.Reflection;
    using System.Resources;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Domain;
    using global::WebService.ViewModels;
    using Microsoft.ServiceFabric.Services.Remoting.Client;

    [RoutePrefix("api")]
    public class ClusterController : ApiController
    {
        private static ResourceManager resources = new ResourceManager("WebService.Resources.Messages", Assembly.GetExecutingAssembly());

        private readonly ICaptcha captcha;

        public ClusterController(ICaptcha captcha)
        {
            this.captcha = captcha;
        }

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
        public async Task<HttpResponseMessage> Join(int clusterId, [FromBody] JoinClusterRequest user)
        {
            try
            {
                if (user == null || String.IsNullOrWhiteSpace(user.UserEmail) || String.IsNullOrWhiteSpace(user.CaptchaResponse))
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel("MissingInput", resources.GetString("MissingInput"), "Missing input."));
                }

                // validate captcha.
                bool captchaValid = await this.captcha.VerifyAsync(user.CaptchaResponse);
                if (!captchaValid)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.Forbidden,
                        new BadRequestViewModel("InvalidCaptcha", resources.GetString("InvalidCaptcha"), "Invalid parameter: captcha"));
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
                        new BadRequestViewModel("InvalidEmail", resources.GetString("InvalidEmail"), argumentEx.Message));
                }

                JoinClusterFailedException joinFailedEx = ae.InnerException as JoinClusterFailedException;
                if (joinFailedEx != null)
                {
                    return this.Request.CreateResponse(
                        HttpStatusCode.BadRequest,
                        new BadRequestViewModel(joinFailedEx.Reason.ToString(), resources.GetString(joinFailedEx.Reason.ToString()), joinFailedEx.Message));
                }

                return this.Request.CreateResponse(
                    HttpStatusCode.InternalServerError,
                    new BadRequestViewModel("ServerError", resources.GetString("ServerError"), ae.InnerException.Message));
            }
            catch (Exception e)
            {
                return this.Request.CreateResponse(
                    HttpStatusCode.InternalServerError,
                    new BadRequestViewModel("ServerError", resources.GetString("ServerError"), e.Message));
            }
        }
    }
}