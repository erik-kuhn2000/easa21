using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data.SqlClient;

[Authorize(Policy = "AdminOnly")]
public class ManageStatesModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ManageStatesModel> _logger;

    public ManageStatesModel(IConfiguration configuration, ILogger<ManageStatesModel> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    // This property will hold the list of states for display.
    public List<string> States { get; set; } = new List<string>();

    // This property binds the input from the "Add" form.
    [BindProperty]
    public string NewState { get; set; }

    // This property binds the array of selected states for deletion.
    [BindProperty]
    public List<string> SelectedStates { get; set; } = new List<string>();

    // Runs when the page is first requested to populate the initial list.
    public void OnGet()
    {
        LoadStates();
    }

    // AJAX Handler: Returns the current list of states as JSON.
    public IActionResult OnGetList()
    {
        LoadStates();
        return new JsonResult(States);
    }

    // AJAX Handler: Adds a new state.
    public async Task<IActionResult> OnPostAddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewState))
        {
            return new JsonResult(new { success = false, message = "State cannot be empty." });
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
                    // Check if state already exists to prevent duplicates
                    string checkQuery = "SELECT COUNT(*) FROM States WHERE State = @State";
                    await using (var checkCommand = new SqlCommand(checkQuery, connection, transaction))
                    {
                        checkCommand.Parameters.AddWithValue("@State", NewState.Trim());
                        if ((int)await checkCommand.ExecuteScalarAsync() > 0)
                        {
                            return new JsonResult(new { success = false, message = "State already exists." });
                        }
                    }

                    // Insert the new state
                    string insertQuery = "INSERT INTO States (State) VALUES (@State)";
                    await using (var command = new SqlCommand(insertQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@State", NewState.Trim());
                        await command.ExecuteNonQueryAsync();
                    }

                    // Commit the transaction if the check and insert operations are successful.
                    await transaction.CommitAsync();
                }
            }

            // Step 2: After the main operation is successful, log the action.
            await LogActionAsync("Add", NewState);

            _logger.LogInformation("Successfully added new state: {State}", NewState);
            return new JsonResult(new
            {
                success = true,
                message = "Successfully added state."
            });
        }
        catch (Exception ex)
        {
            // This will catch any errors from the main transaction (check/insert).
            _logger.LogError(ex, "Error adding state: {State}", NewState);
            return new JsonResult(new { success = false, message = "An error occurred while adding the state." });
        }
    }

    // AJAX Handler: Deletes selected states.
    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (SelectedStates == null || !SelectedStates.Any())
        {
            return new JsonResult(new { success = false, message = "No states selected for deletion." });
        }

        string connectionString = _configuration.GetConnectionString("SQLConnection");
        try
        {
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Iterate through each selected state to delete it and log the action
                foreach (var state in SelectedStates)
                {
                    // Use a transaction to ensure the delete and log operations succeed or fail together.
                    await using (var transaction = connection.BeginTransaction())
                    {
                        // 1. Delete the item
                        string query = "DELETE FROM States WHERE State = @State";
                        await using (var command = new SqlCommand(query, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@State", state);
                            await command.ExecuteNonQueryAsync();
                        }

                        // 2. Log the deletion action
                        await LogActionAsync("Delete", state);

                        // If both operations were successful, commit the transaction
                        await transaction.CommitAsync();
                    }
                }
            }

            _logger.LogInformation($"Deleted {SelectedStates.Count} states");
            return new JsonResult(new
            {
                success = true,
                message = $"Successfully deleted {SelectedStates.Count} state(s)."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting states");
            return new JsonResult(new { success = false, message = "An error occurred while deleting states." });
        }
    }

    // Private helper method to load state data from the database.
    private void LoadStates()
    {
        try
        {
            States.Clear();
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT State FROM States ORDER BY State";
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            States.Add(reader["State"].ToString());
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading states");
            States = new List<string>();
            ModelState.AddModelError(string.Empty, "Could not load states from the database.");
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
                var logQuery = "INSERT INTO Log_States (Action, Performed_By, Datetime, State) VALUES (@Action, @ID, @Time, @State)";
                await using (var cmd = new SqlCommand(logQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Action", action);
                    cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@State", name.Trim());
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