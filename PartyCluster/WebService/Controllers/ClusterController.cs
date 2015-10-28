using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Domain;
using Microsoft.ServiceFabric.Services;

namespace WebService.Controllers
{
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
        [Route("clusters/join")]
        public Task JoinRandom([FromBody]string user)
        {
            throw new NotImplementedException();
        }

        [HttpPost]
        [Route("clusters/join/{clusterId}")]
        public async Task<IHttpActionResult> Join(int clusterId, [FromBody]UserView user)
        {
            ServiceUriBuilder builder = new ServiceUriBuilder("ClusterService");
            IClusterService clusterService = ServiceProxy.Create<IClusterService>(1, builder.ToUri());

            try
            {
                await clusterService.JoinClusterAsync(clusterId, user);

                return this.Ok();
            }
            catch (AggregateException ae)
            {
                if (ae.InnerException is ArgumentException)
                {
                    return this.BadRequest();
                }

                return this.InternalServerError(ae.InnerException);
            }
            catch (Exception e)
            {
                return this.InternalServerError(e);
            }
        }
    }
}
