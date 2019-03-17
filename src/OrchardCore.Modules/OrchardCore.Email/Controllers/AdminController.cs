using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using OrchardCore.DisplayManagement;
using OrchardCore.DisplayManagement.ModelBinding;
using OrchardCore.DisplayManagement.Notify;
using OrchardCore.Email.Drivers;
using OrchardCore.Email.ViewModels;

namespace OrchardCore.Email.Controllers
{
    public class AdminController : Controller, IUpdateModel
    {
        private readonly IAuthorizationService _authorizationService;
        private readonly INotifier _notifier;
        private readonly ISmtpService _smtpService;
        private readonly IHtmlLocalizer H;

        public AdminController(
            IHtmlLocalizer<AdminController> h,
            IAuthorizationService authorizationService,
            INotifier notifier,
            IShapeFactory shapeFactory,
            ISmtpService smtpService,
            IStringLocalizer<AdminController> stringLocalizer)
        {
            H = h;
            _authorizationService = authorizationService;
            _notifier = notifier;
            _smtpService = smtpService;

            T = stringLocalizer;
        }

        IStringLocalizer T { get; set; }

        [HttpGet]
        [ActionName("Index")]
        public async Task<IActionResult> Get()
        {
            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageEmailSettings))
            {
                return Unauthorized();
            }

            return View();
        }

        [HttpPost]
        [ActionName("Index")]
        public async Task<IActionResult> Post(SmtpSettingsViewModel model)
        {
            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageEmailSettings))
            {
                return Unauthorized();
            }

            if (ModelState.IsValid)
            {
                var message = CreateMessageFromViewModel(model);

                if (ModelState.IsValid)
                {
                    // send email with DefaultSender
                    var result = await _smtpService.SendAsync(message);

                    if (!result.Succeeded)
                    {
                        foreach (var error in result.Errors)
                        {
                            ModelState.AddModelError("*", error.ToString());
                        }
                    }
                    else
                    {
                        _notifier.Success(H["Message sent successfully"]);

                        return Redirect(Url.Action("Index", "Admin", new { area = "OrchardCore.Settings", groupId = SmtpSettingsDisplayDriver.GroupId }));
                    }
                }
            }

            return View(model);
        }

        private MailMessage CreateMessageFromViewModel(SmtpSettingsViewModel testSettings)
        {
            var message = new MailMessage();

            foreach (var email in ParseEmailAddresses(testSettings.To))
            {
                if (ValidateEmail(email))
                {
                    message.To.Add(email);
                }
                else
                {
                    ModelState.AddModelError(string.Empty, T["Invalid \"To\" email : "] + email);
                }
            }

            foreach (var email in ParseEmailAddresses(testSettings.Bcc))
            {
                if (ValidateEmail(email))
                {
                    message.Bcc.Add(email);
                }
                else
                {
                    ModelState.AddModelError(string.Empty, T["Invalid \"Bcc\" email : "] + email);
                }
            }

            foreach (var email in ParseEmailAddresses(testSettings.Cc))
            {
                if (ValidateEmail(email))
                {
                    message.CC.Add(email);
                }
                else
                {
                    ModelState.AddModelError(string.Empty, T["Invalid \"Cc\" email : "] + email);
                }
            }

            foreach (var email in ParseEmailAddresses(testSettings.ReplyTo))
            {
                if (ValidateEmail(email))
                {
                    message.ReplyToList.Add(email);
                }
                else
                {
                    ModelState.AddModelError(string.Empty, T["Invalid \"Reply To\" email : "] + email);
                }
            }

            if (!String.IsNullOrWhiteSpace(testSettings.Subject))
            {
                message.Subject = testSettings.Subject;
            }

            if (!String.IsNullOrWhiteSpace(testSettings.Body))
            {
                message.Body = testSettings.Body;
            }

            return message;
        }

        private static IEnumerable<string> ParseEmailAddresses(string adresses)
        {
            if (String.IsNullOrWhiteSpace(adresses))
            {
                return Array.Empty<string>();
            }

            return adresses.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool ValidateEmail(string email)
        {
            var regexOptions = RegexOptions.Singleline | RegexOptions.IgnoreCase;
            // From https://stackoverflow.com/questions/16167983/best-regular-expression-for-email-validation-in-c-sharp
            // Retrieved 2018-11-16
            string pattern = @"\A(?:[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)\Z";

            return Regex.IsMatch(email, pattern, regexOptions);
        }
    }
}
