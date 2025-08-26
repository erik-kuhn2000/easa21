using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;



namespace QApp.Pages // <-- IMPORTANT: Change to your project's namespace
{
    [Authorize(Policy = "AdminOnly")]
    public class DownloadLogsModel : PageModel
    {
        private readonly IConfiguration _configuration;

        // A public property to hold the list of allowed log tables.
        // The View will use this to create the download buttons.
        public Dictionary<string, string> LogNameMap { get; } = new Dictionary<string, string>
        {
            { "Log_Certificates", "Certificate Log (Add, Update, Print)" },
            { "Log_PartNumbers", "Part Number Management Log" },
            { "Log_Prefixes", "Prefix Management Log" },
            { "Log_Amendments", "Amendment Management Log" },
            { "Log_Statuses", "Status Management Log" },
            { "Log_AuthorisationNumber", "Authorisation Number Management Log" },
            { "Log_Users", "User Management Log" },
            { "Log_TemplateUploads", "Template Upload Log" }
        };

        public DownloadLogsModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync(string tableName)
        {
            // SECURITY: Validate that the requested table is a valid key in our dictionary.
            if (string.IsNullOrEmpty(tableName) || !LogNameMap.ContainsKey(tableName))
            {
                return BadRequest("Invalid or unauthorized table specified.");
            }

            string connectionString = _configuration.GetConnectionString("SQLConnection");

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("LogData");

                try
                {
                    using (var connection = new SqlConnection(connectionString))
                    {
                        await connection.OpenAsync();
                        var command = new SqlCommand($"SELECT * FROM {tableName}", connection);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                worksheet.Cell(1, 1).Value = "No log entries found.";
                            }
                            else
                            {
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    worksheet.Cell(1, i + 1).Value = reader.GetName(i);
                                }

                                int currentRow = 2;
                                while (await reader.ReadAsync())
                                {
                                    for (int i = 0; i < reader.FieldCount; i++)
                                    {
                                        worksheet.Cell(currentRow, i + 1).Value = reader.GetValue(i).ToString();
                                    }
                                    currentRow++;
                                }
                            }
                        }
                    }
                }
                catch (SqlException ex)
                {
                    worksheet.Cell(1, 1).Value = $"Error reading table {tableName}: {ex.Message}";
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string timestamp = DateTime.Now.ToString("ddMMMyyyy");

                    Response.Headers.Append("Access-Control-Expose-Headers", "Content-Disposition");

                    return File(
                        content,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"{tableName}_{DateTime.Now:ddMMMyyyy}.xlsx");
                }
            }
        }
    }
}