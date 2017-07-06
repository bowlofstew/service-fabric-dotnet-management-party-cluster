// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Linq;
    using System.Security.Claims;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Common;
    using Microsoft.Owin;
    using Microsoft.Owin.FileSystems;
    using Microsoft.Owin.Security.Facebook;
    using Microsoft.Owin.StaticFiles;
    using Middleware;
    using Owin;
    using Owin.Security.Providers.GitHub;

    internal class Startup : IOwinAppBuilder
    {
        private readonly StatelessServiceContext serviceContext;
        private readonly ConfigSettings config;

        public Startup(StatelessServiceContext serviceContext)
        {
            this.serviceContext = serviceContext;

            var configPkg = this.serviceContext.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            var parameters = configPkg.Settings.Sections["AuthProviderConfiguration"].Parameters;
            this.config = new ConfigSettings(parameters);
        }

        public void Configuration(IAppBuilder appBuilder)
        {
            HttpConfiguration httpConfig = new HttpConfiguration();

            FormatterConfig.ConfigureFormatters(httpConfig.Formatters);

            UnityConfig.RegisterComponents();

            PhysicalFileSystem physicalFileSystem = new PhysicalFileSystem(@".\wwwroot");
            FileServerOptions fileOptions = new FileServerOptions();

            fileOptions.EnableDefaultFiles = true;
            fileOptions.RequestPath = PathString.Empty;
            fileOptions.FileSystem = physicalFileSystem;
            fileOptions.DefaultFilesOptions.DefaultFileNames = new[] { "index.html" };
            fileOptions.StaticFileOptions.FileSystem = fileOptions.FileSystem = physicalFileSystem;
            fileOptions.StaticFileOptions.ServeUnknownFileTypes = true;

            httpConfig.MapHttpAttributeRoutes();

            appBuilder.Use(typeof(CsrfCookieMiddleware), this.config);
            appBuilder.Use(typeof(CustomHeadersMiddleware));

            appBuilder.UseCookieAuthentication(new Microsoft.Owin.Security.Cookies.CookieAuthenticationOptions()
            {
#if !LOCAL
                CookieSecure = Microsoft.Owin.Security.Cookies.CookieSecureOption.Always,
#endif
                AuthenticationType = this.config.AuthenticationTypeName,
                LoginPath = new PathString("/auth/login"),
            });

#if !LOCAL
            appBuilder.UseFacebookAuthentication(
                new Microsoft.Owin.Security.Facebook.FacebookAuthenticationOptions()
                {
                    AppId = this.config.FacebookAppId.ToUnsecureString(),
                    AppSecret = this.config.FacebookAppSecret.ToUnsecureString(),
                    SignInAsAuthenticationType = this.config.AuthenticationTypeName,
                });

            appBuilder.UseGitHubAuthentication(
                new GitHubAuthenticationOptions()
                {
                    ClientId = this.config.GithubClientId.ToUnsecureString(),
                    ClientSecret = this.config.GithubClientSecret.ToUnsecureString(),
                    SignInAsAuthenticationType = this.config.AuthenticationTypeName,
                    Provider = new Owin.Security.Providers.GitHub.GitHubAuthenticationProvider()
                    {
                        OnAuthenticated = (context) =>
                        {
                            return Task.FromResult(false);
                        }
                    },
                });
#endif

            appBuilder.Map(new PathString("/auth/logout"),
                (application) =>
                {
                    application.Run(Logout);
                });

#if LOCAL
            appBuilder.Map(new PathString("/auth/login"),
                (application) =>
                {
                    application.Run(LocalLogin);
                });
#else
            appBuilder.Map(new PathString("/auth/facebook"),
                (application) =>
                {
                    application.Run(InvokeFacebookLogin);
                });

            appBuilder.Map(new PathString("/auth/github"),
                (application) =>
                {
                    application.Run(InvokeGithubLogin);
                });
#endif
            appBuilder.Use(typeof(CsrfValidationMiddleware), this.config);
            appBuilder.UseWebApi(httpConfig);
            appBuilder.UseFileServer(fileOptions);
        }

        public Task Logout(IOwinContext context)
        {
            context.Authentication.SignOut(this.config.AuthenticationTypeName);
            context.Authentication.SignOut(
                context.Authentication.GetAuthenticationTypes()
                .Select(o => o.AuthenticationType).ToArray());

            context.Response.Redirect("/");
            return Task.FromResult(false);
        }

        public Task LocalLogin(IOwinContext context)
        {
            var userId = context.Request.Query["id"] ?? "testUser";

            var claims = new List<Claim>();
            claims.Add(new Claim(ClaimTypes.Name, userId));

            var identity = new ClaimsIdentity(claims, this.config.AuthenticationTypeName);

            context.Authentication.SignIn(new Microsoft.Owin.Security.AuthenticationProperties()
            {
                ExpiresUtc = DateTime.UtcNow.AddMinutes(45),
            }, identity);

            context.Response.Redirect("/");
            return Task.FromResult(false);
        }

        public Task<int> InvokeFacebookLogin(IOwinContext context)
        {
            context.Authentication.Challenge(
                new Microsoft.Owin.Security.AuthenticationProperties()
                    {
                        RedirectUri = "/",
                        ExpiresUtc = DateTime.UtcNow.AddMinutes(45),
                }, "Facebook");

            return Task.FromResult((int)System.Net.HttpStatusCode.Unauthorized);
        }

        public Task<int> InvokeGithubLogin(IOwinContext context)
        {
            context.Authentication.Challenge(
                new Microsoft.Owin.Security.AuthenticationProperties()
                {
                    RedirectUri = "/",
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(45),
                }, "GitHub");

            return Task.FromResult((int)System.Net.HttpStatusCode.Unauthorized);
        }
    }
}