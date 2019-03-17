using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Apis;
using OrchardCore.Media.Fields;
using OrchardCore.Modules;

namespace OrchardCore.Media.GraphQL
{
    [RequireFeatures("OrchardCore.Apis.GraphQL")]
    public class Startup : StartupBase
    {
        public override void ConfigureServices(IServiceCollection services)
        {
             services.AddObjectGraphType<MediaField, MediaFieldQueryObjectType>();
        }
    }
}
