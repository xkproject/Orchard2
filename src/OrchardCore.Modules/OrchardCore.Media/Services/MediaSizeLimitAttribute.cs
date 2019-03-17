using System;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Environment.Shell.Configuration;

namespace OrchardCore.Media.Services
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    [Obsolete("This type is being deprecated because of GH/#3263")]
    public class MediaSizeLimitAttribute : Attribute, IFilterFactory, IOrderedFilter
    {
        public int Order { get; set; } = 900;

        /// <inheritdoc />
        public bool IsReusable => true;

        /// <inheritdoc />
        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetRequiredService<IShellConfiguration>();
            var section = configuration.GetSection("OrchardCore.Media");

            var maxUploadSize = section.GetValue("MaxRequestBodySize", 100_000_000);
            var maxFileSize = section.GetValue("MaxFileSize", 30_000_000);

            return new InternalMediaSizeFilter(maxUploadSize, maxFileSize);
        }

        private class InternalMediaSizeFilter : IAuthorizationFilter, IRequestFormLimitsPolicy
        {
            private readonly long _maxUploadSize;
            private readonly long _maxFileSize;

            public InternalMediaSizeFilter(long maxUploadSize, long maxFileSize)
            {
                _maxUploadSize = maxUploadSize;
                _maxFileSize = maxFileSize;
            }

            public void OnAuthorization(AuthorizationFilterContext context)
            {
                if (context == null)
                {
                    throw new ArgumentNullException(nameof(context));
                }

                var effectiveFormPolicy = context.FindEffectivePolicy<IRequestFormLimitsPolicy>();
                if (effectiveFormPolicy == null || effectiveFormPolicy == this)
                {

                    var features = context.HttpContext.Features;
                    var formFeature = features.Get<IFormFeature>();

                    if (formFeature == null || formFeature.Form == null)
                    {
                        // Request form has not been read yet, so set the limits
                        var formOptions = new FormOptions
                        {
                            MultipartBodyLengthLimit = _maxFileSize
                        };

                        features.Set<IFormFeature>(new FormFeature(context.HttpContext.Request, formOptions));
                    }
                }

                var effectiveRequestSizePolicy = context.FindEffectivePolicy<IRequestSizePolicy>();
                if (effectiveRequestSizePolicy == null || effectiveRequestSizePolicy == this)
                {

                    var maxRequestBodySizeFeature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();

                    if (maxRequestBodySizeFeature != null && maxRequestBodySizeFeature.IsReadOnly)
                    {
                        maxRequestBodySizeFeature.MaxRequestBodySize = _maxUploadSize;
                    }
                }
            }
        }
    }
}
