using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Castle.Core.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchardCore.Modules;
using OrchardCore.ReCaptcha.ActionFilters.Abuse;
using OrchardCore.ReCaptcha.Configuration;
using OrchardCore.ReCaptcha.Services;

namespace OrchardCore.ReCaptcha.ActionFilters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class ValidateReCaptchaAttribute : ActionFilterAttribute
    {
        private readonly ReCaptchaMode _mode;


        public ValidateReCaptchaAttribute(ReCaptchaMode mode = ReCaptchaMode.AlwaysShow)
        {
            _mode = mode;
        }

        public override async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            var recaptchaService = context.HttpContext.RequestServices.GetService<ReCaptchaService>();
            var T = context.HttpContext.RequestServices.GetService<IStringLocalizer<ReCaptchaService>>();
            var isValidCaptcha = false;
            var reCaptchaResponse = context.HttpContext.Request?.Form?[Constants.ReCaptchaServerResponseHeaderName].ToString();

            if (!String.IsNullOrWhiteSpace(reCaptchaResponse))
                isValidCaptcha = await recaptchaService.VerifyCaptchaResponseAsync(reCaptchaResponse);

            var isRobot = false;

            switch (_mode)
            {
                case ReCaptchaMode.PreventRobots:
                    isRobot = recaptchaService.IsThisARobot();
                    break;
                case ReCaptchaMode.AlwaysShow:
                    isRobot = true;
                    break;

            }

            if (isRobot && !isValidCaptcha)
            {
                context.ModelState.AddModelError("ReCaptcha", T["Failed to validate captcha"]);
            }

            await next();

            if (context.ModelState.IsValid)
            {
                recaptchaService.ThisIsAHuman();
            }
            else
            {
                recaptchaService.MaybeThisIsARobot();
            }

            return;
        }
    }
}
