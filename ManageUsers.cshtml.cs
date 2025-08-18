using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace QApp.Pages
{
    [Authorize(Policy = "AdminOnly")]
    public class ManagePersonnelModel : PageModel
    {
        // Model for a Personnel Entry

        public string LoggedInUserTGI { get; private set; }
        public class PersonnelEntry
        {
            public string TGI { get; set; }
            public string Name { get; set; }
            [Required(ErrorMessage = "Role is required.")]
            public int? Role { get; set; }
        }

        [BindProperty]
        public PersonnelEntry NewPersonnel { get; set; } = new PersonnelEntry();
        public List<PersonnelEntry> PersonnelList { get; set; } = new List<PersonnelEntry>();
        public List<SelectListItem> RoleList { get; set; }

        // Pagination Properties
        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalResults { get; set; } = 0;
        public int TotalPages => (int)Math.Ceiling((double)TotalResults / PageSize);

        // Message Properties
        public string SuccessMessage { get; set; }
        public string ErrorMessage { get; set; }

        private readonly IConfiguration _configuration;
        private readonly ILogger<ManagePersonnelModel> _logger;

        public ManagePersonnelModel(IConfiguration configuration, ILogger<ManagePersonnelModel> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public void OnGet()
        {
            if (TempData["SuccessMessage"] is string successMessage)
            {
                SuccessMessage = successMessage;
            }
            LoadDropdowns();
            LoadPersonnel();
            LoggedInUserTGI = User.Identity?.Name;
        }

        public IActionResult OnPost(string handler)
        {
            if (handler == "add")
            {
                return OnPostAddPersonnel();
            }

            LoadDropdowns();
            LoadPersonnel();
            return Page();
        }

        public IActionResult OnPostAddPersonnel()
        {
            // The framework has already validated the [Required] and [Range]
            // attributes and populated ModelState before this code runs.

            // We only need to add our custom database validation.
            // Check if the TGI exists, but only if the rest of the model is valid so far.
            if (ModelState.IsValid && PersonnelExists(NewPersonnel.TGI))
            {
                ModelState.AddModelError("NewPersonnel.TGI", "This TGI already exists.");
            }

            // Now, check the final state of the model.
            if (!ModelState.IsValid)
            {
                // This will now work correctly with no duplicates.
                var allErrors = ModelState.Values.SelectMany(v => v.Errors);
                ErrorMessage = string.Join(" ", allErrors.Select(e => e.ErrorMessage));

                LoadDropdowns();
                LoadPersonnel();
                return Page();
            }

            try
            {
                if (AddPersonnel(NewPersonnel))
                {
                    LogAction("Add", NewPersonnel);
                    
                    TempData["SuccessMessage"] = "User entry added successfully.";
                    return RedirectToPage();
                }
                else
                {
                    ErrorMessage = "Failed to add personnel entry to the database.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding personnel {TGI}", NewPersonnel.TGI);
                ErrorMessage = $"Error adding personnel: {ex.Message}";
            }

            LoadDropdowns();
            LoadPersonnel();
            return Page();
        }

        public async Task<IActionResult> OnPostUpdatePersonnel(string tgi, string name, int role)
        {
            if (string.Equals(tgi, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            {
                return new JsonResult(new { success = false, message = "Error: You cannot update your own account." });
            }
            try
            {
                var errors = new List<string>();
                if (string.IsNullOrWhiteSpace(name)) errors.Add("Name is required.");
                if (role == 0) errors.Add("Role is required.");

                if (errors.Any())
                {
                    return new JsonResult(new { success = false, message = string.Join(" ", errors) });
                }

                var currentPersonnel = GetPersonnelDetails(tgi);
                if (currentPersonnel == null)
                {
                    return new JsonResult(new { success = false, message = "User not found." });
                }

                var personnelToUpdate = new PersonnelEntry { TGI = tgi, Name = name, Role = role };

                if (UpdatePersonnel(personnelToUpdate))
                {
                    LogAction("Update", personnelToUpdate, currentPersonnel);
                    return new JsonResult(new { success = true, message = "User entry updated successfully." });
                }
                else
                {
                    return new JsonResult(new { success = false, message = "Failed to update user entry." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user {TGI}", tgi);
                return new JsonResult(new { success = false, message = $"Update error: {ex.Message}" });
            }
        }

        public async Task<IActionResult> OnPostDeletePersonnel(string tgi)
        {
            if (string.Equals(tgi, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            {
                return new JsonResult(new { success = false, message = "Error: You cannot delete your own account." });
            }
            if (string.IsNullOrWhiteSpace(tgi))
            {
                return new JsonResult(new { success = false, message = "TGI is required." });
            }

            try
            {
                var personnelToDelete = GetPersonnelDetails(tgi);
                if (personnelToDelete == null)
                {
                    return new JsonResult(new { success = false, message = "User entry not found." });
                }

                if (DeletePersonnel(personnelToDelete))
                {
                    LogAction("Delete", personnelToDelete);
                    return new JsonResult(new { success = true, message = "User entry deleted successfully." });
                }
                else
                {
                    return new JsonResult(new { success = false, message = "Failed to delete user entry." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user {TGI}", tgi);
                return new JsonResult(new { success = false, message = $"Delete error: {ex.Message}" });
            }
        }

        public IActionResult OnGetPersonnelDetails(string tgi)
        {
            if (string.IsNullOrWhiteSpace(tgi))
            {
                return new JsonResult(new { success = false, message = "TGI is required." });
            }
            try
            {
                var personnel = GetPersonnelDetails(tgi);
                if (personnel == null)
                {
                    return new JsonResult(new { success = false, message = "User not found." });
                }
                return new JsonResult(new { success = true, data = personnel });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user details for {TGI}", tgi);
                return new JsonResult(new { success = false, message = $"Database error: {ex.Message}" });
            }
        }

        private void LoadDropdowns()
        {
            RoleList = new List<SelectListItem>
            {
                new SelectListItem { Value = "1", Text = "Admin" },
                new SelectListItem { Value = "2", Text = "Signatory" }
            };
        }

        private void LoadPersonnel()
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var countCommand = new SqlCommand("SELECT COUNT(*) FROM Users", connection))
                    {
                        TotalResults = (int)countCommand.ExecuteScalar();
                    }

                    int offset = (PageNumber - 1) * PageSize;
                    var sql = @"SELECT TGI, Name, Role FROM Users 
                                ORDER BY TGI 
                                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@Offset", offset);
                        command.Parameters.AddWithValue("@PageSize", PageSize);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                PersonnelList.Add(new PersonnelEntry
                                {
                                    TGI = reader["TGI"].ToString(),
                                    Name = reader["Name"].ToString(),
                                    Role = Convert.ToInt32(reader["Role"])
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading users list");
                ErrorMessage = "Error loading users list.";
            }
        }

        private bool PersonnelExists(string tgi)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = "SELECT COUNT(*) FROM Users WHERE TGI = @TGI";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TGI", tgi);
                    return (int)command.ExecuteScalar() > 0;
                }
            }
        }

        private bool AddPersonnel(PersonnelEntry personnel)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = "INSERT INTO Users (TGI, Name, Role) VALUES (@TGI, @Name, @Role)";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TGI", personnel.TGI);
                    command.Parameters.AddWithValue("@Name", personnel.Name);
                    command.Parameters.AddWithValue("@Role", personnel.Role);
                    return command.ExecuteNonQuery() > 0;
                }
            }
        }

        private bool UpdatePersonnel(PersonnelEntry personnel)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = "UPDATE Users SET Name = @Name, Role = @Role WHERE TGI = @TGI";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Name", personnel.Name);
                    command.Parameters.AddWithValue("@Role", personnel.Role);
                    command.Parameters.AddWithValue("@TGI", personnel.TGI);
                    return command.ExecuteNonQuery() > 0;
                }
            }
        }

        private bool DeletePersonnel(PersonnelEntry personnel)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = "DELETE FROM Users WHERE TGI = @TGI";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TGI", personnel.TGI);
                    return command.ExecuteNonQuery() > 0;
                }
            }
        }

        private PersonnelEntry GetPersonnelDetails(string tgi)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = "SELECT TGI, Name, Role FROM Users WHERE TGI = @TGI";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TGI", tgi);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new PersonnelEntry
                            {
                                TGI = reader["TGI"].ToString(),
                                Name = reader["Name"].ToString(),
                                Role = Convert.ToInt32(reader["Role"])
                            };
                        }
                    }
                }
            }
            return null;
        }

        private void LogAction(string action, PersonnelEntry personnel, PersonnelEntry oldPersonnel = null)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    var logQuery = "INSERT INTO Log_Users (Action, Performed_By, Datetime, TGI, Name, Role) VALUES (@Action, @ID, @Time, @TGI, @Name, @Role)";
                    using (var cmd = new SqlCommand(logQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@Action", action);
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                        

                        if (action == "Add")
                        {
                            cmd.Parameters.AddWithValue("@Name", personnel.Name);
                            cmd.Parameters.AddWithValue("@Role", personnel.Role);
                            cmd.Parameters.AddWithValue("@TGI", personnel.TGI);
                        }
                        else if (action == "Delete")
                        {
                            cmd.Parameters.AddWithValue("@Name", (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Role", (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@TGI", (object)DBNull.Value);
                        }
                        else if (action == "Update" && oldPersonnel != null)
                        {
                            cmd.Parameters.AddWithValue("@Name", oldPersonnel.Name != personnel.Name ? personnel.Name : (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Role", oldPersonnel.Role != personnel.Role ? personnel.Role : (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@TGI", (object)DBNull.Value);
                        }

                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging {Action} action for user {TGI}", action, personnel.TGI);
            }
        }
    }
}