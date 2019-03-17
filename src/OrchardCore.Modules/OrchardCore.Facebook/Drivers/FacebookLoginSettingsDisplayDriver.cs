using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using OrchardCore.DisplayManagement.Entities;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.DisplayManagement.Views;
using OrchardCore.Environment.Shell;
using OrchardCore.Facebook.Settings;
using OrchardCore.Facebook.ViewModels;
using OrchardCore.Settings;

namespace OrchardCore.Facebook.Drivers
{
    public class FacebookLoginSettingsDisplayDriver : SectionDisplayDriver<ISite, FacebookLoginSettings>
    {
        private readonly IAuthorizationService _authorizationService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IShellHost _shellHost;
        private readonly ShellSettings _shellSettings;

        public FacebookLoginSettingsDisplayDriver(
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            IShellHost shellHost,
            ShellSettings shellSettings)
        {
            _authorizationService = authorizationService;
            _httpContextAccessor = httpContextAccessor;
            _shellHost = shellHost;
            _shellSettings = shellSettings;
        }

        public override async Task<IDisplayResult> EditAsync(FacebookLoginSettings settings, BuildEditorContext context)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !await _authorizationService.AuthorizeAsync(user, Permissions.ManageFacebookApp))
            {
                return null;
            }

            return Initialize<FacebookLoginSettingsViewModel>("FacebookLoginSettings_Edit", model =>
            {
                model.CallbackPath = settings.CallbackPath;
            }).Location("Content:5").OnGroup(FacebookConstants.Features.Login);
        }

        public override async Task<IDisplayResult> UpdateAsync(FacebookLoginSettings settings, BuildEditorContext context)
        {
            if (context.GroupId == FacebookConstants.Features.Login)
            {
                var user = _httpContextAccessor.HttpContext?.User;
                if (user == null || !await _authorizationService.AuthorizeAsync(user, Permissions.ManageFacebookApp))
                {
                    return null;
                }

                var model = new FacebookLoginSettingsViewModel();
                await context.Updater.TryUpdateModelAsync(model, Prefix);

                if (context.Updater.ModelState.IsValid)
                {
                    settings.CallbackPath = model.CallbackPath;
                    await _shellHost.ReloadShellContextAsync(_shellSettings);
                }
            }
            return await EditAsync(settings, context);
        }
    }
}