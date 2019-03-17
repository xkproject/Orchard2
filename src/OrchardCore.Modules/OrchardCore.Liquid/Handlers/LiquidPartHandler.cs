using System.Threading.Tasks;
using Fluid;
using Microsoft.AspNetCore.Html;
using OrchardCore.ContentManagement.Handlers;
using OrchardCore.ContentManagement.Models;
using OrchardCore.Liquid.Model;

namespace OrchardCore.Liquid.Handlers
{
    public class LiquidPartHandler : ContentPartHandler<LiquidPart>
    {
        private readonly ILiquidTemplateManager _liquidTemplateManager;

        public LiquidPartHandler(ILiquidTemplateManager liquidTemplateManager)
        {
            _liquidTemplateManager = liquidTemplateManager;
        }

        public override Task GetContentItemAspectAsync(ContentItemAspectContext context, LiquidPart part)
        {
            context.For<BodyAspect>(bodyAspect =>
            {
                try
                {
                    var result = _liquidTemplateManager.RenderAsync(part.Liquid, System.Text.Encodings.Web.HtmlEncoder.Default, new TemplateContext()).GetAwaiter().GetResult();
                    bodyAspect.Body = new HtmlString(result);
                }
                catch
                {
                    bodyAspect.Body = HtmlString.Empty;
                }
            });

            return Task.CompletedTask;
        }
    }
}