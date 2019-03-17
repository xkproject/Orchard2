using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OrchardCore.Environment.Shell.Descriptor.Models;
using OrchardCore.Modules;
using OrchardCore.Navigation;

namespace OrchardCore.Microsoft.Authentication
{
    [Feature(MicrosoftAuthenticationConstants.Features.MicrosoftAccount)]
    public class AdminMenuMicrosoftAccount : INavigationProvider
    {
        private readonly ShellDescriptor _shellDescriptor;

        public AdminMenuMicrosoftAccount(
            IStringLocalizer<AdminMenuMicrosoftAccount> localizer,
            ShellDescriptor shellDescriptor)
        {
            T = localizer;
            _shellDescriptor = shellDescriptor;
        }

        public IStringLocalizer T { get; set; }

        public Task BuildNavigationAsync(string name, NavigationBuilder builder)
        {
            if (String.Equals(name, "admin", StringComparison.OrdinalIgnoreCase))
            {
                builder.Add(T["Microsoft Authentication"], "15", settings => settings
                        .AddClass("microsoft").Id("microsoft")
                        .Add(T["Microsoft Account"], "10", client => client
                            .Action("Index", "Admin", new { area = "OrchardCore.Settings", groupId = MicrosoftAuthenticationConstants.Features.MicrosoftAccount })
                            .Permission(Permissions.ManageMicrosoftAuthentication)
                            .LocalNav())
                    );
            }
            return Task.CompletedTask;
        }
    }

    [Feature(MicrosoftAuthenticationConstants.Features.AAD)]
    public class AdminMenuAAD : INavigationProvider
    {
        private readonly ShellDescriptor _shellDescriptor;

        public AdminMenuAAD(
            IStringLocalizer<AdminMenuAAD> localizer,
            ShellDescriptor shellDescriptor)
        {
            T = localizer;
            _shellDescriptor = shellDescriptor;
        }

        public IStringLocalizer T { get; set; }

        public Task BuildNavigationAsync(string name, NavigationBuilder builder)
        {
            if (String.Equals(name, "admin", StringComparison.OrdinalIgnoreCase))
            {
                builder.Add(T["Microsoft Authentication"], "15", settings => settings
                        .AddClass("microsoft").Id("microsoft")
                        .Add(T["Azure Active Directory"], "20", client => client
                            .Action("Index", "Admin", new { area = "OrchardCore.Settings", groupId = MicrosoftAuthenticationConstants.Features.AAD })
                            .Permission(Permissions.ManageMicrosoftAuthentication)
                            .LocalNav())
                    );
            }
            return Task.CompletedTask;
        }
    }

}
