using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QApp.Pages.Authorization;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using static QApp.Pages.ManagePrefixesModel;

[Authorize(Policy = "AdminOnly")]
public class ManageStatusesModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ManageStatusesModel> _logger;

    public ManageStatusesModel(IConfiguration configuration, ILogger<ManageStatusesModel> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    // This property will hold the list of statuses for display.
    public List<string> Statuses { get; set; } = new List<string>();

    // This property binds the input from the "Add" form.
    [BindProperty]
    public string NewStatus { get; set; }

    // This property binds the array of selected statuses for deletion.
    [BindProperty]
    public List<string> SelectedStatuses { get; set; } = new List<string>();

    // Runs when the page is first requested to populate the initial list.
    public void OnGet()
    {
        LoadStatuses();
    }

    // AJAX Handler: Returns the current list of statuses as JSON.
    public IActionResult OnGetList()
    {
        LoadStatuses();
        return new JsonResult(Statuses);
    }

    // AJAX Handler: Adds a new status.
    public async Task<IActionResult> OnPostAddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewStatus))
        {
            return new JsonResult(new { success = false, message = "Status cannot be empty." });
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
                    // Check if status already exists to prevent duplicates
                    string checkQuery = "SELECT COUNT(*) FROM Statuses WHERE Status = @Status";
                    await using (var checkCommand = new SqlCommand(checkQuery, connection, transaction))
                    {
                        checkCommand.Parameters.AddWithValue("@Status", NewStatus.Trim());
                        if ((int)await checkCommand.ExecuteScalarAsync() > 0)
                        {
                            return new JsonResult(new { success = false, message = "Status already exists." });
                        }
                    }

                    // Insert the new status
                    string insertQuery = "INSERT INTO Statuses (Status) VALUES (@Status)";
                    await using (var command = new SqlCommand(insertQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Status", NewStatus.Trim());
                        await command.ExecuteNonQueryAsync();
                    }

                    // Commit the transaction if the check and insert operations are successful.
                    await transaction.CommitAsync();
                }
            }

            // Step 2: After the main operation is successful, log the action.
            await LogActionAsync("Add", NewStatus);

            _logger.LogInformation("Successfully added new status: {Status}", NewStatus);
            return new JsonResult(new
            {
                success = true,
                message = "Successfully added status."
            });
        }
        catch (Exception ex)
        {
            // This will catch any errors from the main transaction (check/insert).
            _logger.LogError(ex, "Error adding status: {Status}", NewStatus);
            return new JsonResult(new { success = false, message = "An error occurred while adding the status." });
        }
    }

    // AJAX Handler: Deletes selected statuses.
    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (SelectedStatuses == null || !SelectedStatuses.Any())
        {
            return new JsonResult(new { success = false, message = "No statuses selected for deletion." });
        }

        string connectionString = _configuration.GetConnectionString("SQLConnection");
        try
        {
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Iterate through each selected status to delete it and log the action
                foreach (var status in SelectedStatuses)
                {
                    // Use a transaction to ensure the delete and log operations succeed or fail together.
                    await using (var transaction = connection.BeginTransaction())
                    {
                        // 1. Delete the item
                        string query = "DELETE FROM Statuses WHERE Status = @Status";
                        await using (var command = new SqlCommand(query, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Status", status);
                            await command.ExecuteNonQueryAsync();
                        }

                        // 2. Log the deletion action
                        await LogActionAsync("Delete", status);

                        // If both operations were successful, commit the transaction
                        await transaction.CommitAsync();
                    }
                }
            }

            _logger.LogInformation($"Deleted {SelectedStatuses.Count} statuses");
            return new JsonResult(new
            {
                success = true,
                message = $"Successfully deleted {SelectedStatuses.Count} status(es)."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting statuses");
            return new JsonResult(new { success = false, message = "An error occurred while deleting statuses." });
        }
    }
    // Private helper method to load status data from the database.
    private void LoadStatuses()
    {
        try
        {
            Statuses.Clear();
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT Status FROM Statuses ORDER BY Status";
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Statuses.Add(reader["Status"].ToString());
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading statuses");
            Statuses = new List<string>();
            ModelState.AddModelError(string.Empty, "Could not load statuses from the database.");
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
                var logQuery = "INSERT INTO Log_Statuses (Action, Performed_By, Datetime, Status) VALUES (@Action, @ID, @Time, @Status)";
                await using (var cmd = new SqlCommand(logQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Action", action);
                    cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@Status", name.Trim());
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