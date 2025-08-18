using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QApp.Pages
{
    [Authorize(Policy = "AdminOnly")]
    public class ManagePrefixesModel : PageModel
    {
        public class Prefix
        {
            public string Year { get; set; }
            public string Code { get; set; }
        }

        [BindProperty]
        public Prefix NewPrefix { get; set; } = new Prefix();

        public List<Prefix> Prefixes { get; set; } = new List<Prefix>();

        public string SuccessMessage { get; set; }
        public string ErrorMessage { get; set; }

        private readonly IConfiguration _configuration;
        private readonly ILogger<ManagePrefixesModel> _logger;

        public ManagePrefixesModel(IConfiguration configuration, ILogger<ManagePrefixesModel> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public void OnGet()
        {
            // Check TempData for a success message from the redirect
            if (TempData["SuccessMessage"] != null)
            {
                SuccessMessage = TempData["SuccessMessage"].ToString();
            }
            LoadPrefixes();
        }

        public IActionResult OnPost(string handler)
        {
            if (handler == "add")
            {
                return OnPostAddPrefix();
            }
            LoadPrefixes();
            return Page();
        }

        public IActionResult OnPostAddPrefix()
        {
            var errors = new List<string>();

            // Validation (this part is unchanged)
            if (string.IsNullOrWhiteSpace(NewPrefix.Year))
                errors.Add("Year is required.");
            if (string.IsNullOrWhiteSpace(NewPrefix.Code))
                errors.Add("Code is required.");

            if (!string.IsNullOrWhiteSpace(NewPrefix.Year) && PrefixExists(NewPrefix.Year))
            {
                errors.Add("Year already exists.");
            }

            // If there are errors, return the page to show them (unchanged)
            if (errors.Any())
            {
                ErrorMessage = string.Join(" ", errors);
                LoadPrefixes();
                return Page();
            }

            try
            {
                bool success = AddPrefix(NewPrefix);
                if (success)
                {
                    LogAddAction(NewPrefix);

                    // --- CHANGE IS HERE ---
                    // Store the success message in TempData, which survives a redirect.
                    TempData["SuccessMessage"] = "Prefix added successfully.";
                    // Redirect to the page using a GET request.
                    return RedirectToPage();
                    // --- END OF CHANGE ---
                }
                else
                {
                    ErrorMessage = "Failed to add Prefix.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding prefix for year {Year}", NewPrefix.Year);
                ErrorMessage = $"Error adding Prefix: {ex.Message}";
            }

            LoadPrefixes();
            return Page();
        }

        public async Task<IActionResult> OnPostUpdatePrefix(string year, string code)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    return new JsonResult(new { success = false, message = "Code is required." });
                }

                var currentPrefix = GetPrefixDetails(year);
                if (currentPrefix == null)
                {
                    return new JsonResult(new { success = false, message = "Prefix not found." });
                }

                var prefixToUpdate = new Prefix { Year = year, Code = code };
                var success = UpdatePrefix(prefixToUpdate);

                if (success)
                {
                    LogUpdateAction(prefixToUpdate, currentPrefix);
                    return new JsonResult(new { success = true, message = "Prefix updated successfully." });
                }
                else
                {
                    return new JsonResult(new { success = false, message = "Failed to update Prefix." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating prefix for year {Year}", year);
                return new JsonResult(new { success = false, message = $"Update error: {ex.Message}" });
            }
        }

        public async Task<IActionResult> OnPostDeletePrefix(string year)
        {
            if (string.IsNullOrWhiteSpace(year))
            {
                return new JsonResult(new { success = false, message = "Year is required." });
            }

            try
            {
                var prefixToDelete = GetPrefixDetails(year);
                if (prefixToDelete == null)
                {
                    return new JsonResult(new { success = false, message = "Prefix not found." });
                }

                var success = DeletePrefix(prefixToDelete);
                if (success)
                {
                    LogDeleteAction(prefixToDelete);
                    return new JsonResult(new { success = true, message = "Prefix deleted successfully." });
                }
                else
                {
                    return new JsonResult(new { success = false, message = "Failed to delete Prefix." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting prefix for year {Year}", year);
                return new JsonResult(new { success = false, message = $"Delete error: {ex.Message}" });
            }
        }

        public IActionResult OnGetPrefixDetails(string year)
        {
            if (string.IsNullOrWhiteSpace(year))
            {
                return new JsonResult(new { success = false, message = "Year is required." });
            }

            try
            {
                var prefix = GetPrefixDetails(year);
                if (prefix == null)
                {
                    return new JsonResult(new { success = false, message = "Prefix not found." });
                }
                return new JsonResult(new { success = true, data = prefix });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"Database error: {ex.Message}" });
            }
        }

        private void LoadPrefixes()
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var selectSql = "SELECT Year, Code FROM Prefixes ORDER BY Year DESC";
                    using (SqlCommand selectCommand = new SqlCommand(selectSql, connection))
                    {
                        using (SqlDataReader reader = selectCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Prefixes.Add(new Prefix
                                {
                                    Year = reader["Year"]?.ToString() ?? "",
                                    Code = reader["Code"]?.ToString() ?? ""
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading prefixes");
                ErrorMessage = "Error loading prefixes.";
            }
        }

        private bool PrefixExists(string year)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sql = "SELECT COUNT(*) FROM Prefixes WHERE Year = @Year";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@Year", year);
                        int count = Convert.ToInt32(command.ExecuteScalar());
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if prefix exists for year {Year}", year);
                throw;
            }
        }

        private bool AddPrefix(Prefix prefix)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sql = "INSERT INTO Prefixes (Year, Code) VALUES (@Year, @Code)";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@Year", prefix.Year ?? "");
                        command.Parameters.AddWithValue("@Code", prefix.Code ?? "");
                        int rowsAffected = command.ExecuteNonQuery();
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding prefix {Year} to database", prefix.Year);
                throw;
            }
        }

        private bool UpdatePrefix(Prefix prefix)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sql = "UPDATE Prefixes SET Code = @Code WHERE Year = @Year";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@Year", prefix.Year ?? "");
                        command.Parameters.AddWithValue("@Code", prefix.Code ?? "");
                        int rowsAffected = command.ExecuteNonQuery();
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating prefix {Year} in database", prefix.Year);
                throw;
            }
        }

        private bool DeletePrefix(Prefix prefix)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sql = "DELETE FROM Prefixes WHERE Year = @Year";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@Year", prefix.Year ?? "");
                        int rowsAffected = command.ExecuteNonQuery();
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting prefix {Year} from database", prefix.Year);
                throw;
            }
        }

        private Prefix GetPrefixDetails(string year)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sql = "SELECT Year, Code FROM Prefixes WHERE Year = @Year";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@Year", year);
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return new Prefix
                                {
                                    Year = reader["Year"]?.ToString() ?? "",
                                    Code = reader["Code"]?.ToString() ?? ""
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting prefix details for {Year}", year);
                throw;
            }
            return null;
        }

        private void LogAddAction(Prefix prefix)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    var logquery = "INSERT INTO Log_Prefixes (Action, Performed_By, Datetime, Year, Prefix) VALUES (@Action, @ID, @Time, @Year, @Code)";
                    using (SqlCommand cmd = new SqlCommand(logquery, conn))
                    {
                        cmd.Parameters.AddWithValue("@Action", "Add");
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@Year", prefix.Year ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Code", prefix.Code ?? (object)DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging add action for prefix {Year}", prefix.Year);
            }
        }

        private void LogUpdateAction(Prefix newPrefix, Prefix currentPrefix)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    var logquery = "INSERT INTO Log_Prefixes (Action, Performed_By, Datetime, Year, Prefix) VALUES (@Action, @ID, @Time, @Year, @Code)";
                    using (SqlCommand cmd = new SqlCommand(logquery, conn))
                    {
                        cmd.Parameters.AddWithValue("@Action", "Update");
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@Year", newPrefix.Year ?? (object)DBNull.Value);

                        // Only log the code if it changed
                        if (!string.Equals(currentPrefix.Code?.Trim(), newPrefix.Code?.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            cmd.Parameters.AddWithValue("@Code", newPrefix.Code ?? (object)DBNull.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@Code", (object)DBNull.Value);
                        }
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging update action for prefix {Year}", newPrefix.Year);
            }
        }

        private void LogDeleteAction(Prefix prefix)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    var logquery = "INSERT INTO Log_Prefixes (Action, Performed_By, Datetime, Year) VALUES (@Action, @ID, @Time, @Year)";
                    using (SqlCommand cmd = new SqlCommand(logquery, conn))
                    {
                        cmd.Parameters.AddWithValue("@Action", "Delete");
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@Year", prefix.Year ?? (object)DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging delete action for prefix {Year}", prefix.Year);
            }
        }
    }
}