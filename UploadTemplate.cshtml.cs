using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using System.Data.SqlClient;


[Authorize(Policy = "AdminOnly")]
public class UploadTemplateModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _hostingEnvironment;
    private readonly ILogger<UploadTemplateModel> _logger;

    public UploadTemplateModel(IConfiguration configuration, IWebHostEnvironment hostingEnvironment, ILogger<UploadTemplateModel> logger)
    {
        _configuration = configuration;
        _hostingEnvironment = hostingEnvironment;
        _logger = logger;
    }

    [BindProperty]
    public IFormFile UploadedFile { get; set; }

    public void OnGet()
    {
        // This page does not need to load any data on GET,
        // so this method can be empty.
    }

    // This is the primary handler for the form post.
    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            if (UploadedFile == null || UploadedFile.Length == 0)
            {
                return new JsonResult(new { success = false, message = "Please select a file to upload." });
            }

            // Validate file extension
            var allowedExtensions = new[] {".pdf"};
            var fileExtension = Path.GetExtension(UploadedFile.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(fileExtension))
            {
                return new JsonResult(new { success = false, message = "Only PDF files (.pdf) are allowed." });
            }

            // Validate file size (10MB limit)
            const long maxFileSize = 10 * 1024 * 1024; // 10MB in bytes
            if (UploadedFile.Length > maxFileSize)
            {
                return new JsonResult(new { success = false, message = "File size too large. Please select a file smaller than 10MB." });
            }

            // Get the template path from configuration
            var templatePath = _configuration.GetValue<string>("FilePaths:TemplatePath");
            var filePath = Path.Combine(_hostingEnvironment.WebRootPath, templatePath);

            // Ensure the directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create backup of existing file if it exists
            if (System.IO.File.Exists(filePath))
            {
                var backupPath = Path.ChangeExtension(filePath, $".backup_{DateTime.Now:ddMMMyyyyHHmmss}{Path.GetExtension(filePath)}");
                System.IO.File.Copy(filePath, backupPath, true);
                _logger.LogInformation($"Backup created: {backupPath}");
            }

            // Save the new file
            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await UploadedFile.CopyToAsync(stream);
            }

            _logger.LogInformation($"Template file uploaded successfully: {UploadedFile.FileName}");

            // Log the action to the database
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            await using (SqlConnection conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                var logQuery = "INSERT INTO Log_TemplateUploads (Action, Performed_By, Datetime) VALUES (@Action, @ID, @Time)";
                await using (SqlCommand cmd = new SqlCommand(logQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Action", "Upload");
                    cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            return new JsonResult(new { success = true, message = "Template file uploaded successfully!" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading template file");
            return new JsonResult(new { success = false, message = "An error occurred while uploading the file. Please try again." });
        }
    }
}
