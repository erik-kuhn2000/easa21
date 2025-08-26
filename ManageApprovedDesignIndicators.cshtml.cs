using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data.SqlClient;


[Authorize(Policy = "AdminOnly")]
public class ManageApprovedDesignIndicatorsModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ManageApprovedDesignIndicatorsModel> _logger;

    public ManageApprovedDesignIndicatorsModel(IConfiguration configuration, ILogger<ManageApprovedDesignIndicatorsModel> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    // This property will hold the list of indicators for display.
    public List<string> ApprovedDesignIndicators { get; set; } = new List<string>();

    // This property binds the input from the "Add" form.
    [BindProperty]
    public string NewApprovedDesignIndicator { get; set; }

    // This property binds the array of selected indicators for deletion.
    [BindProperty]
    public List<string> SelectedApprovedDesignIndicators { get; set; } = new List<string>();

    // Runs when the page is first requested to populate the initial list.
    public void OnGet()
    {
        LoadApprovedDesignIndicators();
    }

    // AJAX Handler: Returns the current list of indicators as JSON.
    public IActionResult OnGetList()
    {
        LoadApprovedDesignIndicators();
        return new JsonResult(ApprovedDesignIndicators);
    }

    // AJAX Handler: Adds a new approved design indicator.
    public async Task<IActionResult> OnPostAddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewApprovedDesignIndicator))
        {
            return new JsonResult(new { success = false, message = "Approved Design Indicator cannot be empty." });
        }

        try
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using (var transaction = connection.BeginTransaction())
                {
                    // Check if indicator already exists to prevent duplicates
                    string checkQuery = "SELECT COUNT(*) FROM ApprovedDesignIndicators WHERE ApprovedDesignIndicator = @ApprovedDesignIndicator";
                    await using (var checkCommand = new SqlCommand(checkQuery, connection, transaction))
                    {
                        checkCommand.Parameters.AddWithValue("@ApprovedDesignIndicator", NewApprovedDesignIndicator.Trim());
                        if ((int)await checkCommand.ExecuteScalarAsync() > 0)
                        {
                            return new JsonResult(new { success = false, message = "Approved Design Indicator already exists." });
                        }
                    }

                    // Insert the new indicator
                    string insertQuery = "INSERT INTO ApprovedDesignIndicators (ApprovedDesignIndicator) VALUES (@ApprovedDesignIndicator)";
                    await using (var command = new SqlCommand(insertQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@ApprovedDesignIndicator", NewApprovedDesignIndicator.Trim());
                        await command.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                }
            }

            await LogActionAsync("Add", NewApprovedDesignIndicator);

            _logger.LogInformation("Successfully added new Approved Design Indicator: {Indicator}", NewApprovedDesignIndicator);
            return new JsonResult(new
            {
                success = true,
                message = "Successfully added Approved Design Indicator."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding Approved Design Indicator: {Indicator}", NewApprovedDesignIndicator);
            return new JsonResult(new { success = false, message = "An error occurred while adding the Approved Design Indicator." });
        }
    }

    // AJAX Handler: Deletes selected approved design indicators.
    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (SelectedApprovedDesignIndicators == null || !SelectedApprovedDesignIndicators.Any())
        {
            return new JsonResult(new { success = false, message = "No Approved Design Indicators selected for deletion." });
        }

        string connectionString = _configuration.GetConnectionString("SQLConnection");
        try
        {
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                foreach (var indicator in SelectedApprovedDesignIndicators)
                {
                    await using (var transaction = connection.BeginTransaction())
                    {
                        // 1. Delete the item
                        string query = "DELETE FROM ApprovedDesignIndicators WHERE ApprovedDesignIndicator = @ApprovedDesignIndicator";
                        await using (var command = new SqlCommand(query, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@ApprovedDesignIndicator", indicator);
                            await command.ExecuteNonQueryAsync();
                        }

                        // 2. Log the deletion action
                        await LogActionAsync("Delete", indicator);

                        await transaction.CommitAsync();
                    }
                }
            }

            _logger.LogInformation($"Deleted {SelectedApprovedDesignIndicators.Count} approved design indicators");
            return new JsonResult(new
            {
                success = true,
                message = $"Successfully deleted {SelectedApprovedDesignIndicators.Count} Approved Design Indicator(s)."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Approved Design Indicators");
            return new JsonResult(new { success = false, message = "An error occurred while deleting the Approved Design Indicators." });
        }
    }

    // Private helper method to load data from the database.
    private void LoadApprovedDesignIndicators()
    {
        try
        {
            ApprovedDesignIndicators.Clear();
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT ApprovedDesignIndicator FROM ApprovedDesignIndicators ORDER BY ApprovedDesignIndicator";
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ApprovedDesignIndicators.Add(reader["ApprovedDesignIndicator"].ToString());
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Approved Design Indicators");
            ApprovedDesignIndicators = new List<string>();
            ModelState.AddModelError(string.Empty, "Could not load Approved Design Indicators from the database.");
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
                var logQuery = "INSERT INTO Log_ApprovedDesignIndicators (Action, Performed_By, Datetime, ApprovedDesignIndicator) VALUES (@Action, @ID, @Time, @ApprovedDesignIndicator)";
                await using (var cmd = new SqlCommand(logQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Action", action);
                    cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@ApprovedDesignIndicator", name.Trim());
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log admin action '{Action}' for item '{Name}'", action, name);
        }
    }
}