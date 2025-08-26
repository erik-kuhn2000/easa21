using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;


namespace QApp.Pages
{
    [Authorize(Policy = "AdminOnly")]
    public class ManagePersonnelModel : PageModel
    {
        // Model for a Personnel Entry
        public class PersonnelEntry
        {
            public string TGI { get; set; }
            public string Name { get; set; }
            [Required(ErrorMessage = "Role is required.")]
            public int? Role { get; set; }
        }

        public string LoggedInUserTGI { get; private set; }

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
        [TempData]
        public string SuccessMessage { get; set; }
        [TempData]
        public string ErrorMessage { get; set; }

        private readonly IConfiguration _configuration;
        private readonly ILogger<ManagePersonnelModel> _logger;

        public ManagePersonnelModel(IConfiguration configuration, ILogger<ManagePersonnelModel> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            LoadDropdowns();
            await LoadPersonnelAsync();
            LoggedInUserTGI = User.Identity?.Name;
        }

        public async Task<IActionResult> OnPostAsync(string handler)
        {
            if (handler == "add")
            {
                return await OnPostAddPersonnelAsync();
            }

            LoadDropdowns();
            await LoadPersonnelAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAddPersonnelAsync()
        {
            if (ModelState.IsValid && await PersonnelExistsAsync(NewPersonnel.TGI))
            {
                ModelState.AddModelError("NewPersonnel.TGI", "This TGI already exists.");
            }

            if (!ModelState.IsValid)
            {
                var allErrors = ModelState.Values.SelectMany(v => v.Errors);
                ErrorMessage = string.Join(" ", allErrors.Select(e => e.ErrorMessage));

                LoadDropdowns();
                await LoadPersonnelAsync();
                return Page();
            }

            try
            {
                if (await AddPersonnelAsync(NewPersonnel))
                {
                    await LogActionAsync("Add", NewPersonnel);
                    SuccessMessage = "User entry added successfully.";
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
            await LoadPersonnelAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostUpdatePersonnelAsync(string tgi, string name, int role)
        {
            if (string.Equals(tgi, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            {
                return new JsonResult(new { success = false, message = "Error: You cannot update your own account." });
            }
            try
            {
                // Validation
                var errors = new List<string>();
                if (string.IsNullOrWhiteSpace(name)) errors.Add("Name is required.");
                if (role == 0) errors.Add("Role is required.");

                if (errors.Any())
                {
                    return new JsonResult(new { success = false, message = string.Join(" ", errors) });
                }

                var currentPersonnel = await GetPersonnelDetailsAsync(tgi);
                if (currentPersonnel == null)
                {
                    return new JsonResult(new { success = false, message = "User not found." });
                }

                var personnelToUpdate = new PersonnelEntry { TGI = tgi, Name = name, Role = role };

                if (await UpdatePersonnelAsync(personnelToUpdate))
                {
                    await LogActionAsync("Update", personnelToUpdate, currentPersonnel);
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

        public async Task<IActionResult> OnPostDeletePersonnelAsync(string tgi)
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
                var personnelToDelete = await GetPersonnelDetailsAsync(tgi);
                if (personnelToDelete == null)
                {
                    return new JsonResult(new { success = false, message = "User entry not found." });
                }

                if (await DeletePersonnelAsync(personnelToDelete))
                {
                    await LogActionAsync("Delete", personnelToDelete);
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

        public async Task<IActionResult> OnGetPersonnelDetailsAsync(string tgi)
        {
            if (string.IsNullOrWhiteSpace(tgi))
            {
                return new JsonResult(new { success = false, message = "TGI is required." });
            }
            try
            {
                var personnel = await GetPersonnelDetailsAsync(tgi);
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

        private async Task LoadPersonnelAsync()
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            PersonnelList.Clear();
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    using (var countCommand = new SqlCommand("SELECT COUNT(*) FROM Users", connection))
                    {
                        TotalResults = (int)await countCommand.ExecuteScalarAsync();
                    }

                    int offset = (PageNumber - 1) * PageSize;
                    var sql = @"SELECT TGI, Name, Role FROM Users 
                                ORDER BY TGI 
                                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@Offset", offset);
                        command.Parameters.AddWithValue("@PageSize", PageSize);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
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

        private async Task<bool> PersonnelExistsAsync(string tgi)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var sql = "SELECT COUNT(*) FROM Users WHERE TGI = @TGI";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TGI", tgi);
                    var result = await command.ExecuteScalarAsync();
                    return (int)result > 0;
                }
            }
        }

        private async Task<bool> AddPersonnelAsync(PersonnelEntry personnel)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var sql = "INSERT INTO Users (TGI, Name, Role) VALUES (@TGI, @Name, @Role)";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TGI", personnel.TGI);
                    command.Parameters.AddWithValue("@Name", personnel.Name);
                    command.Parameters.AddWithValue("@Role", personnel.Role);
                    return await command.ExecuteNonQueryAsync() > 0;
                }
            }
        }

        private async Task<bool> UpdatePersonnelAsync(PersonnelEntry personnel)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var sql = "UPDATE Users SET Name = @Name, Role = @Role WHERE TGI = @TGI";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Name", personnel.Name);
                    command.Parameters.AddWithValue("@Role", personnel.Role);
                    command.Parameters.AddWithValue("@TGI", personnel.TGI);
                    return await command.ExecuteNonQueryAsync() > 0;
                }
            }
        }

        private async Task<bool> DeletePersonnelAsync(PersonnelEntry personnel)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var sql = "DELETE FROM Users WHERE TGI = @TGI";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TGI", personnel.TGI);
                    return await command.ExecuteNonQueryAsync() > 0;
                }
            }
        }

        private async Task<PersonnelEntry> GetPersonnelDetailsAsync(string tgi)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var sql = "SELECT TGI, Name, Role FROM Users WHERE TGI = @TGI";
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TGI", tgi);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
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

        private async Task LogActionAsync(string action, PersonnelEntry personnel, PersonnelEntry oldPersonnel = null)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    var logQuery = "INSERT INTO Log_Users (Action, Performed_By, Datetime, TGI, Name, Role) VALUES (@Action, @ID, @Time, @TGI, @Name, @Role)";
                    using (var cmd = new SqlCommand(logQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@Action", action);
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now);

                        // Simplified logging logic
                        cmd.Parameters.AddWithValue("@TGI", personnel.TGI);

                        if (action == "Update" && oldPersonnel != null)
                        {
                            cmd.Parameters.AddWithValue("@Name", oldPersonnel.Name != personnel.Name ? personnel.Name : (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Role", oldPersonnel.Role != personnel.Role ? personnel.Role : (object)DBNull.Value);
                        }
                        else if (action == "Add")
                        {
                            cmd.Parameters.AddWithValue("@Name", personnel.Name);
                            cmd.Parameters.AddWithValue("@Role", personnel.Role);
                        }
                        else // For Delete, log nulls for name/role
                        {
                            cmd.Parameters.AddWithValue("@Name", (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Role", (object)DBNull.Value);
                        }

                        await cmd.ExecuteNonQueryAsync();
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