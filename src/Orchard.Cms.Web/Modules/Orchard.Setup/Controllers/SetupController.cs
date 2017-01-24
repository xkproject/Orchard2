using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Orchard.Data;
using Orchard.Environment.Shell;
using Orchard.Recipes.Models;
using Orchard.Setup.Services;
using Orchard.Setup.ViewModels;

namespace Orchard.Setup.Controllers
{
    public class SetupController : Controller
    {
        private readonly ISetupService _setupService;
        private readonly ShellSettings _shellSettings;
        private const string DefaultRecipe = "Default";
        private readonly IEnumerable<DatabaseProvider> _databaseProviders;

        public SetupController(
            ISetupService setupService,
            ShellSettings shellSettings,
            IEnumerable<DatabaseProvider> databaseProviders,
            IStringLocalizer<SetupController> t)
        {
            _setupService = setupService;
            _shellSettings = shellSettings;
            _databaseProviders = databaseProviders;

            T = t;
        }

        public IStringLocalizer T { get; set; }

        public async Task<ActionResult> Index()
        {
            var recipes = await _setupService.GetSetupRecipesAsync();
            var defaultRecipe = recipes.FirstOrDefault(x => x.Tags.Contains("default")) ?? recipes.First();

            var model = new SetupViewModel
            {
                DatabaseProviders = _databaseProviders,
                Recipes = recipes,
                RecipeName = defaultRecipe.Name
            };

            if (!String.IsNullOrEmpty(_shellSettings.ConnectionString))
            {
                model.ConnectionStringPreset = true;
                model.ConnectionString = _shellSettings.ConnectionString;
            }

            if (!String.IsNullOrEmpty(_shellSettings.DatabaseProvider))
            {
                model.DatabaseProviderPreset = true;
                model.DatabaseProvider = _shellSettings.DatabaseProvider;
            }

            if (!String.IsNullOrEmpty(_shellSettings.TablePrefix))
            {
                model.TablePrefixPreset = true;
                model.TablePrefix = _shellSettings.TablePrefix;
            }
            model.SiteName = "Distrib Web API";
            model.TablePrefixPreset = true;
            if (Request.Query.ContainsKey("ConnectionString"))
                model.ConnectionString = Request.Query["ConnectionString"];
            else
                model.ConnectionString = "Data Source={DATASOURCE};Initial Catalog=DistribWebAPI;Trusted_Connection=Yes;";// User ID=orchard;Password={PASSWORD};";
            model.UserName = "admin";
            model.DistribB2BUserName = "distribb2b";
            model.OpenIDAppId = "distribb2b";

            return View(model);
        }

        [HttpPost, ActionName("Index")]
        public async Task<ActionResult> IndexPOST(SetupViewModel model)
        {
            model.DatabaseProviders = _databaseProviders;
            model.Recipes = await _setupService.GetSetupRecipesAsync();

            var selectedProvider = model.DatabaseProviders.FirstOrDefault(x => x.Value == model.DatabaseProvider);

            if (selectedProvider != null && selectedProvider.HasConnectionString && String.IsNullOrWhiteSpace(model.ConnectionString))
            {
                ModelState.AddModelError(nameof(model.ConnectionString), T["The connection string is mandatory for this provider."]);
            }

            if (String.IsNullOrEmpty(model.Password))
            {
                ModelState.AddModelError(nameof(model.Password), T["The password is required."]);
            }

            if (model.Password != model.PasswordConfirmation)
            {
                ModelState.AddModelError(nameof(model.PasswordConfirmation), T["The password confirmation doesn't match the password."]);
            }

            RecipeDescriptor selectedRecipe = null;

            if (String.IsNullOrEmpty(model.RecipeName) || (selectedRecipe = model.Recipes.FirstOrDefault(x => x.Name == model.RecipeName)) == null)
            {
                ModelState.AddModelError(nameof(model.RecipeName), T["Invalid recipe."]);
            }

            if (!String.IsNullOrEmpty(_shellSettings.ConnectionString))
            {
                model.ConnectionStringPreset = true;
                model.ConnectionString = _shellSettings.ConnectionString;
            }

            if (!String.IsNullOrEmpty(_shellSettings.DatabaseProvider))
            {
                model.DatabaseProviderPreset = true;
                model.DatabaseProvider = _shellSettings.DatabaseProvider;
            }

            if (!String.IsNullOrEmpty(_shellSettings.TablePrefix))
            {
                model.TablePrefixPreset = true;
                model.TablePrefix = _shellSettings.TablePrefix;
            }

            model.TablePrefixPreset = true;

            if (model.DistribB2BUserPassword != model.DistribB2BUserPasswordConfirmation)
            {
                ModelState.AddModelError(nameof(model.DistribB2BUserPasswordConfirmation), T["The password confirmation doesn't match the password."]);
            }

            if (model.Email == model.DistribB2BUserEmail)
            {
                ModelState.AddModelError(nameof(model.DistribB2BUserEmail), T["The Distrib B2B user cannot have same email as admin user."]);
            }

            if (model.ClientSecret != model.ClientSecretConfirmation)
            {
                ModelState.AddModelError(nameof(model.ClientSecretConfirmation), T["The client secret confirmation doesn't match the password."]);
            }

            if (model.Password == model.DistribB2BUserPassword)
            {
                ModelState.AddModelError(nameof(model.DistribB2BUserPassword), T["The admin password cannot be same password as the Distrib B2B user password."]);
            }

            if (model.Password == model.ClientSecret)
            {
                ModelState.AddModelError(nameof(model.ClientSecret), T["The client secret cannot be same password as the admin password."]);
            }

            if (model.DistribB2BUserPassword == model.ClientSecret)
            {
                ModelState.AddModelError(nameof(model.ClientSecret), T["The client secret cannot be same password as the Distrib B2B user password."]);
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var setupContext = new SetupContext
            {
                SiteName = model.SiteName,
                EnabledFeatures = null, // default list,
                AdminUsername = model.UserName,
                AdminEmail = model.Email,
                AdminPassword = model.Password,
                Errors = new Dictionary<string, string>(),
                Recipe = selectedRecipe,

                DistribB2BUserName = model.DistribB2BUserName,
                DistribB2BUserEmail = model.DistribB2BUserEmail,
                DistribB2BUserPassword = model.DistribB2BUserPassword,
                DistribB2BUserPasswordConfirmation = model.DistribB2BUserPasswordConfirmation,
                OpenIDAppId = model.OpenIDAppId,
                ClientSecret = model.ClientSecret,
                ClientSecretConfirmation = model.ClientSecretConfirmation
            };

            if (!model.DatabaseProviderPreset)
            {
                setupContext.DatabaseProvider = model.DatabaseProvider;
            }

            if (!model.ConnectionStringPreset)
            {
                setupContext.DatabaseConnectionString = model.ConnectionString;
            }

            if (!model.TablePrefixPreset)
            {
                setupContext.DatabaseTablePrefix = model.TablePrefix;
            }

            var executionId = await _setupService.SetupAsync(setupContext);

            // Check if a component in the Setup failed
            if (setupContext.Errors.Any())
            {
                foreach (var error in setupContext.Errors)
                {
                    ModelState.AddModelError(error.Key, error.Value);
                }

                return View(model);
            }

            return Redirect("~/");
        }
    }
}