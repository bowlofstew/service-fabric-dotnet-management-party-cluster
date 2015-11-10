using Microsoft.Practices.Unity;
using System.Web.Http;
using Unity.WebApi;
using WebService.Controllers;
using Domain;
using System.Fabric;

namespace WebService
{
    public static class UnityConfig
    {
        public static void RegisterComponents(HttpConfiguration config, ServiceInitializationParameters serviceParameters)
        {
            var container = new UnityContainer();

            container.RegisterType<ClusterController>(
                new TransientLifetimeManager(),
                new InjectionConstructor(new Recaptcha(serviceParameters)));
            
            config.DependencyResolver = new UnityDependencyResolver(container);
        }
    }
}