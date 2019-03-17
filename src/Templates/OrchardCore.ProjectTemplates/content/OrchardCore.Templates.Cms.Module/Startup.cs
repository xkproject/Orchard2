using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
#if (AddPart)
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Display.ContentDisplay;
using OrchardCore.ContentManagement.Handlers;
using OrchardCore.ContentTypes.Editors;
using OrchardCore.Data.Migration;
using OrchardCore.Templates.Cms.Module.Drivers;
using OrchardCore.Templates.Cms.Module.Handlers;
using OrchardCore.Templates.Cms.Module.Models;
using OrchardCore.Templates.Cms.Module.Settings;
#endif
using OrchardCore.Modules;

namespace OrchardCore.Templates.Cms.Module
{
    public class Startup : StartupBase
    {
        public override void ConfigureServices(IServiceCollection services)
        {
#if (AddPart)
            services.AddScoped<IContentPartDisplayDriver, MyTestPartDisplayDriver>();
            services.AddSingleton<ContentPart, MyTestPart>();
            services.AddScoped<IContentPartDefinitionDisplayDriver, MyTestPartSettingsDisplayDriver>();
            services.AddScoped<IDataMigration, Migrations>();
            services.AddScoped<IContentPartHandler, MyTestPartHandler>();
#endif
        }

        public override void Configure(IApplicationBuilder builder, IRouteBuilder routes, IServiceProvider serviceProvider)
        {
            routes.MapAreaRoute(
                name: "Home",
                areaName: "OrchardCore.Templates.Cms.Module",
                template: "Home/Index",
                defaults: new { controller = "Home", action = "Index" }
            );
        }
    }
}