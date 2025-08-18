using Microsoft.AspNetCore.Authorization;

namespace QApp.Pages.Authorization
{
    // Requirement for admin-only access
    public class AdminRequirement : IAuthorizationRequirement
    {
    }

        // Requirement for signatory access to specific certificates
        public class SignatoryRequirement : IAuthorizationRequirement
        {
        }

        // Requirement for any authenticated user in the system
        public class AuthenticatedUserRequirement : IAuthorizationRequirement
        {
        }
   
}