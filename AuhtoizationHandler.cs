using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;

using QApp.Pages.Authorization;

namespace QApp.Pages.Authorization
{
    public class AdminAuthorizationHandler : AuthorizationHandler<AdminRequirement>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AdminAuthorizationHandler> _logger;

        public AdminAuthorizationHandler(IConfiguration configuration, ILogger<AdminAuthorizationHandler> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
        {
            if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                context.Fail();
                return Task.CompletedTask;
            }

            string userTGI = context.User.Identity.Name;

            if (IsUserAdmin(userTGI))
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }

            return Task.CompletedTask;
        }

        private bool IsUserAdmin(string tgi)
        {
            const string sql = "SELECT 1 FROM Users WHERE TGI = @TGI AND Role = @AdminRole";

            try
            {
                using var connection = new SqlConnection(_configuration.GetConnectionString("SQLConnection"));
                connection.Open();
                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TGI", tgi);
                command.Parameters.AddWithValue("@AdminRole", (int)UserRole.Admin);

                return command.ExecuteScalar() != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking admin status for user {TGI}", tgi);
                return false;
            }
        }
    }

    // This is your original handler for checking a specific certificate.
    // Make sure it looks like this again.
    public class SignatoryAuthorizationHandler : AuthorizationHandler<SignatoryRequirement, string>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SignatoryAuthorizationHandler> _logger;

        public SignatoryAuthorizationHandler(IConfiguration configuration, ILogger<SignatoryAuthorizationHandler> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SignatoryRequirement requirement, string certNo)
        {
            if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                context.Fail();
                return Task.CompletedTask;
            }

            string userTGI = context.User.Identity.Name;

            if (IsUserSignatoryForCertificate(userTGI, certNo))
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }

            return Task.CompletedTask;
        }

        private bool IsUserSignatoryForCertificate(string tgi, string certNo)
        {
            const string sql = @"
            SELECT 1 FROM Certificates d
            INNER JOIN Users u ON d.Signatory = u.Name
            WHERE u.TGI = @TGI 
            AND u.Role = @SignatoryRole 
            AND d.CertNo = @CertNo";

            try
            {
                using var connection = new SqlConnection(_configuration.GetConnectionString("SQLConnection"));
                connection.Open();
                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TGI", tgi);
                command.Parameters.AddWithValue("@SignatoryRole", (int)UserRole.Signatory);
                command.Parameters.AddWithValue("@CertNo", certNo);

                return command.ExecuteScalar() != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking signatory access for user {TGI} on certificate {CertNo}", tgi, certNo);
                return false;
            }
        }
    }

    // Add this new requirement to your Authorization.cs file
    public class HasSignatoryRoleRequirement : IAuthorizationRequirement { }

    // Add this new handler to your Authorization.cs file
    public class HasSignatoryRoleHandler : AuthorizationHandler<HasSignatoryRoleRequirement>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<HasSignatoryRoleHandler> _logger;

        public HasSignatoryRoleHandler(IConfiguration configuration, ILogger<HasSignatoryRoleHandler> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, HasSignatoryRoleRequirement requirement)
        {
            if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                context.Fail();
                return Task.CompletedTask;
            }

            string userTGI = context.User.Identity.Name;

            if (IsUserSignatory(userTGI))
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }

            return Task.CompletedTask;
        }

        private bool IsUserSignatory(string tgi)
        {
            const string sql = "SELECT 1 FROM Users WHERE TGI = @TGI AND Role = @SignatoryRole";

            try
            {
                using var connection = new SqlConnection(_configuration.GetConnectionString("SQLConnection"));
                connection.Open();
                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TGI", tgi);
                command.Parameters.AddWithValue("@SignatoryRole", (int)UserRole.Signatory); // Assumes UserRole is an enum

                return command.ExecuteScalar() != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking signatory role for user {TGI}", tgi);
                return false;
            }
        }
    }

    public class AuthenticatedUserAuthorizationHandler : AuthorizationHandler<AuthenticatedUserRequirement>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthenticatedUserAuthorizationHandler> _logger;

        public AuthenticatedUserAuthorizationHandler(IConfiguration configuration, ILogger<AuthenticatedUserAuthorizationHandler> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuthenticatedUserRequirement requirement)
        {
            if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                context.Fail();
                return Task.CompletedTask;
            }

            string userTGI = context.User.Identity.Name;

            if (IsUserInSystem(userTGI))
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }

            return Task.CompletedTask;
        }

        private bool IsUserInSystem(string tgi)
        {
            const string sql = "SELECT 1 FROM Users WHERE TGI = @TGI";

            try
            {
                using var connection = new SqlConnection(_configuration.GetConnectionString("SQLConnection"));
                connection.Open();
                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TGI", tgi);

                return command.ExecuteScalar() != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking user existence for {TGI}", tgi);
                return false;
            }
        }
    }
}