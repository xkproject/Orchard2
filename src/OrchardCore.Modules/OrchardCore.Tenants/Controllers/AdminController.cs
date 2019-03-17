using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using OrchardCore.Data;
using OrchardCore.DisplayManagement;
using OrchardCore.DisplayManagement.Notify;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Models;
using OrchardCore.Modules;
using OrchardCore.Navigation;
using OrchardCore.Recipes.Services;
using OrchardCore.Settings;
using OrchardCore.Tenants.ViewModels;

namespace OrchardCore.Tenants.Controllers
{
    public class AdminController : Controller
    {
        private readonly IShellHost _shellHost;
        private readonly IShellSettingsManager _shellSettingsManager;
        private readonly IEnumerable<DatabaseProvider> _databaseProviders;
        private readonly IAuthorizationService _authorizationService;
        private readonly ShellSettings _currentShellSettings;
        private readonly IEnumerable<IRecipeHarvester> _recipeHarvesters;
        private readonly IDataProtectionProvider _dataProtectorProvider;
        private readonly IClock _clock;
        private readonly INotifier _notifier;
        private readonly ISiteService _siteService;

        private readonly dynamic New;

        public AdminController(
            IShellHost shellHost,
            ShellSettings currentShellSettings,
            IAuthorizationService authorizationService,
            IShellSettingsManager shellSettingsManager,
            IEnumerable<DatabaseProvider> databaseProviders,
            IDataProtectionProvider dataProtectorProvider,
            IClock clock,
            INotifier notifier,
            IEnumerable<IRecipeHarvester> recipeHarvesters,
            ISiteService siteService,
            IShapeFactory shapeFactory,
            IStringLocalizer<AdminController> stringLocalizer,
            IHtmlLocalizer<AdminController> htmlLocalizer)
        {
            _dataProtectorProvider = dataProtectorProvider;
            _clock = clock;
            _recipeHarvesters = recipeHarvesters;
            _shellHost = shellHost;
            _authorizationService = authorizationService;
            _shellSettingsManager = shellSettingsManager;
            _databaseProviders = databaseProviders;
            _currentShellSettings = currentShellSettings;
            _notifier = notifier;
            _siteService = siteService;

            New = shapeFactory;
            S = stringLocalizer;
            H = htmlLocalizer;
        }

        public IStringLocalizer S { get; set; }
        public IHtmlLocalizer H { get; set; }

        public async Task<IActionResult> Index(TenantIndexOptions options, PagerParameters pagerParameters)
        {
            var allSettings = _shellHost.GetAllSettings().OrderBy(s => s.Name);
            var dataProtector = _dataProtectorProvider.CreateProtector("Tokens").ToTimeLimitedDataProtector();

            var siteSettings = await _siteService.GetSiteSettingsAsync();
            var pager = new Pager(pagerParameters, siteSettings.PageSize);

            // default options
            if (options == null)
            {
                options = new TenantIndexOptions();
            }

            var entries = allSettings.Select(x =>
                {
                    var entry = new ShellSettingsEntry
                    {
                        Name = x.Name,
                        ShellSettings = x,
                        IsDefaultTenant = string.Equals(x.Name, ShellHelper.DefaultShellName, StringComparison.OrdinalIgnoreCase)
                    };

                    if (x.State == TenantState.Uninitialized && !string.IsNullOrEmpty(x["Secret"]))
                    {
                        entry.Token = dataProtector.Protect(x["Secret"], _clock.UtcNow.Add(new TimeSpan(24, 0, 0)));
                    }

                    return entry;
                }).ToList();

            if (!string.IsNullOrWhiteSpace(options.Search))
            {
                entries = entries.Where(t => t.Name.IndexOf(options.Search, StringComparison.OrdinalIgnoreCase) > -1 ||
                    (t.ShellSettings != null && t.ShellSettings != null &&
                    ((t.ShellSettings.RequestUrlHost != null && t.ShellSettings.RequestUrlHost.IndexOf(options.Search, StringComparison.OrdinalIgnoreCase) > -1) ||
                    (t.ShellSettings.RequestUrlPrefix != null && t.ShellSettings.RequestUrlPrefix.IndexOf(options.Search, StringComparison.OrdinalIgnoreCase) > -1)))).ToList();
            }

            switch (options.Filter)
            {
                case TenantsFilter.Disabled:
                    entries = entries.Where(t => t.ShellSettings.State == TenantState.Disabled).ToList();
                    break;
                case TenantsFilter.Running:
                    entries = entries.Where(t => t.ShellSettings.State == TenantState.Running).ToList();
                    break;
                case TenantsFilter.Uninitialized:
                    entries = entries.Where(t => t.ShellSettings.State == TenantState.Uninitialized).ToList();
                    break;
            }

            switch (options.OrderBy)
            {
                case TenantsOrder.Name:
                    entries = entries.OrderBy(t => t.Name).ToList();
                    break;
                case TenantsOrder.State:
                    entries = entries.OrderBy(t => t.ShellSettings?.State).ToList();
                    break;
                default:
                    entries = entries.OrderByDescending(t => t.Name).ToList();
                    break;
            }
            var count = entries.Count();

            var results = entries
                .Skip(pager.GetStartIndex())
                .Take(pager.PageSize).ToList();

            // Maintain previous route data when generating page links
            var routeData = new RouteData();
            routeData.Values.Add("Options.Filter", options.Filter);
            routeData.Values.Add("Options.Search", options.Search);
            routeData.Values.Add("Options.OrderBy", options.OrderBy);

            var pagerShape = (await New.Pager(pager)).TotalItemCount(count).RouteData(routeData);

            var model = new AdminIndexViewModel
            {
                ShellSettingsEntries = results,
                Options = options,
                Pager = pagerShape
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Index(BulkActionViewModel model)
        {
            var allSettings = _shellHost.GetAllSettings();

            foreach (var tenantName in model.TenantNames ?? Enumerable.Empty<string>())
            {
                var shellSettings = allSettings
                    .Where(x => string.Equals(x.Name, tenantName, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (shellSettings == null)
                {
                    break;
                }

                switch (model.BulkAction.ToString())
                {
                    case "Disable":
                        if (string.Equals(shellSettings.Name, ShellHelper.DefaultShellName, StringComparison.OrdinalIgnoreCase))
                        {
                            _notifier.Warning(H["You cannot disable the default tenant."]);
                        }
                        else if (shellSettings.State != TenantState.Running)
                        {
                            _notifier.Warning(H["The tenant '{0}' is already disabled.", shellSettings.Name]);
                        }
                        else
                        {
                            shellSettings.State = TenantState.Disabled;
                            await _shellHost.UpdateShellSettingsAsync(shellSettings);
                        }

                        break;

                    case "Enable":
                        if (shellSettings.State != TenantState.Disabled)
                        {
                            _notifier.Warning(H["The tenant '{0}' is already enabled.", shellSettings.Name]);
                        }
                        else
                        {
                            shellSettings.State = TenantState.Running;
                            await _shellHost.UpdateShellSettingsAsync(shellSettings);
                        }

                        break;

                    default:
                        break;
                }
            }

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Create()
        {
            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageTenants))
            {
                return Unauthorized();
            }

            if (!IsDefaultShell())
            {
                return Unauthorized();
            }

            var recipeCollections = await Task.WhenAll(_recipeHarvesters.Select(x => x.HarvestRecipesAsync()));
            var recipes = recipeCollections.SelectMany(x => x).Where(x => x.IsSetupRecipe).ToArray();

            // Creates a default shell settings based on the configuration.
            var shellSettings = _shellSettingsManager.CreateDefaultSettings();

            var model = new EditTenantViewModel
            {
                Recipes = recipes,
                RequestUrlHost = shellSettings.RequestUrlHost,
                RequestUrlPrefix = shellSettings.RequestUrlPrefix,
                DatabaseProvider = shellSettings["DatabaseProvider"],
                TablePrefix = shellSettings["TablePrefix"],
                ConnectionString = shellSettings["ConnectionString"],
                RecipeName = shellSettings["RecipeName"]
            };

            model.Recipes = recipes;

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Create(EditTenantViewModel model)
        {
            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageTenants))
            {
                return Unauthorized();
            }

            if (!IsDefaultShell())
            {
                return Unauthorized();
            }

            if (ModelState.IsValid)
            {
                ValidateViewModel(model, true);
            }

            if (ModelState.IsValid)
            {
                // Creates a default shell settings based on the configuration.
                var shellSettings = _shellSettingsManager.CreateDefaultSettings();

                shellSettings.Name = model.Name;
                shellSettings.RequestUrlHost = model.RequestUrlHost;
                shellSettings.RequestUrlPrefix = model.RequestUrlPrefix;
                shellSettings.State = TenantState.Uninitialized;

                shellSettings["ConnectionString"] = model.ConnectionString;
                shellSettings["TablePrefix"] = model.TablePrefix;
                shellSettings["DatabaseProvider"] = model.DatabaseProvider;
                shellSettings["Secret"] = Guid.NewGuid().ToString();
                shellSettings["RecipeName"] = model.RecipeName;

                _shellSettingsManager.SaveSettings(shellSettings);
                var shellContext = await _shellHost.GetOrCreateShellContextAsync(shellSettings);

                return RedirectToAction(nameof(Index));
            }

            var recipeCollections = await Task.WhenAll(_recipeHarvesters.Select(x => x.HarvestRecipesAsync()));
            var recipes = recipeCollections.SelectMany(x => x).Where(x => x.IsSetupRecipe).ToArray();
            model.Recipes = recipes;

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        public async Task<IActionResult> Edit(string id)
        {
            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageTenants))
            {
                return Unauthorized();
            }

            if (!IsDefaultShell())
            {
                return Unauthorized();
            }

            var shellSettings = _shellHost.GetAllSettings()
                .Where(x => string.Equals(x.Name, id, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (shellSettings == null)
            {
                return NotFound();
            }

            var model = new EditTenantViewModel
            {
                Name = shellSettings.Name,
                RequestUrlHost = shellSettings.RequestUrlHost,
                RequestUrlPrefix = shellSettings.RequestUrlPrefix,
            };

            // The user can change the 'preset' database information only if the 
            // tenant has not been initialized yet
            if (shellSettings.State == TenantState.Uninitialized)
            {
                var recipeCollections = await Task.WhenAll(_recipeHarvesters.Select(x => x.HarvestRecipesAsync()));
                var recipes = recipeCollections.SelectMany(x => x).Where(x => x.IsSetupRecipe).ToArray();
                model.Recipes = recipes;

                model.DatabaseProvider = shellSettings["DatabaseProvider"];
                model.TablePrefix = shellSettings["TablePrefix"];
                model.ConnectionString = shellSettings["ConnectionString"];
                model.RecipeName = shellSettings["RecipeName"];
                model.CanSetDatabasePresets = true;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(EditTenantViewModel model)
        {
            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageTenants))
            {
                return Unauthorized();
            }

            if (!IsDefaultShell())
            {
                return Unauthorized();
            }

            if (ModelState.IsValid)
            {
                ValidateViewModel(model, false);
            }

            var shellSettings = _shellHost.GetAllSettings()
                .Where(x => string.Equals(x.Name, model.Name, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (shellSettings == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                shellSettings.RequestUrlPrefix = model.RequestUrlPrefix;
                shellSettings.RequestUrlHost = model.RequestUrlHost;

                // The user can change the 'preset' database information only if the 
                // tenant has not been initialized yet
                if (shellSettings.State == TenantState.Uninitialized)
                {
                    shellSettings["DatabaseProvider"] = model.DatabaseProvider;
                    shellSettings["TablePrefix"] = model.TablePrefix;
                    shellSettings["ConnectionString"] = model.ConnectionString;
                    shellSettings["RecipeName"] = model.RecipeName;
                    shellSettings["Secret"] = Guid.NewGuid().ToString();
                }

                await _shellHost.UpdateShellSettingsAsync(shellSettings);

                return RedirectToAction(nameof(Index));
            }

            // The user can change the 'preset' database information only if the 
            // tenant has not been initialized yet
            if (shellSettings.State == TenantState.Uninitialized)
            {
                model.DatabaseProvider = shellSettings["DatabaseProvider"];
                model.TablePrefix = shellSettings["TablePrefix"];
                model.ConnectionString = shellSettings["ConnectionString"];
                model.RecipeName = shellSettings["RecipeName"];
                model.CanSetDatabasePresets = true;
            }

            var recipeCollections = await Task.WhenAll(_recipeHarvesters.Select(x => x.HarvestRecipesAsync()));
            var recipes = recipeCollections.SelectMany(x => x).Where(x => x.IsSetupRecipe).ToArray();
            model.Recipes = recipes;

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Disable(string id)
        {
            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageTenants))
            {
                return Unauthorized();
            }

            if (!IsDefaultShell())
            {
                return Unauthorized();
            }

            var shellSettings = _shellHost.GetAllSettings()
                .Where(s => string.Equals(s.Name, id, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (shellSettings == null)
            {
                return NotFound();
            }

            if (string.Equals(shellSettings.Name, ShellHelper.DefaultShellName, StringComparison.OrdinalIgnoreCase))
            {
                _notifier.Error(H["You cannot disable the default tenant."]);
                return RedirectToAction(nameof(Index));
            }

            if (shellSettings.State != TenantState.Running)
            {
                _notifier.Error(H["You can only disable an Enabled tenant."]);
                return RedirectToAction(nameof(Index));
            }

            shellSettings.State = TenantState.Disabled;
            await _shellHost.UpdateShellSettingsAsync(shellSettings);

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Enable(string id)
        {
            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageTenants))
            {
                return Unauthorized();
            }

            if (!IsDefaultShell())
            {
                return Unauthorized();
            }

            var shellSettings = _shellHost.GetAllSettings()
                .Where(x => string.Equals(x.Name, id, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (shellSettings == null)
            {
                return NotFound();
            }

            if (shellSettings.State != TenantState.Disabled)
            {
                _notifier.Error(H["You can only enable a Disabled tenant."]);
            }

            shellSettings.State = TenantState.Running;
            await _shellHost.UpdateShellSettingsAsync(shellSettings);

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Reload(string id)
        {
            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageTenants))
            {
                return Unauthorized();
            }

            if (!IsDefaultShell())
            {
                return Unauthorized();
            }

            var shellSettings = _shellHost.GetAllSettings()
                .OrderBy(x => x.Name)
                .Where(x => string.Equals(x.Name, id, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (shellSettings == null)
            {
                return NotFound();
            }

            // Generating routes can fail while the tenant is recycled as routes can use services.
            // It could be fixed by waiting for the next request or the end of the current one
            // to actually release the tenant. Right now we render the url before recycling the tenant.

            var redirectUrl = Url.Action(nameof(Index));

            await _shellHost.ReloadShellContextAsync(shellSettings);

            return Redirect(redirectUrl);
        }

        private void ValidateViewModel(EditTenantViewModel model, bool newTenant)
        {
            var selectedProvider = _databaseProviders.FirstOrDefault(x => x.Value == model.DatabaseProvider);

            if (selectedProvider != null && selectedProvider.HasConnectionString && String.IsNullOrWhiteSpace(model.ConnectionString))
            {
                ModelState.AddModelError(nameof(EditTenantViewModel.ConnectionString), S["The connection string is mandatory for this provider."]);
            }

            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(EditTenantViewModel.Name), S["The tenant name is mandatory."]);
            }

            var allSettings = _shellHost.GetAllSettings();

            if (newTenant && allSettings.Any(tenant => string.Equals(tenant.Name, model.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError(nameof(EditTenantViewModel.Name), S["A tenant with the same name already exists.", model.Name]);
            }

            if (!string.IsNullOrEmpty(model.Name) && !Regex.IsMatch(model.Name, @"^\w+$"))
            {
                ModelState.AddModelError(nameof(EditTenantViewModel.Name), S["Invalid tenant name. Must contain characters only and no spaces."]);
            }

            if (!IsDefaultShell() && string.IsNullOrWhiteSpace(model.RequestUrlHost) && string.IsNullOrWhiteSpace(model.RequestUrlPrefix))
            {
                ModelState.AddModelError(nameof(EditTenantViewModel.RequestUrlPrefix), S["Host and url prefix can not be empty at the same time."]);
            }

            var allOtherShells = allSettings.Where(tenant => !string.Equals(tenant.Name, model.Name, StringComparison.OrdinalIgnoreCase));
            if (allOtherShells.Any(tenant => string.Equals(tenant.RequestUrlPrefix, model.RequestUrlPrefix?.Trim(), StringComparison.OrdinalIgnoreCase) && string.Equals(tenant.RequestUrlHost, model.RequestUrlHost, StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError(nameof(EditTenantViewModel.RequestUrlPrefix), S["A tenant with the same host and prefix already exists.", model.Name]);
            }

            if (!string.IsNullOrWhiteSpace(model.RequestUrlPrefix))
            {
                if (model.RequestUrlPrefix.Contains('/'))
                {
                    ModelState.AddModelError(nameof(EditTenantViewModel.RequestUrlPrefix), S["The url prefix can not contain more than one segment."]);
                }
            }
        }

        private bool IsDefaultShell()
        {
            return string.Equals(_currentShellSettings.Name, ShellHelper.DefaultShellName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
