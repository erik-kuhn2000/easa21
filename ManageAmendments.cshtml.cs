using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data.SqlClient;


[Authorize(Policy = "AdminOnly")]
public class ManageAmendmentsModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ManageAmendmentsModel> _logger;

    public ManageAmendmentsModel(IConfiguration configuration, ILogger<ManageAmendmentsModel> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    // This property will hold the list of amendments for display.
    public List<string> Amendments { get; set; } = new List<string>();

    // This property binds the input from the "Add" form.
    [BindProperty]
    public string NewAmendment { get; set; }

    // This property binds the array of selected amendments for deletion.
    [BindProperty]
    public List<string> SelectedAmendments { get; set; } = new List<string>();

    // Runs when the page is first requested to populate the initial list.
    public void OnGet()
    {
        LoadAmendments();
    }

    // AJAX Handler: Returns the current list of amendments as JSON.
    public IActionResult OnGetList()
    {
        LoadAmendments();
        return new JsonResult(Amendments);
    }

    // AJAX Handler: Adds a new amendment.
    // Refactored AJAX Handler: Adds a new amendment.
    public async Task<IActionResult> OnPostAddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAmendment))
        {
            return new JsonResult(new { success = false, message = "Amendment cannot be empty." });
        }

        try
        {
            // Step 1: Perform the main database operation within a transaction for atomicity.
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using (var transaction = connection.BeginTransaction())
                {
                    // Check if amendment already exists to prevent duplicates
                    string checkQuery = "SELECT COUNT(*) FROM Amendments WHERE Amendment = @Amendment";
                    await using (var checkCommand = new SqlCommand(checkQuery, connection, transaction))
                    {
                        checkCommand.Parameters.AddWithValue("@Amendment", NewAmendment.Trim());
                        if ((int)await checkCommand.ExecuteScalarAsync() > 0)
                        {
                            return new JsonResult(new { success = false, message = "Amendment already exists." });
                        }
                    }

                    // Insert the new amendment
                    string insertQuery = "INSERT INTO Amendments (Amendment) VALUES (@Amendment)";
                    await using (var command = new SqlCommand(insertQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Amendment", NewAmendment.Trim());
                        await command.ExecuteNonQueryAsync();
                    }

                    // Commit the transaction if the check and insert operations are successful.
                    await transaction.CommitAsync();
                }
            }

            // Step 2: After the main operation is successful, log the action.
            // This is a separate database call as per the provided LogActionAsync method.
            await LogActionAsync("Add", NewAmendment);

            _logger.LogInformation("Successfully added new amendment: {Amendment}", NewAmendment);
            return new JsonResult(new
            {
                success = true,
                message = "Successfully added amendment."
            });
        }
        catch (Exception ex)
        {
            // This will catch any errors from the main transaction (check/insert).
            _logger.LogError(ex, "Error adding amendment: {Amendment}", NewAmendment);
            return new JsonResult(new { success = false, message = "An error occurred while adding the amendment." });
        }
    }

    // AJAX Handler: Deletes selected amendments.
    // Refactored AJAX Handler: Deletes selected amendments.
    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (SelectedAmendments == null || !SelectedAmendments.Any())
        {
            return new JsonResult(new { success = false, message = "No amendments selected for deletion." });
        }

        string connectionString = _configuration.GetConnectionString("SQLConnection");
        try
        {
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Iterate through each selected amendment to delete it and log the action
                foreach (var amendment in SelectedAmendments)
                {
                    // Use a transaction to ensure the delete and log operations succeed or fail together.
                    await using (var transaction = connection.BeginTransaction())
                    {
                        // 1. Delete the item
                        string query = "DELETE FROM Amendments WHERE Amendment = @Amendment";
                        await using (var command = new SqlCommand(query, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Amendment", amendment);
                            await command.ExecuteNonQueryAsync();
                        }

                        // 2. Log the deletion action (using the corrected helper method)
                        // Note: This logging call is now inside the loop.
                        await LogActionAsync("Delete", amendment);

                        // If both operations were successful, commit the transaction
                        transaction.Commit();
                    }
                }
            }

            _logger.LogInformation($"Deleted {SelectedAmendments.Count} amendments");
            return new JsonResult(new
            {
                success = true,
                message = $"Successfully deleted {SelectedAmendments.Count} amendment(s)."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting amendments");
            return new JsonResult(new { success = false, message = "An error occurred while deleting amendments." });
        }
    }
    // Private helper method to load amendment data from the database.
    private void LoadAmendments()
    {
        try
        {
            Amendments.Clear();
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT Amendment FROM Amendments ORDER BY Amendment";
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Amendments.Add(reader["Amendment"].ToString());
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading amendments");
            Amendments = new List<string>();
            ModelState.AddModelError(string.Empty, "Could not load amendments from the database.");
        }
    }

    private async Task LogActionAsync(string action, string name)
    {
        try
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var logQuery = "INSERT INTO Log_Amendments (Action, Performed_By, Datetime, Amendment) VALUES (@Action, @ID, @Time, @Amendment)";
                await using (var cmd = new SqlCommand(logQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Action", action);
                    cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@Amendment", name.Trim());
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch (Exception ex)
        {
            // Log that the logging action itself failed
            _logger.LogError(ex, "Failed to log admin action '{Action}' for item '{Name}'", action, name);
        }
    }
}