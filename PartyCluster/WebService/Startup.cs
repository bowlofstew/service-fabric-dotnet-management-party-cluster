// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService
{
    using System.Fabric;
    using System.Web.Http;
    using Microsoft.Owin;
    using Microsoft.Owin.FileSystems;
    using Microsoft.Owin.StaticFiles;
    using Owin;

    internal class Startup : IOwinAppBuilder
    {
        private readonly StatelessServiceContext serviceContext;

        public Startup(StatelessServiceContext serviceContext)
        {
            this.serviceContext = serviceContext;
        }

        public void Configuration(IAppBuilder appBuilder)
        {
            HttpConfiguration config = new HttpConfiguration();

            FormatterConfig.ConfigureFormatters(config.Formatters);
            UnityConfig.RegisterComponents(config, this.serviceContext);

            PhysicalFileSystem physicalFileSystem = new PhysicalFileSystem(@".\wwwroot");
            FileServerOptions fileOptions = new FileServerOptions();

            fileOptions.EnableDefaultFiles = true;
            fileOptions.RequestPath = PathString.Empty;
            fileOptions.FileSystem = physicalFileSystem;
            fileOptions.DefaultFilesOptions.DefaultFileNames = new[] {"index.html"};
            fileOptions.StaticFileOptions.FileSystem = fileOptions.FileSystem = physicalFileSystem;
            fileOptions.StaticFileOptions.ServeUnknownFileTypes = true;

            config.MapHttpAttributeRoutes();

            appBuilder.UseWebApi(config);
            appBuilder.UseFileServer(fileOptions);
        }
    }
}