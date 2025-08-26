using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data.SqlClient;

[Authorize(Policy = "AdminOnly")]

public class ManageAuthorisationModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ManageAuthorisationModel> _logger;

    public ManageAuthorisationModel(IConfiguration configuration, ILogger<ManageAuthorisationModel> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    // This property holds the current number for display on the page.
    public string CurrentAuthorisationNumber { get; set; } = "Not Set";

    // This property binds the new number from the user's input.
    [BindProperty]
    public string NewAuthorisationNumber { get; set; }

    // OnGetAsync runs when the page is first loaded.
    public async Task OnGetAsync()
    {
        await LoadCurrentAuthorisationNumber();
    }

    // The primary handler for the form post, which updates the number.
    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAuthorisationNumber))
        {
            return new JsonResult(new { success = false, message = "Authorisation number cannot be empty." });
        }

        try
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                using (SqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Step 1: Get the current number for logging before it's deleted.
                        string oldNumber = "Not Set";
                        string selectQuery = "SELECT TOP 1 AuthorisationNo FROM AuthorisationNumber";
                        await using (var selectCommand = new SqlCommand(selectQuery, connection, transaction))
                        {
                            var result = await selectCommand.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                oldNumber = result.ToString();
                            }
                        }

                        // Step 2: Delete all existing entries from the table.
                        string deleteQuery = "DELETE FROM AuthorisationNumber";
                        await using (var deleteCommand = new SqlCommand(deleteQuery, connection, transaction))
                        {
                            await deleteCommand.ExecuteNonQueryAsync();
                        }

                        // Step 3: Insert the new entry.
                        string insertQuery = "INSERT INTO AuthorisationNumber (AuthorisationNo) VALUES (@AuthorisationNo)";
                        await using (var insertCommand = new SqlCommand(insertQuery, connection, transaction))
                        {
                            insertCommand.Parameters.AddWithValue("@AuthorisationNo", NewAuthorisationNumber.Trim());
                            await insertCommand.ExecuteNonQueryAsync();
                        }

                        // Step 4: Log the action with the old number.
                        var logQuery = "INSERT INTO Log_AuthorisationNumber (Action, Performed_By, Datetime, AuthorisationNo) VALUES (@Action, @ID, @Time, @AuthorisationNo)";
                        await using (SqlCommand cmd = new SqlCommand(logQuery, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@Action", "Update");
                            cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                            cmd.Parameters.AddWithValue("@AuthorisationNo", oldNumber); // Log the number that was replaced
                            await cmd.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();
                    }
                    catch (Exception transactionEx)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(transactionEx, "Error during authorisation number update transaction.");
                        throw; // Re-throw to be caught by the outer catch block
                    }
                }
            }

            _logger.LogInformation($"Authorisation number updated to: {NewAuthorisationNumber.Trim()}");
            return new JsonResult(new { success = true, message = "Authorisation number updated successfully!", newNumber = NewAuthorisationNumber.Trim() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "General error updating authorisation number.");
            return new JsonResult(new { success = false, message = $"An error occurred: {ex.Message}" });
        }
    }

    // Private helper to load the current number from the database.
    private async Task LoadCurrentAuthorisationNumber()
    {
        try
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                string query = "SELECT TOP 1 AuthorisationNo FROM AuthorisationNumber";
                await using (var command = new SqlCommand(query, connection))
                {
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        CurrentAuthorisationNumber = result.ToString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching current authorisation number");
            CurrentAuthorisationNumber = "Error loading number.";
            ModelState.AddModelError(string.Empty, "Failed to load the current authorisation number from the database.");
        }
    }
}
