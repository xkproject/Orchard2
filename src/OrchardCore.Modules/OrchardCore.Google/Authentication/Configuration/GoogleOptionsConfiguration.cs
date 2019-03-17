using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchardCore.Environment.Shell;
using OrchardCore.Google.Authentication.Services;
using OrchardCore.Google.Authentication.Settings;

namespace OrchardCore.Google.Authentication.Configuration
{
    public class GoogleOptionsConfiguration :
        IConfigureOptions<AuthenticationOptions>,
        IConfigureNamedOptions<GoogleOptions>
    {
        private readonly GoogleAuthenticationService _googleAuthenticationService;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly ILogger<GoogleOptionsConfiguration> _logger;
        private readonly string _tenantPrefix;


        public GoogleOptionsConfiguration(
            GoogleAuthenticationService googleAuthenticationService,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<GoogleOptionsConfiguration> logger,
            ShellSettings shellSettings)
        {
            _googleAuthenticationService = googleAuthenticationService;
            _dataProtectionProvider = dataProtectionProvider;
            _logger = logger;
            _tenantPrefix = "/" + shellSettings.RequestUrlPrefix;
        }

        public void Configure(AuthenticationOptions options)
        {
            var settings = GetGoogleAuthenticationSettingsAsync().GetAwaiter().GetResult();
            if (settings == null)
            {
                return;
            }

            if (!_googleAuthenticationService.CheckSettings(settings))
                return;

            options.AddScheme(GoogleDefaults.AuthenticationScheme, builder =>
            {
                builder.DisplayName = "Google";
                builder.HandlerType = typeof(GoogleHandler);
            });
        }

        public void Configure(string name, GoogleOptions options)
        {
            if (!string.Equals(name, GoogleDefaults.AuthenticationScheme, StringComparison.Ordinal))
            {
                return;
            }
            var settings = GetGoogleAuthenticationSettingsAsync().GetAwaiter().GetResult();
            options.ClientId = settings?.ClientID ?? string.Empty;
            try
            {
                options.ClientSecret = _dataProtectionProvider.CreateProtector(GoogleConstants.Features.GoogleAuthentication).Unprotect(settings.ClientSecret);
            }
            catch
            {
                _logger.LogError("The Consumer Secret could not be decrypted. It may have been encrypted using a different key.");
            }

            if (settings.CallbackPath.HasValue)
            {
                options.CallbackPath = settings.CallbackPath;
            }
        }

        public void Configure(GoogleOptions options) => Debug.Fail("This infrastructure method shouldn't be called.");

        private async Task<GoogleAuthenticationSettings> GetGoogleAuthenticationSettingsAsync()
        {
            var settings = await _googleAuthenticationService.GetSettingsAsync();
            if (!_googleAuthenticationService.CheckSettings(settings))
            {
                _logger.LogWarning("Google Authentication is not correctly configured.");
                return null;
            }
            return settings;
        }
    }
}
