using System.Collections.Generic;
using OrchardCore.Security.Permissions;

namespace OrchardCore.Twitter
{
    public class Permissions : IPermissionProvider
    {
        public static readonly Permission ManageTwitterSignin
            = new Permission(nameof(ManageTwitterSignin), "Manage Sign in with Twitter settings");

        public IEnumerable<Permission> GetPermissions()
        {
            yield return ManageTwitterSignin;
        }

        public IEnumerable<PermissionStereotype> GetDefaultStereotypes()
        {
            yield return new PermissionStereotype
            {
                Name = "Administrator",
                Permissions = new[]
                {
                    ManageTwitterSignin
                }
            };
        }
    }
}
