using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;


public class HomeModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _hostingEnvironment;
    private readonly ILogger<HomeModel> _logger;

    public HomeModel(IConfiguration configuration, IWebHostEnvironment hostingEnvironment, ILogger<HomeModel> logger)
    {
        _configuration = configuration;
        _hostingEnvironment = hostingEnvironment;
        _logger = logger;
    }

    [TempData]
    public string SuccessMessage { get; set; }

    public void OnGet()
    {
        // Method can be used to load dashboard KPIs.
    }

    // Handler to download the template file. This is called by the global JS function.
    public async Task<IActionResult> OnGetDownloadTemplate()
    {
        // ... (This function is already complete and correct)
        try
        {
            var templatePath = _configuration.GetValue<string>("FilePaths:TemplatePath");
            var filePath = Path.Combine(_hostingEnvironment.WebRootPath, templatePath);

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("Template file not found.");
            }

            var memory = new MemoryStream();
            await using (var stream = new FileStream(filePath, FileMode.Open))
            {
                await stream.CopyToAsync(memory);
            }
            memory.Position = 0;

            _logger.LogInformation("Template file downloaded successfully.");
            return File(memory, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Path.GetFileName(filePath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading template file");
            return StatusCode(500, "Error downloading template file.");
        }
    }

}