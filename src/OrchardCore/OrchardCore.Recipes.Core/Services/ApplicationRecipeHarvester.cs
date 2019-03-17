using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OrchardCore.Environment.Extensions;
using OrchardCore.Recipes.Models;

namespace OrchardCore.Recipes.Services
{
    /// <summary>
    /// Finds recipes in the application content folder.
    /// </summary>
    public class ApplicationRecipeHarvester : RecipeHarvester
    {
        public ApplicationRecipeHarvester(
            IRecipeReader recipeReader,
            IExtensionManager extensionManager,
            IHostingEnvironment hostingEnvironment,
            ILogger<RecipeHarvester> logger)
            : base(recipeReader, extensionManager, hostingEnvironment, logger)
        {
        }

        public IStringLocalizer T { get; set; }

        public override Task<IEnumerable<RecipeDescriptor>> HarvestRecipesAsync()
        {
            return HarvestRecipesAsync("Recipes");
        }
    }
}