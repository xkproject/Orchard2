using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Primitives;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Mvc;
using OpenIddict.Server;
using OpenIddict.Validation;
using OrchardCore.Environment.Shell;
using OrchardCore.Modules;
using OrchardCore.OpenId.Services;
using OrchardCore.OpenId.Settings;

namespace OrchardCore.OpenId.Configuration
{
    [Feature(OpenIdConstants.Features.Server)]
    public class OpenIdServerConfiguration : IConfigureOptions<AuthenticationOptions>,
        IConfigureOptions<OpenIddictMvcOptions>,
        IConfigureNamedOptions<OpenIddictServerOptions>,
        IConfigureNamedOptions<OpenIddictValidationOptions>,
        IConfigureNamedOptions<JwtBearerOptions>
    {
        private readonly ILogger<OpenIdServerConfiguration> _logger;
        private readonly IRunningShellTable _runningShellTable;
        private readonly ShellSettings _shellSettings;
        private readonly IOpenIdServerService _serverService;

        public OpenIdServerConfiguration(
            ILogger<OpenIdServerConfiguration> logger,
            IRunningShellTable runningShellTable,
            ShellSettings shellSettings,
            IOpenIdServerService serverService)
        {
            _logger = logger;
            _runningShellTable = runningShellTable;
            _shellSettings = shellSettings;
            _serverService = serverService;
        }

        public void Configure(AuthenticationOptions options)
        {
            var settings = GetServerSettingsAsync().GetAwaiter().GetResult();
            if (settings == null)
            {
                return;
            }

            // Register the OpenIddict handler in the authentication handlers collection.
            options.AddScheme(OpenIddictServerDefaults.AuthenticationScheme, builder =>
            {
                builder.HandlerType = typeof(OpenIddictServerHandler);
            });

            // If the userinfo endpoint was enabled, register a private JWT or validation handler instance.
            // Unlike the instance registered by the validation feature, this one is only used for the
            // OpenID Connect userinfo endpoint and thus only supports local opaque/JWT token validation.
            if (settings.EnableUserInfoEndpoint)
            {
                if (settings.AccessTokenFormat == OpenIdServerSettings.TokenFormat.Encrypted)
                {
                    options.AddScheme(OpenIdConstants.Schemes.Userinfo, builder =>
                    {
                        builder.HandlerType = typeof(OpenIddictValidationHandler);
                    });
                }
                else if (settings.AccessTokenFormat == OpenIdServerSettings.TokenFormat.JWT)
                {
                    options.AddScheme(OpenIdConstants.Schemes.Userinfo, builder =>
                    {
                        builder.HandlerType = typeof(JwtBearerHandler);
                    });
                }
                else
                {
                    throw new InvalidOperationException("The specified access token format is not valid.");
                }
            }
        }

        public void Configure(OpenIddictMvcOptions options) => options.DisableBindingExceptions = true;

        public void Configure(string name, OpenIddictServerOptions options)
        {
            // Ignore OpenIddict handler instances that don't correspond to the instance managed by the OpenID module.
            if (!string.Equals(name, OpenIddictServerDefaults.AuthenticationScheme, StringComparison.Ordinal))
            {
                return;
            }

            var settings = GetServerSettingsAsync().GetAwaiter().GetResult();
            if (settings == null)
            {
                return;
            }

            options.ApplicationCanDisplayErrors = true;
            options.EnableRequestCaching = true;
            options.IgnoreScopePermissions = true;
            options.UseRollingTokens = settings.UseRollingTokens;
            options.AllowInsecureHttp = settings.TestingModeEnabled;

            foreach (var key in _serverService.GetSigningKeysAsync().GetAwaiter().GetResult())
            {
                options.SigningCredentials.AddKey(key);
            }

            if (!string.IsNullOrEmpty(settings.Authority))
            {
                options.Issuer = new Uri(settings.Authority, UriKind.Absolute);
            }

            if (settings.AccessTokenFormat == OpenIdServerSettings.TokenFormat.JWT)
            {
                options.AccessTokenHandler = new JwtSecurityTokenHandler();
            }

            if (settings.EnableAuthorizationEndpoint)
            {
                options.AuthorizationEndpointPath = "/OrchardCore.OpenId/Access/Authorize";
            }
            if (settings.EnableTokenEndpoint)
            {
                options.TokenEndpointPath = "/OrchardCore.OpenId/Access/Token";
            }
            if (settings.EnableLogoutEndpoint)
            {
                options.LogoutEndpointPath = "/OrchardCore.OpenId/Access/Logout";
            }
            if (settings.EnableUserInfoEndpoint)
            {
                options.UserinfoEndpointPath = "/OrchardCore.OpenId/UserInfo/Me";
            }
            if (settings.AllowAuthorizationCodeFlow)
            {
                options.GrantTypes.Add(OpenIdConnectConstants.GrantTypes.AuthorizationCode);
            }
            if (settings.AllowClientCredentialsFlow)
            {
                options.GrantTypes.Add(OpenIdConnectConstants.GrantTypes.ClientCredentials);
            }
            if (settings.AllowImplicitFlow)
            {
                options.GrantTypes.Add(OpenIdConnectConstants.GrantTypes.Implicit);
            }
            if (settings.AllowPasswordFlow)
            {
                options.GrantTypes.Add(OpenIdConnectConstants.GrantTypes.Password);
            }
            if (settings.AllowRefreshTokenFlow)
            {
                options.GrantTypes.Add(OpenIdConnectConstants.GrantTypes.RefreshToken);
            }

            options.Scopes.Add(OpenIdConnectConstants.Scopes.Email);
            options.Scopes.Add(OpenIdConnectConstants.Scopes.Phone);
            options.Scopes.Add(OpenIdConnectConstants.Scopes.Profile);
            options.Scopes.Add(OpenIddictConstants.Claims.Roles);
        }

        public void Configure(OpenIddictServerOptions options) => Debug.Fail("This infrastructure method shouldn't be called.");

        public void Configure(string name, JwtBearerOptions options)
        {
            // Ignore JWT handler instances that don't correspond to the private instance managed by the OpenID module.
            if (!string.Equals(name, OpenIdConstants.Schemes.Userinfo, StringComparison.Ordinal))
            {
                return;
            }

            var settings = GetServerSettingsAsync().GetAwaiter().GetResult();
            if (settings == null)
            {
                return;
            }

            options.TokenValidationParameters.ValidAudience = OpenIdConstants.Prefixes.Tenant + _shellSettings.Name;
            options.TokenValidationParameters.IssuerSigningKeys = _serverService.GetSigningKeysAsync().GetAwaiter().GetResult();

            // If an authority was explicitly set in the OpenID server options,
            // prefer it to the dynamic tenant comparison as it's more efficient.
            if (!string.IsNullOrEmpty(settings.Authority))
            {
                options.TokenValidationParameters.ValidIssuer = settings.Authority;
            }
            else
            {
                options.TokenValidationParameters.IssuerValidator = (issuer, token, parameters) =>
                {
                    if (!Uri.TryCreate(issuer, UriKind.Absolute, out Uri uri))
                    {
                        throw new SecurityTokenInvalidIssuerException("The token issuer is not valid.");
                    }

                    var tenant = _runningShellTable.Match(uri.Authority, uri.AbsolutePath);
                    if (tenant == null || !string.Equals(tenant.Name, _shellSettings.Name, StringComparison.Ordinal))
                    {
                        throw new SecurityTokenInvalidIssuerException("The token issuer is not valid.");
                    }

                    return issuer;
                };
            }
        }

        public void Configure(JwtBearerOptions options) => Debug.Fail("This infrastructure method shouldn't be called.");

        public void Configure(string name, OpenIddictValidationOptions options)
        {
            // Ignore validation handler instances that don't correspond to the private instance managed by the OpenID module.
            if (!string.Equals(name, OpenIdConstants.Schemes.Userinfo, StringComparison.Ordinal))
            {
                return;
            }

            options.Audiences.Add(OpenIdConstants.Prefixes.Tenant + _shellSettings.Name);
        }

        public void Configure(OpenIddictValidationOptions options) => Debug.Fail("This infrastructure method shouldn't be called.");

        private async Task<OpenIdServerSettings> GetServerSettingsAsync()
        {
            var settings = await _serverService.GetSettingsAsync();
            if ((await _serverService.ValidateSettingsAsync(settings)).Any(result => result != ValidationResult.Success))
            {
                _logger.LogWarning("The OpenID Connect module is not correctly configured.");

                return null;
            }

            return settings;
        }
    }
}
