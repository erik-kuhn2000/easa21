using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

namespace QApp.Pages
{
    // --- FONT RESOLVER IMPLEMENTATION FOR PDFSHARP ---
    public class FontResolver : IFontResolver
    {
        private static string _fontPath;

        public static void Initialize(string fontPath)
        {
            _fontPath = fontPath;
        }

        public byte[] GetFont(string faceName)
        {
            var fontFile = Path.Combine(_fontPath, faceName);
            if (File.Exists(fontFile))
            {
                return File.ReadAllBytes(fontFile);
            }
            return null;
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            string fontFile = "Courier New.ttf";

            if (familyName.Equals("Arial", StringComparison.OrdinalIgnoreCase))
            {
                fontFile = "arial.ttf";
            }
            else if (familyName.Equals("Courier New", StringComparison.OrdinalIgnoreCase))
            {
                fontFile = "Courier New.ttf";
            }

            return new FontResolverInfo(fontFile);
        }
    }

    public class SearchModel : PageModel
    {
        public class CertificateSummary
        {
            public string CertNo { get; set; }
            public string ProductNo { get; set; }
            public string SerialNo { get; set; }
            public string Amendment { get; set; }
            public string Signatory { get; set; }
            public string Date { get; set; }
            public string Quantity { get; set; }
            public string Edition { get; set; }

            public string State { get; set; }

            public bool IsLatestEdition { get; set; }
        }

        public class CertificateDetails
        {
            public string CertNo { get; set; }
            public string ProductNo { get; set; }
            public string ProductDescription { get; set; } // ADDED
            public string ProductType { get; set; }
            public string Manufacturer { get; set; }
            public string SerialNo { get; set; }
            public string Serialization { get; set; }
            public string Amendment { get; set; }
            public string Signatory { get; set; }
            public string Date { get; set; }
            public string Quantity { get; set; }
            public string Edition { get; set; }
            public string Remarks1 { get; set; }
            public string Remarks2 { get; set; }
            public string Remarks3 { get; set; }
            public string Remarks4 { get; set; }
            public string Authorisation { get; set; }
            public string Item { get; set; }
            public string Status { get; set; }
            public string Approved { get; set; }
            public string State { get; set; }

            public string Comment { get; set; }

        }

        public class SearchCriteriaModel
        {
            public string CertNo { get; set; }
            public string ProductNo { get; set; }
            public string SerialNo { get; set; }
            public List<string> Amendment { get; set; }
            public string Signatory { get; set; }
            public string StartDate { get; set; }
            public string EndDate { get; set; }
            public string Quantity { get; set; }
            public string Edition { get; set; }

            public string State { get; set; }
        }

        [BindProperty]
        public SearchCriteriaModel SearchCriteria { get; set; } = new SearchCriteriaModel();
        public List<SelectListItem> ProductNoListNew { get; set; }
        public List<SelectListItem> SignatoryListNew { get; set; }
        public List<CertificateSummary> SearchResults { get; set; } = new List<CertificateSummary>();
        public List<SelectListItem> ProductNoList { get; set; }
        public List<SelectListItem> AmendmentList { get; set; }
        public List<SelectListItem> AmendmentListNew { get; set; }
        public List<SelectListItem> SignatoryList { get; set; }

        public List<SelectListItem> StateList { get; set; }
        public List<SelectListItem> StateListNew { get; set; }

        public List<SelectListItem> ApprovedList { get; set; }
        public List<SelectListItem> ApprovedListNew { get; set; }

        public List<SelectListItem> StatusList { get; set; }
        public List<SelectListItem> StatusListNew { get; set; }

        public string CurrentUserSignatoryName { get; set; }

        [BindProperty]
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalResults { get; set; } = 0;
        public int TotalPages => (int)Math.Ceiling((double)TotalResults / PageSize);
        public string LimitMessage { get; set; }
        public string SearchErrorMessage { get; set; }
        public bool ShowNoResultsMessage { get; set; }
        public bool Searched { get; set; }

        private readonly IConfiguration _configuration;
        private readonly ILogger<SearchModel> _logger;
        private readonly IAuthorizationService _authorizationService;
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly IMemoryCache _cache;

        public SearchModel(IConfiguration configuration, ILogger<SearchModel> logger, IAuthorizationService authorizationService, IWebHostEnvironment hostingEnvironment, IMemoryCache memoryCache)
        {
            _configuration = configuration;
            _logger = logger;
            _authorizationService = authorizationService;
            _hostingEnvironment = hostingEnvironment;
            _cache = memoryCache;

            string fontPath = Path.Combine(_hostingEnvironment.WebRootPath, "Fonts");
            FontResolver.Initialize(fontPath);
            GlobalFontSettings.FontResolver = new FontResolver();
        }

        [Authorize(Policy = "SignatoryAccess")]
        public async Task<IActionResult> OnPostPrintCertificate(string certNo, string edition = null)
        {
            // Step 1: Get the certificate's original details for logging
            var originalDetails = await GetCertificateDetailsAsync(certNo, edition);
            if (originalDetails == null)
            {
                return NotFound("Certificate not found.");
            }

            // Step 2: Update the state to "Printed" ONLY if current state is "Valid"
            bool stateChanged = false;
            if (string.Equals(originalDetails.State, "Valid", StringComparison.OrdinalIgnoreCase))
            {
                var stateUpdateSuccess = await UpdateCertificateStateAsync(certNo, "Printed", edition);
                if (stateUpdateSuccess)
                {
                    stateChanged = true;
                    // Update the object for PDF generation
                    originalDetails.State = "Printed";
                }
            }

            // Step 3: Log the print action (regardless of state change)
            await LogPrintActionAsync(certNo, edition, stateChanged);

            // Step 4: Generate the PDF and return it
            byte[] pdfBytes = await GeneratePdfCertificateAsync(originalDetails);

            return File(
                pdfBytes,
                "application/pdf",
                $"Certificate_{certNo}_Ed{edition}_{DateTime.Now:ddMMMyyyy}.pdf"
                
            );
        }

        private async Task<string> GetCurrentUserSignatoryNameAsync()
        {
            var userId = User.Identity?.Name;
            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            string connectionString = _configuration.GetConnectionString("SQLConnection");
            await using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                string query = "SELECT Name FROM Users WHERE TGI = @UserId AND Role = @SignatoryRole";
                await using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@SignatoryRole", 2);
                    var result = await cmd.ExecuteScalarAsync();
                    return result?.ToString();
                }
            }
        }
        private async Task<byte[]> GeneratePdfCertificateAsync(CertificateDetails details)
        {
            string relativePath = _configuration["FilePaths:TemplatePath"];
            string webRootPath = _hostingEnvironment.WebRootPath;
            string templatePath = Path.Combine(webRootPath, relativePath);

            if (!System.IO.File.Exists(templatePath))
            {
                throw new FileNotFoundException("PDF template not found.", templatePath);
            }

            // Asynchronously read the file into a byte array first
            byte[] templateBytes = await System.IO.File.ReadAllBytesAsync(templatePath);
            using (var templateStream = new MemoryStream(templateBytes))
            {
                PdfDocument document = PdfReader.Open(templateStream, PdfDocumentOpenMode.Modify);

                if (document.AcroForm != null)
                {
                    // PDF manipulation logic remains the same (it's in-memory)
                    var form = document.AcroForm;
                    // ... (SetFormField calls as before) ...
                    string formattedDate = string.Empty;
                    if (!string.IsNullOrEmpty(details.Date) && DateTime.TryParse(details.Date, out DateTime parsedDate))
                    {
                        formattedDate = parsedDate.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
                    }

                    SetFormField(form, "remarks1", details.Remarks1, "Arial", 10);
                    SetFormField(form, "remarks2", details.Remarks2, "Arial", 10);
                    SetFormField(form, "remarks3", details.Remarks3, "Arial", 10);
                    SetFormField(form, "remarks4", details.Remarks4, "Arial", 10);
                    SetFormField(form, "items", details.Item, "Arial", 10);
                    SetFormField(form, "approvalno", details.Authorisation, "Arial", 10);
                    SetFormField(form, "quantity", details.Quantity, "Arial", 10);
                    SetFormField(form, "serialno", details.SerialNo, "Arial", 10);
                    SetFormField(form, "name", details.Signatory, "Arial", 10);
                    SetFormField(form, "date", formattedDate, "Arial", 10);
                    SetFormField(form, "partno", details.ProductNo, "Arial", 10);
                    SetFormField(form, "trackingnumber", details.CertNo + "-2-" + details.Edition, "Arial", 10);
                    SetFormField(form, "status", details.Status, "Arial", 10);
                    SetFormField(form, "description", details.ProductDescription, "Arial", 10);

                    if (details.Amendment == ".") { SetFormField(form, "amendment", "", "Arial", 10); }
                    else { SetFormField(form, "amendment", details.Amendment, "Arial", 10); }

                    if (details.Approved == "Approved Design Data") { SetFormField(form, "approved", "X", "Arial", 10); }
                    else { SetFormField(form, "notapproved", "X", "Arial", 10); }

                    if (details.Serialization == "Yes") { SetFormField(form, "workorder", details.ProductNo + "-" + details.SerialNo, "Arial", 10); }
                    else { SetFormField(form, "workorder", details.ProductNo, "Arial", 10); }

                    if (form.Elements.ContainsKey("/NeedAppearances"))
                    {
                        form.Elements["/NeedAppearances"] = new PdfBoolean(true);
                    }
                    else
                    {
                        form.Elements.Add("/NeedAppearances", new PdfBoolean(true));
                    }

                    foreach (var fieldName in form.Fields.Names)
                    {
                        var field = form.Fields[fieldName];
                        if (field != null)
                        {
                            field.ReadOnly = true;
                        }
                    }
                }
                else
                {
                    _logger.LogError("Could not find an AcroForm in the PDF template.");
                }

                using (MemoryStream outputStream = new MemoryStream())
                {
                    document.Save(outputStream, false);
                    return outputStream.ToArray();
                }
            }
        }

        private void SetFormField(PdfAcroForm form, string fieldName, string value, string fontName, double fontSize)
        {
            if (form.Fields.Names.Contains(fieldName) && form.Fields[fieldName] is PdfTextField textField)
            {
                textField.Value = new PdfString(value ?? string.Empty);
                string daString = $"/{fontName} {fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)} Tf";
                textField.Elements.SetString(PdfAcroField.Keys.DA, daString);
            }
            else
            {
                _logger.LogWarning("PDF form field '{FieldName}' was not found or is not a text field in the template.", fieldName);
            }
        }

        public async Task OnGetAsync()
        {
            await LoadDropdownsAsync();
            CurrentUserSignatoryName = await GetCurrentUserSignatoryNameAsync();
        }

        public async Task<IActionResult> OnPostAsync(string handler)
        {
            LimitMessage = string.Empty;
            Searched = false;
            ShowNoResultsMessage = false;

            if (handler == "clear")
            {
                SearchCriteria = new SearchCriteriaModel();
                SearchResults = new List<CertificateSummary>();
                ModelState.Clear();
            }
            else if (handler == "search")
            {
                // ... (Validation logic is synchronous and remains unchanged) ...
                if (!string.IsNullOrWhiteSpace(SearchCriteria.Edition) &&
                    int.TryParse(SearchCriteria.Edition, out int editionValue) &&
                    editionValue >= 0 && editionValue <= 99)
                {
                    SearchCriteria.Edition = editionValue.ToString("D2");
                }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.Quantity) &&
                    int.TryParse(SearchCriteria.Quantity, out int quantityValue) &&
                    quantityValue >= 0 && quantityValue <= 9999)
                {
                    if (quantityValue < 10)
                    {
                        SearchCriteria.Quantity = quantityValue.ToString("D2");
                    }
                    else
                    {
                        SearchCriteria.Quantity = quantityValue.ToString();
                    }
                }

                if (!string.IsNullOrWhiteSpace(SearchCriteria.StartDate) && !string.IsNullOrWhiteSpace(SearchCriteria.EndDate))
                {
                    if (DateTime.TryParse(SearchCriteria.StartDate, out DateTime startDate) &&
                        DateTime.TryParse(SearchCriteria.EndDate, out DateTime endDate))
                    {
                        if (startDate > endDate)
                        {
                            ModelState.AddModelError(string.Empty, "Start date cannot be later than end date.");
                            await LoadDropdownsAsync(); // Still need to load dropdowns on error
                            return Page();
                        }
                    }
                }

                if (PageNumber < 1) PageNumber = 1;

                // CORRECTED LINE: Deconstruct the tuple into the class properties
                (SearchResults, TotalResults) = await SearchCertificatesAsync(PageNumber, PageSize);
                Searched = true;

                if (SearchResults == null || !SearchResults.Any())
                {
                    SearchErrorMessage = "No certificates found matching your criteria.";
                }
            }
            else if (handler == "export")
            {
                return await OnPostExportAsync();
            }

            await LoadDropdownsAsync();
            CurrentUserSignatoryName = await GetCurrentUserSignatoryNameAsync();
            return Page();
        }

        private async Task<bool> UpdateCertificateStateAsync(string certNo, string newState, string edition = null, bool isCancellation = false)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                await using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    string sql = !string.IsNullOrEmpty(edition)
                        ? "UPDATE Certificates SET State = @NewState WHERE CertNo = @CertNo AND Edition = @Edition"
                        : "UPDATE Certificates SET State = @NewState WHERE CertNo = @CertNo";

                    await using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@NewState", newState ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@CertNo", certNo);
                        if (!string.IsNullOrEmpty(edition))
                        {
                            command.Parameters.AddWithValue("@Edition", edition);
                        }

                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        if (rowsAffected > 0 && isCancellation)
                        {
                            var originalDetails = await GetCertificateDetailsAsync(certNo, edition);
                            await LogUpdateActionAsync(originalDetails, new CertificateDetails { State = "Cancelled" }, certNo, true);
                        }
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating state for certificate {CertNo} edition {Edition}", certNo, edition);
                return false;
            }
        }

        public async Task<IActionResult> OnPostExportAsync()
        {
            // Build the SQL query and parameters based on the current search criteria.
            var (sqlQuery, parameters) = BuildExportQuery();

            // If the query is null, it means there were no search criteria, so we shouldn't export anything.
            if (sqlQuery == null)
            {
                SearchErrorMessage = "Please perform a search before exporting.";
                await LoadDropdownsAsync();
                return Page();
            }

            string connectionString = _configuration.GetConnectionString("SQLConnection");
            var stream = new MemoryStream();

            try
            {
                await using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    await using (var command = new SqlCommand(sqlQuery, connection))
                    {
                        if (parameters.Any())
                        {
                            command.Parameters.AddRange(parameters.ToArray());
                        }

                        // Execute the query and get a reader to stream the results.
                        await using (var reader = await command.ExecuteReaderAsync())
                        {
                            // Check if the query returned any rows before creating the workbook.
                            if (!reader.HasRows)
                            {
                                SearchErrorMessage = "No results to export based on the current criteria.";
                                await LoadDropdownsAsync();
                                return Page();
                            }

                            using (var workbook = new XLWorkbook())
                            {
                                var worksheet = workbook.Worksheets.Add("Certificate Details");
                                int currentRow = 1;

                                // Define and write the headers for the Excel file.
                                var headers = new string[]
                                {
                            "3. Certificate No.", "3. Edition of Form Tracking No.", "6. Item", "7. Description", "8. Part No.", "9. Qty.",
                            "10. Serial No.", "11. Status/Work.", "12. Remarks (Line 1)", "12. Remarks (Line 2)", "12. Remarks (Line 3)", "12. Remarks (Line 4)", "12. Remarks (Amendment)", "13a. Approved Design Indicator","13c. Approval/Authorisation Number", "13d. Name of Signatory",
                            "13e. Approval Date", "Part Type", "Manufacturer", "Serialization", "State", "Comment"
                                };

                                for (int i = 0; i < headers.Length; i++)
                                {
                                    worksheet.Cell(currentRow, i + 1).Value = headers[i];
                                }

                                // Style the header row.
                                var headerRow = worksheet.Row(currentRow);
                                headerRow.Style.Font.Bold = true;
                                headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;
                                headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                                // --- Streaming Core Logic ---
                                // Loop through the reader and write each database row directly to the worksheet.
                                while (await reader.ReadAsync())
                                {
                                    currentRow++;
                                    int col = 1;
                                    worksheet.Cell(currentRow, col++).Value = reader["CertNo"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Edition"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Item"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["ProductDescription"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["ProductNo"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Quantity"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["SerialNo"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Status"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Remarks1"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Remarks2"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Remarks3"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Remarks4"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Amendment"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Approved"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Authorisation"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Signatory"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Date"] != DBNull.Value ? Convert.ToDateTime(reader["Date"]).ToString("dd MMM yyyy") : "";
                                    worksheet.Cell(currentRow, col++).Value = reader["ProductType"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Manufacturer"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Serialization"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["State"]?.ToString();
                                    worksheet.Cell(currentRow, col++).Value = reader["Comment"]?.ToString();
                                }

                                // Auto-fit columns for better readability after all data is written.
                                worksheet.Columns().AdjustToContents();

                                // Save the workbook content to the memory stream.
                                workbook.SaveAs(stream);
                            }
                        }
                    }
                }

                // Reset the stream's position to the beginning before sending it to the browser.
                stream.Position = 0;
                string excelName = $"Certificates_Export_{DateTime.Now:ddMMMyyyy}.xlsx";

                return File(
                    stream,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    excelName
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during the Excel export stream.");
                SearchErrorMessage = "An unexpected error occurred during export. Please try again.";
                await stream.DisposeAsync(); // Ensure stream is disposed on error
                await LoadDropdownsAsync();
                return Page();
            }
        }

        private (string sql, List<SqlParameter> parameters) BuildExportQuery()
        {
            var conditions = new List<string>();
            var parameters = new List<SqlParameter>();

            // This logic to build conditions and parameters remains the same.
            if (!string.IsNullOrWhiteSpace(SearchCriteria.CertNo)) { conditions.Add("CertNo LIKE @CertNo"); parameters.Add(new SqlParameter("@CertNo", $"%{SearchCriteria.CertNo}%")); }
            if (!string.IsNullOrWhiteSpace(SearchCriteria.ProductNo)) { conditions.Add("ProductNo = @ProductNo"); parameters.Add(new SqlParameter("@ProductNo", SearchCriteria.ProductNo)); }
            if (!string.IsNullOrWhiteSpace(SearchCriteria.Edition)) { conditions.Add("Edition LIKE @Edition"); parameters.Add(new SqlParameter("@Edition", SearchCriteria.Edition)); }
            if (!string.IsNullOrWhiteSpace(SearchCriteria.Quantity)) { conditions.Add("Quantity LIKE @Quantity"); parameters.Add(new SqlParameter("@Quantity", SearchCriteria.Quantity)); }
            if (!string.IsNullOrWhiteSpace(SearchCriteria.SerialNo)) { conditions.Add("SerialNo LIKE @SerialNo"); parameters.Add(new SqlParameter("@SerialNo", $"%{SearchCriteria.SerialNo}%")); }
            if (SearchCriteria.Amendment != null && SearchCriteria.Amendment.Any())
            {
                var amendmentValue = string.Join(", ", SearchCriteria.Amendment);
                conditions.Add("Amendment = @Amendment");
                parameters.Add(new SqlParameter("@Amendment", amendmentValue));
            }
            if (!string.IsNullOrWhiteSpace(SearchCriteria.StartDate) && !string.IsNullOrWhiteSpace(SearchCriteria.EndDate))
            {
                conditions.Add("CONVERT(date, Date) >= @StartDate AND CONVERT(date, Date) <= @EndDate");
                parameters.Add(new SqlParameter("@StartDate", DateTime.Parse(SearchCriteria.StartDate)));
                parameters.Add(new SqlParameter("@EndDate", DateTime.Parse(SearchCriteria.EndDate)));
            }
            else if (!string.IsNullOrWhiteSpace(SearchCriteria.StartDate))
            {
                conditions.Add("CONVERT(date, Date) >= @StartDate");
                parameters.Add(new SqlParameter("@StartDate", DateTime.Parse(SearchCriteria.StartDate)));
            }
            else if (!string.IsNullOrWhiteSpace(SearchCriteria.EndDate))
            {
                conditions.Add("CONVERT(date, Date) <= @EndDate");
                parameters.Add(new SqlParameter("@EndDate", DateTime.Parse(SearchCriteria.EndDate)));
            }
            if (!string.IsNullOrWhiteSpace(SearchCriteria.Signatory)) { conditions.Add("Signatory = @Signatory"); parameters.Add(new SqlParameter("@Signatory", SearchCriteria.Signatory)); }
            if (!string.IsNullOrWhiteSpace(SearchCriteria.State)) { conditions.Add("State = @State"); parameters.Add(new SqlParameter("@State", $"%{SearchCriteria.State}%")); }

   

            // Base SELECT statement
            var selectSql = @"SELECT CertNo, ProductNo, SerialNo, Amendment, Signatory, Date, Edition,Remarks1, Remarks2, Remarks3, Remarks4, Quantity, Authorisation, Item, Status, Approved, ProductDescription,Serialization, ProductType, Manufacturer, State, Comment FROM Certificates";

            // Conditionally add the WHERE clause ONLY if there are conditions
            if (conditions.Any())
            {
                selectSql += " WHERE " + string.Join(" AND ", conditions);
            }

            // Always add the ORDER BY clause
            selectSql += " ORDER BY CertNo DESC, CAST(Edition AS INT) DESC";

        

            return (selectSql, parameters);
        }




        public async Task<bool> IsUserAuthorizedToUpdate(string certNo)
        {
            // CHANGED: This now checks the user's general permission policy ("SignatoryAccess")
            // instead of checking against a specific certificate number.
            var authorizationResult = await _authorizationService.AuthorizeAsync(User, "SignatoryRoleRequired");
            return authorizationResult.Succeeded;
        }


        public async Task<IActionResult> OnGetCertificateDetailsAsync(string certNo, string edition = null)
        {
            if (string.IsNullOrWhiteSpace(certNo))
            {
                return new JsonResult(new { success = false, message = "Certificate number is required." });
            }

            try
            {
                var certificateDetails = await GetCertificateDetailsAsync(certNo, edition);
                if (certificateDetails == null)
                {
                    return new JsonResult(new { success = false, message = "Certificate not found." });
                }

                return new JsonResult(new { success = true, data = certificateDetails });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting details for certificate {CertNo}", certNo);
                return new JsonResult(new { success = false, message = $"Database error: {ex.Message}" });
            }
        }


        public async Task<IActionResult> OnPostUpdateCertificate(
      string certNo, string productNo, string productDescription,
      string serialNo, string serialization, string amendment, string signatory, string date,
      string edition, string remarks1, string remarks2, string remarks3, string remarks4,
      string quantity, string authorisation, string item, string status, string approved,
      string state, string comment)
        {
            var originalDetails = await GetCertificateDetailsAsync(certNo, edition);
            if (originalDetails == null)
            {
                return new JsonResult(new { success = false, message = "Certificate not found." });
            }
            if (originalDetails.State == "Cancelled")
            {
                return new JsonResult(new { success = false, message = "Cannot update a cancelled certificate." });
            }

            string currentUserSignatory = await GetCurrentUserSignatoryNameAsync();
            string finalSignatoryName = originalDetails.Signatory;
            if (!string.IsNullOrEmpty(currentUserSignatory) && originalDetails.Signatory != currentUserSignatory)
            {
                finalSignatoryName = currentUserSignatory;
            }

            if (string.IsNullOrWhiteSpace(certNo))
            {
                return new JsonResult(new { success = false, message = "Certificate number is required." });
            }

            try
            {
                var errors = new List<string>();
                if (string.IsNullOrWhiteSpace(productNo)) errors.Add("Product Number is required.");
                if (string.IsNullOrWhiteSpace(serialNo)) errors.Add("Serial Number is required.");
                if (string.IsNullOrWhiteSpace(amendment)) errors.Add("Amendment is required.");
                if (string.IsNullOrWhiteSpace(signatory)) errors.Add("Signatory is required.");
                if (string.IsNullOrWhiteSpace(date)) errors.Add("Approval Date is required.");
                if (!string.IsNullOrWhiteSpace(date) && !DateTime.TryParse(date, out _)) errors.Add("Invalid date format.");

                if (string.IsNullOrWhiteSpace(edition))
                {
                    errors.Add("Edition of Certificate is required.");
                }


                if (string.IsNullOrWhiteSpace(quantity))
                {
                    errors.Add("Quantity is required.");
                }
                else if (int.TryParse(quantity, out int quantityNum))
                {
                    if (quantityNum < 0 || quantityNum > 99999)
                    {
                        errors.Add("Quantity must be between 0 and 99999.");
                    }
                    else
                    {
                        quantity = quantityNum < 10 ? quantityNum.ToString("D2") : quantityNum.ToString();
                    }
                }
                else
                {
                    errors.Add("Quantity must have a valid number format.");
                }

                if (errors.Any())
                {
                    return new JsonResult(new { success = false, message = string.Join(" ", errors) });
                }

                var newDetails = new CertificateDetails
                {
                    CertNo = certNo, // Same certificate number
                    ProductNo = productNo,
                    ProductDescription = productDescription,
                    SerialNo = serialNo,
                    Serialization = serialization,
                    Amendment = amendment,
                    Signatory = finalSignatoryName,
                    Date = DateTime.Now.ToString("dd MMM yyyy"),
                    Edition = edition, // Incremented edition
                    Remarks1 = remarks1,
                    Remarks2 = remarks2,
                    Remarks3 = remarks3,
                    Remarks4 = remarks4,
                    Quantity = quantity,
                    Authorisation = authorisation,
                    Item = item,
                    Status = status,
                    Approved = approved,
                    State = state,
                    Comment = comment,
                    ProductType = originalDetails.ProductType,
                    Manufacturer = originalDetails.Manufacturer,
                };
                bool success;
                if (originalDetails.State == "Valid")
                {
                    success = await UpdateCertificateInDatabaseAsync(originalDetails, newDetails, certNo, edition);
                }
                else
                {
                    if (int.TryParse(edition, out int editionNum))
                    {

                        editionNum++;
                        if (editionNum < 0 || editionNum > 99)
                        {
                            errors.Add("Edition increment would result in a value outside the range 00-99.");
                        }
                        else
                        {
                            newDetails.Edition = editionNum.ToString("D2");
                        }
                    }
                    else
                    {
                        errors.Add("Edition has an invalid format.");
                    }
                    success = await InsertNewCertificateVersionAsync(originalDetails, newDetails, certNo);
                }

                if (success)
                {
                    await LogUpdateActionAsync(originalDetails, newDetails, certNo);
                    return new JsonResult(new { success = true, message = "New certificate version created successfully." });
                }
                else
                {
                    return new JsonResult(new { success = false, message = "Failed to create new certificate version in the database." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating new certificate version for {CertNo}", certNo);
                return new JsonResult(new { success = false, message = $"Update error: {ex.Message}" });
            }
        }

        public async Task<IActionResult> OnPostCancelCertificateAsync(string certNo, string edition)
        {
            var originalDetails = await GetCertificateDetailsAsync(certNo, edition);
            if (originalDetails == null)
            {
                return new JsonResult(new { success = false, message = "Certificate not found." });
            }

            try
            {
                var success = await UpdateCertificateStateAsync(certNo, "Cancelled", edition, true);

                if (success)
                {
                    return new JsonResult(new { success = true, message = "Certificate cancelled successfully." });
                }
                else
                {
                    return new JsonResult(new { success = false, message = "Failed to cancel certificate." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling certificate {CertNo}", certNo);
                return new JsonResult(new { success = false, message = $"Cancellation error: {ex.Message}" });
            }
        }

        // Add this new method to insert a new certificate version
        private async Task<bool> InsertNewCertificateVersionAsync(CertificateDetails originalDetails, CertificateDetails newDetails, string certNo)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                await using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    // Insert new certificate entry with incremented edition
                    var sql = @"INSERT INTO Certificates (
                CertNo, ProductNo, ProductDescription, ProductType, Manufacturer, 
                SerialNo, Serialization, Amendment, Signatory, Date, Edition, 
                Quantity, Remarks1, Remarks2, Remarks3, Remarks4, Authorisation, 
                Item, Status, Approved, State, Comment
            ) VALUES (
                @CertNo, @ProductNo, @ProductDescription, @ProductType, @Manufacturer,
                @SerialNo, @Serialization, @Amendment, @Signatory, @Date, @Edition,
                @Quantity, @Remarks1, @Remarks2, @Remarks3, @Remarks4, @Authorisation,
                @Item, @Status, @Approved, @State, @Comment
            )";

                    await using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@CertNo", newDetails.CertNo);
                        command.Parameters.AddWithValue("@ProductNo", newDetails.ProductNo ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@ProductDescription", newDetails.ProductDescription ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@ProductType", newDetails.ProductType ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Manufacturer", newDetails.Manufacturer ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@SerialNo", newDetails.SerialNo ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Serialization", newDetails.Serialization ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Amendment", newDetails.Amendment ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Signatory", newDetails.Signatory ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Date", string.IsNullOrEmpty(newDetails.Date) ? (object)DBNull.Value : DateTime.Parse(newDetails.Date));
                        command.Parameters.AddWithValue("@Edition", newDetails.Edition ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Quantity", newDetails.Quantity ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Remarks1", newDetails.Remarks1 ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Remarks2", newDetails.Remarks2 ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Remarks3", newDetails.Remarks3 ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Remarks4", newDetails.Remarks4 ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Authorisation", newDetails.Authorisation ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Item", newDetails.Item ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Status", newDetails.Status ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Approved", newDetails.Approved ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@State", newDetails.State ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Comment", newDetails.Comment ?? (object)DBNull.Value);

                        int rowsAffected = command.ExecuteNonQuery();
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting new certificate version for {CertNo}", certNo);
                throw;
            }
        }

        private async Task<bool> UpdateCertificateInDatabaseAsync(CertificateDetails originalDetails, CertificateDetails newDetails, string certNo, string edition)
        {
            var setClauses = new List<string>();
            var parameters = new List<SqlParameter>();

            // Helper to compare values and build clauses
            void AddClauseIfChanged(string originalValue, string newValue, string dbColumn)
            {
                // Use string.Equals to handle nulls gracefully
                if (!string.Equals(originalValue, newValue))
                {
                    setClauses.Add($"{dbColumn} = @{dbColumn}");
                    parameters.Add(new SqlParameter($"@{dbColumn}", newValue ?? (object)DBNull.Value));
                }
            }

            // Compare all fields
            AddClauseIfChanged(originalDetails.ProductNo, newDetails.ProductNo, "ProductNo");
            AddClauseIfChanged(originalDetails.ProductDescription, newDetails.ProductDescription, "ProductDescription");
            AddClauseIfChanged(originalDetails.SerialNo, newDetails.SerialNo, "SerialNo");
            AddClauseIfChanged(originalDetails.Serialization, newDetails.Serialization, "Serialization");
            AddClauseIfChanged(originalDetails.Amendment, newDetails.Amendment, "Amendment");
            AddClauseIfChanged(originalDetails.Signatory, newDetails.Signatory, "Signatory");
            AddClauseIfChanged(originalDetails.Date, newDetails.Date, "Date");
            AddClauseIfChanged(originalDetails.Edition, newDetails.Edition, "Edition");
            AddClauseIfChanged(originalDetails.Remarks1, newDetails.Remarks1, "Remarks1");
            AddClauseIfChanged(originalDetails.Remarks2, newDetails.Remarks2, "Remarks2");
            AddClauseIfChanged(originalDetails.Remarks3, newDetails.Remarks3, "Remarks3");
            AddClauseIfChanged(originalDetails.Remarks4, newDetails.Remarks4, "Remarks4");
            AddClauseIfChanged(originalDetails.Quantity, newDetails.Quantity, "Quantity");
            AddClauseIfChanged(originalDetails.Authorisation, newDetails.Authorisation, "Authorisation");
            AddClauseIfChanged(originalDetails.Item, newDetails.Item, "Item");
            AddClauseIfChanged(originalDetails.Status, newDetails.Status, "Status");
            AddClauseIfChanged(originalDetails.Approved, newDetails.Approved, "Approved");
            AddClauseIfChanged(originalDetails.State, newDetails.State, "State");
            AddClauseIfChanged(originalDetails.Comment, newDetails.Comment, "Comment");

            // If no fields were changed, no need to hit the database.
            if (setClauses.Count == 0)
            {
                _logger.LogInformation("Update for certificate {CertNo} was requested, but no fields were changed.", certNo);
                return true; // Report success as nothing needed to be done.
            }

            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                await using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    var sql = $"UPDATE Certificates SET {string.Join(", ", setClauses)} WHERE CertNo = @CertNo AND Edition = @Edition";

                    await using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddRange(parameters.ToArray());
                        command.Parameters.AddWithValue("@CertNo", certNo); // Add the WHERE clause parameter
                        command.Parameters.AddWithValue("@Edition", edition);


                        int rowsAffected = command.ExecuteNonQuery();
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dynamically updating certificate {CertNo} in database", certNo);
                throw; // Re-throw to be caught by the handler
            }
        }

        private async Task LogUpdateActionAsync(CertificateDetails originalDetails, CertificateDetails newDetails, string certNo, bool isCancellation = false)
        {
            
                // Check if any changes were made at all.
                var changesExist = originalDetails.GetType().GetProperties()
                    .Any(prop => !string.Equals(prop.GetValue(originalDetails)?.ToString(), prop.GetValue(newDetails)?.ToString()));

                if (!changesExist && !isCancellation)
                {
                    _logger.LogInformation("Update log for certificate {CertNo} skipped as no fields were changed.", certNo);
                    return; // Nothing to log
                }

                string connectionString = _configuration.GetConnectionString("SQLConnection");
                try
                {
                    await using (var conn = new SqlConnection(connectionString))
                    {
                        await conn.OpenAsync();
                        var logquery = @"
                        INSERT INTO Log_Certificates (
                            CertNo, Action, Performed_By, Datetime, ProductNo, ProductDescription, SerialNo, 
                            Serialization, Amendment, Signatory, Date, Edition, Remarks1, Remarks2, 
                            Remarks3, Remarks4, Quantity, Authorisation, Item, Status, Approved, State, Comment
                        ) VALUES (
                            @CertNo, @Action, @ID, @Time, @ProductNo, @ProductDescription, @SerialNo, 
                            @Serialization, @Amendment, @Signatory, @Date, @Edition, @Remarks1, @Remarks2, 
                            @Remarks3, @Remarks4, @Quantity, @Authorisation, @Item, @Status, @Approved, @State, @Comment
                        )";

                        await using (var cmd = new SqlCommand(logquery, conn))
                        {
                            // Helper function to add the parameter's value only if it has changed
                            // Corrected code
                            void AddParamIfChanged(string paramName, string originalValue, string newValue)
                            {
                                // Treat null, empty, and whitespace strings as equivalent.
                                if (string.IsNullOrWhiteSpace(originalValue) && string.IsNullOrWhiteSpace(newValue))
                                {
                                    cmd.Parameters.AddWithValue(paramName, DBNull.Value);
                                    return;
                                }

                                // Perform a direct comparison for non-empty values.
                                if (string.Equals(originalValue, newValue))
                                {
                                    cmd.Parameters.AddWithValue(paramName, DBNull.Value);
                                }
                                else
                                {
                                    cmd.Parameters.AddWithValue(paramName, (object)newValue ?? DBNull.Value);
                                }
                            }

                            // Add non-conditional parameters
                            cmd.Parameters.AddWithValue("@CertNo", certNo);
                            cmd.Parameters.AddWithValue("@Action", "Update");
                            cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));

                            if (isCancellation)
                            {
                                // Log that the state was changed to Cancelled
                                cmd.Parameters.AddWithValue("@State", "Cancelled");

                                // Add DBNull for all other parameters to satisfy the INSERT query
                                cmd.Parameters.AddWithValue("@ProductNo", DBNull.Value);
                                cmd.Parameters.AddWithValue("@ProductDescription", DBNull.Value);
                                cmd.Parameters.AddWithValue("@SerialNo", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Serialization", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Amendment", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Signatory", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Date", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Edition", (object)originalDetails.Edition ?? DBNull.Value); // Log the edition that was cancelled
                                cmd.Parameters.AddWithValue("@Remarks1", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Remarks2", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Remarks3", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Remarks4", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Quantity", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Authorisation", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Item", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Status", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Approved", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Comment", (object)newDetails.Comment ?? DBNull.Value);
                            }
                            else
                            {
                                // This part for regular updates remains unchanged
                                AddParamIfChanged("@ProductNo", originalDetails.ProductNo, newDetails.ProductNo);
                                AddParamIfChanged("@ProductDescription", originalDetails.ProductDescription, newDetails.ProductDescription);
                                AddParamIfChanged("@SerialNo", originalDetails.SerialNo, newDetails.SerialNo);
                                AddParamIfChanged("@Serialization", originalDetails.Serialization, newDetails.Serialization);
                                AddParamIfChanged("@Amendment", originalDetails.Amendment, newDetails.Amendment);
                                AddParamIfChanged("@Signatory", originalDetails.Signatory, newDetails.Signatory);
                                AddParamIfChanged("@Date", originalDetails.Date, newDetails.Date);
                                AddParamIfChanged("@Edition", originalDetails.Edition, newDetails.Edition);
                                AddParamIfChanged("@Remarks1", originalDetails.Remarks1, newDetails.Remarks1);
                                AddParamIfChanged("@Remarks2", originalDetails.Remarks2, newDetails.Remarks2);
                                AddParamIfChanged("@Remarks3", originalDetails.Remarks3, newDetails.Remarks3);
                                AddParamIfChanged("@Remarks4", originalDetails.Remarks4, newDetails.Remarks4);
                                AddParamIfChanged("@Quantity", originalDetails.Quantity, newDetails.Quantity);
                                AddParamIfChanged("@Authorisation", originalDetails.Authorisation, newDetails.Authorisation);
                                AddParamIfChanged("@Item", originalDetails.Item, newDetails.Item);
                                AddParamIfChanged("@Status", originalDetails.Status, newDetails.Status);
                                AddParamIfChanged("@Approved", originalDetails.Approved, newDetails.Approved);
                                AddParamIfChanged("@State", originalDetails.State, newDetails.State);
                                AddParamIfChanged("@Comment", originalDetails.Comment, newDetails.Comment);
                            }


                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error logging update action for certificate {CertNo}", certNo);
                }
            }
        private async Task LogPrintActionAsync(string certNo, string edition = null, bool stateChanged = false)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                await using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    var logquery = "INSERT INTO Log_Certificates (CertNo, Action, Performed_By, Datetime, State, Edition) VALUES (@CertNo, @Action, @ID, @Time, @State, @Edition)";
                    await using (SqlCommand cmd = new SqlCommand(logquery, conn))
                    {
                        cmd.Parameters.AddWithValue("@CertNo", certNo);
                        cmd.Parameters.AddWithValue("@Action", "Print");
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));

                        // Only log "Printed" state if it was actually changed
                        if (stateChanged)
                        {
                            cmd.Parameters.AddWithValue("@State", "Printed");

                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@State", DBNull.Value);

                        }


                        cmd.Parameters.AddWithValue("@Edition", edition ?? (object)DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging print action for certificate {CertNo}", certNo);
            }
        }

        private async Task<CertificateDetails> GetCertificateDetailsAsync(string certNo, string edition = null)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                // Use `await using` for async disposal
                await using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    // FIX: Use OpenAsync()
                    await connection.OpenAsync();

                    string sql;
                    if (!string.IsNullOrEmpty(edition))
                    {
                        // Get specific edition
                        sql = @"SELECT CertNo, ProductNo, ProductDescription, ProductType, Manufacturer, 
                       SerialNo, Serialization, Amendment, Signatory, Date, Edition, Quantity, 
                       Remarks1, Remarks2, Remarks3, Remarks4, Authorisation, Item, Status, 
                       Approved, State, Comment 
                       FROM Certificates 
                       WHERE CertNo = @CertNo AND Edition = @Edition";
                    }
                    else
                    {
                        // Get the certificate with the highest edition number (fallback for existing code)
                        sql = @"SELECT TOP 1 CertNo, ProductNo, ProductDescription, ProductType, Manufacturer, 
                       SerialNo, Serialization, Amendment, Signatory, Date, Edition, Quantity, 
                       Remarks1, Remarks2, Remarks3, Remarks4, Authorisation, Item, Status, 
                       Approved, State, Comment 
                       FROM Certificates 
                       WHERE CertNo = @CertNo 
                       ORDER BY CAST(Edition AS INT) DESC";
                    }

                    await using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.Add(new SqlParameter("@CertNo", certNo));
                        if (!string.IsNullOrEmpty(edition))
                        {
                            command.Parameters.Add(new SqlParameter("@Edition", edition));
                        }

                        await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            // FIX: Use ReadAsync()
                            if (await reader.ReadAsync())
                            {
                                return new CertificateDetails
                                {
                                    CertNo = reader["CertNo"]?.ToString() ?? "",
                                    ProductNo = reader["ProductNo"]?.ToString() ?? "",
                                    ProductDescription = reader["ProductDescription"]?.ToString() ?? "",
                                    ProductType = reader["ProductType"]?.ToString() ?? "",
                                    Manufacturer = reader["Manufacturer"]?.ToString() ?? "",
                                    SerialNo = reader["SerialNo"]?.ToString() ?? "",
                                    Serialization = reader["Serialization"]?.ToString() ?? "",
                                    Amendment = reader["Amendment"]?.ToString() ?? "",
                                    Signatory = reader["Signatory"]?.ToString() ?? "",
                                    Date = reader["Date"] != DBNull.Value ? Convert.ToDateTime(reader["Date"]).ToString("yyyy-MM-dd") : "",
                                    Quantity = reader["Quantity"]?.ToString() ?? "",
                                    Edition = reader["Edition"]?.ToString() ?? "",
                                    Remarks1 = reader["Remarks1"]?.ToString() ?? "",
                                    Remarks2 = reader["Remarks2"]?.ToString() ?? "",
                                    Remarks3 = reader["Remarks3"]?.ToString() ?? "",
                                    Remarks4 = reader["Remarks4"]?.ToString() ?? "",
                                    Authorisation = reader["Authorisation"]?.ToString() ?? "",
                                    Item = reader["Item"]?.ToString() ?? "",
                                    Status = reader["Status"]?.ToString() ?? "",
                                    Approved = reader["Approved"]?.ToString() ?? "",
                                    State = reader["State"]?.ToString() ?? "",
                                    Comment = reader["Comment"]?.ToString() ?? ""
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetCertificateDetails: {ex.Message}");
                throw;
            }
            return null;
        }


        private async Task<(List<CertificateSummary> results, int total)> SearchCertificatesAsync(int pageNumber, int pageSize)
        {
            var results = new List<CertificateSummary>();
            int totalResults = 0;
            string connectionString = _configuration.GetConnectionString("SQLConnection");

            await using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Build conditions and parameters once
                var conditions = new List<string>();
                var parameters = new List<SqlParameter>();

                if (!string.IsNullOrWhiteSpace(SearchCriteria.CertNo))
                {
                    conditions.Add("c.CertNo LIKE @CertNo");
                    parameters.Add(new SqlParameter("@CertNo", $"%{SearchCriteria.CertNo}%"));
                }

                if (!string.IsNullOrWhiteSpace(SearchCriteria.ProductNo))
                {
                    conditions.Add("c.ProductNo = @ProductNo");
                    parameters.Add(new SqlParameter("@ProductNo", SearchCriteria.ProductNo));
                }

                if (!string.IsNullOrWhiteSpace(SearchCriteria.SerialNo))
                {
                    conditions.Add("c.SerialNo LIKE @SerialNo");
                    parameters.Add(new SqlParameter("@SerialNo", $"%{SearchCriteria.SerialNo}%"));
                }

                if (SearchCriteria.Amendment != null && SearchCriteria.Amendment.Any())
                {
                    var amendmentValue = string.Join(", ", SearchCriteria.Amendment);
                    conditions.Add("c.Amendment = @Amendment");
                    parameters.Add(new SqlParameter("@Amendment", amendmentValue));
                }

                if (!string.IsNullOrWhiteSpace(SearchCriteria.Signatory))
                {
                    conditions.Add("c.Signatory = @Signatory");
                    parameters.Add(new SqlParameter("@Signatory", SearchCriteria.Signatory));
                }

                // Date filtering logic
                if (!string.IsNullOrWhiteSpace(SearchCriteria.StartDate) && !string.IsNullOrWhiteSpace(SearchCriteria.EndDate))
                {
                    conditions.Add("c.Date >= @StartDate AND c.Date <= @EndDate");
                    parameters.Add(new SqlParameter("@StartDate", DateTime.Parse(SearchCriteria.StartDate)));
                    parameters.Add(new SqlParameter("@EndDate", DateTime.Parse(SearchCriteria.EndDate).AddDays(1)));
                }
                else if (!string.IsNullOrWhiteSpace(SearchCriteria.StartDate))
                {
                    conditions.Add("c.Date >= @StartDate");
                    parameters.Add(new SqlParameter("@StartDate", DateTime.Parse(SearchCriteria.StartDate)));
                }
                else if (!string.IsNullOrWhiteSpace(SearchCriteria.EndDate))
                {
                    conditions.Add("c.Date <= @EndDate");
                    parameters.Add(new SqlParameter("@EndDate", DateTime.Parse(SearchCriteria.EndDate).AddDays(1)));
                }

                if (!string.IsNullOrWhiteSpace(SearchCriteria.Edition))
                {
                    conditions.Add("c.Edition LIKE @Edition");
                    parameters.Add(new SqlParameter("@Edition", $"%{SearchCriteria.Edition}%"));
                }

                if (!string.IsNullOrWhiteSpace(SearchCriteria.Quantity))
                {
                    conditions.Add("c.Quantity LIKE @Quantity");
                    parameters.Add(new SqlParameter("@Quantity", $"%{SearchCriteria.Quantity}%"));
                }

                if (!string.IsNullOrWhiteSpace(SearchCriteria.State))
                {
                    conditions.Add("c.State LIKE @State");
                    parameters.Add(new SqlParameter("@State", $"%{SearchCriteria.State}%"));
                }

                string whereClause = conditions.Count > 0 ? " AND " + string.Join(" AND ", conditions) : "";

                // OPTIMIZED: Single CTE-based query that gets both count and data in one execution
                var combinedSql = $@"
WITH MaxEditions AS (
    SELECT CertNo, MAX(CAST(Edition AS INT)) AS MaxEdition 
    FROM Certificates 
    GROUP BY CertNo
),
FilteredCertificates AS (
    SELECT
        c.CertNo, c.ProductNo, c.SerialNo, c.Amendment, c.Signatory, c.Date, 
        c.Edition, c.Quantity, c.State,
        CAST(c.Edition AS INT) AS EditionInt,
        me.MaxEdition,
        ROW_NUMBER() OVER (ORDER BY c.CertNo DESC, CAST(c.Edition AS INT) DESC) AS RowNum
    FROM Certificates c
    INNER JOIN MaxEditions me ON c.CertNo = me.CertNo
    WHERE 1=1{whereClause}
),
TotalCount AS (
    SELECT COUNT(*) AS Total FROM FilteredCertificates
)
SELECT 
    fc.CertNo, fc.ProductNo, fc.SerialNo, fc.Amendment, fc.Signatory, fc.Date, 
    fc.Edition, fc.Quantity, fc.State, fc.EditionInt, fc.MaxEdition,
    tc.Total
FROM FilteredCertificates fc
CROSS JOIN TotalCount tc
WHERE fc.RowNum BETWEEN @StartRow AND @EndRow
ORDER BY fc.CertNo DESC, fc.EditionInt DESC";

                // Calculate pagination parameters
                int startRow = (pageNumber - 1) * pageSize + 1;
                int endRow = pageNumber * pageSize;

                await using (SqlCommand command = new SqlCommand(combinedSql, connection))
                {
                    // Add search parameters
                    command.Parameters.AddRange(parameters.ToArray());

                    // Add pagination parameters
                    command.Parameters.AddWithValue("@StartRow", startRow);
                    command.Parameters.AddWithValue("@EndRow", endRow);

                    await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        bool totalSet = false;

                        while (await reader.ReadAsync())
                        {
                            // Set total count from first row (all rows have the same total)
                            if (!totalSet)
                            {
                                totalResults = Convert.ToInt32(reader["Total"]);
                                totalSet = true;
                            }

                            var editionInt = Convert.ToInt32(reader["EditionInt"]);
                            var maxEdition = Convert.ToInt32(reader["MaxEdition"]);

                            results.Add(new CertificateSummary
                            {
                                CertNo = reader["CertNo"].ToString(),
                                ProductNo = reader["ProductNo"].ToString(),
                                SerialNo = reader["SerialNo"].ToString(),
                                Amendment = reader["Amendment"].ToString(),
                                Signatory = reader["Signatory"].ToString(),
                                Date = reader["Date"] != DBNull.Value ? Convert.ToDateTime(reader["Date"]).ToString("dd MMM yyyy") : "",
                                Quantity = reader["Quantity"].ToString(),
                                Edition = reader["Edition"].ToString(),
                                State = reader["State"].ToString(),
                                IsLatestEdition = editionInt == maxEdition
                            });
                        }
                    }
                }
            }

            return (results, totalResults);
        }

        private async Task LoadDropdownsAsync()
        {
            // Define a unique key for the cached dropdown data.
            const string dropdownCacheKey = "SearchPageDropdowns_v1";

            // Use a tuple to store all the lists together in a single cache entry.
            // This is more efficient than caching each list separately.
            var cachedLists = await _cache.GetOrCreateAsync(dropdownCacheKey, async entry =>
            {
                // Set the cache expiration policies.
                // Keep the data for 10 minutes from the last access (sliding).
                // Force an absolute refresh every 1 hour to ensure data isn't too stale.
                entry.SlidingExpiration = TimeSpan.FromMinutes(2); // Keep for 2 mins from last access
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10); // Force refresh after 10 mins

                _logger.LogInformation("Cache miss for '{CacheKey}'. Fetching dropdowns from database.", dropdownCacheKey);

                // Initialize lists that will be populated and cached.
                var productNoList = new List<SelectListItem>();
                var productNoListNew = new List<SelectListItem>();
                var amendmentList = new List<SelectListItem>();
                var amendmentListNew = new List<SelectListItem>();
                var signatoryList = new List<SelectListItem>();
                var signatoryListNew = new List<SelectListItem>();
                var stateList = new List<SelectListItem>();
                var stateListNew = new List<SelectListItem>();
                var approvedList = new List<SelectListItem>();
                var approvedListNew = new List<SelectListItem>();
                var statusList = new List<SelectListItem>();
                var statusListNew = new List<SelectListItem>();

                // The combined SQL query remains the same.
                var sqlQuery = @"
            SELECT DISTINCT ProductNo FROM Certificates ORDER BY ProductNo;
            SELECT DISTINCT ProductNo FROM PartNumbers ORDER BY ProductNo;
            SELECT DISTINCT Amendment FROM Certificates ORDER BY Amendment;
            SELECT DISTINCT Amendment FROM Amendments ORDER BY Amendment;
            SELECT DISTINCT Signatory FROM Certificates ORDER BY Signatory;
            SELECT DISTINCT State FROM Certificates WHERE State IS NOT NULL AND State <> '' ORDER BY State;
            SELECT DISTINCT State FROM States ORDER BY State;
            SELECT DISTINCT Approved FROM Certificates ORDER BY Approved;
            SELECT DISTINCT ApprovedDesignIndicator FROM ApprovedDesignIndicators ORDER BY ApprovedDesignIndicator;
            SELECT DISTINCT Status FROM Certificates ORDER BY Status;
            SELECT DISTINCT Status FROM Statuses ORDER BY Status;
            SELECT DISTINCT Name FROM Users WHERE Role = @SignatoryRole ORDER BY Name;";

                string connectionString = _configuration.GetConnectionString("SQLConnection");

                try
                {
                    await using (var conn = new SqlConnection(connectionString))
                    {
                        await conn.OpenAsync();
                        await using (var cmd = new SqlCommand(sqlQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@SignatoryRole", 2);

                            await using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                await PopulateDropdownListFromReaderAsync(reader, productNoList, "ProductNo");
                                await reader.NextResultAsync();
                                await PopulateDropdownListFromReaderAsync(reader, productNoListNew, "ProductNo");
                                await reader.NextResultAsync();
                                await PopulateDropdownListFromReaderAsync(reader, amendmentList, "Amendment");
                                await reader.NextResultAsync();
                                await PopulateDropdownListFromReaderAsync(reader, amendmentListNew, "Amendment");
                                await reader.NextResultAsync();
                                await PopulateDropdownListFromReaderAsync(reader, signatoryList, "Signatory");
                                await reader.NextResultAsync();
                                await PopulateDropdownListFromReaderAsync(reader, stateList, "State");
                                await reader.NextResultAsync();
                                await PopulateDropdownListFromReaderAsync(reader, stateListNew, "State");
                                await reader.NextResultAsync();
                                await PopulateDropdownListFromReaderAsync(reader, approvedList, "Approved");
                                await reader.NextResultAsync();
                                await PopulateDropdownListFromReaderAsync(reader, approvedListNew, "ApprovedDesignIndicator");
                                await reader.NextResultAsync();
                                await PopulateDropdownListFromReaderAsync(reader, statusList, "Status");
                                await reader.NextResultAsync();
                                await PopulateDropdownListFromReaderAsync(reader, statusListNew, "Status");
                                await reader.NextResultAsync();
                                await PopulateDropdownListFromReaderAsync(reader, signatoryListNew, "Name");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while fetching and caching dropdown data.");
                    // Return empty lists on failure to prevent caching nulls, which could cause errors.
                    // The page will still load, just with empty dropdowns.
                }

                // Return the populated lists in a tuple to be cached.
                return (
                    productNoList, productNoListNew, amendmentList, amendmentListNew,
                    signatoryList, signatoryListNew, stateList, stateListNew,
                    approvedList, approvedListNew, statusList, statusListNew
                );
            });

            // Deconstruct the tuple from the cache (or the fresh DB query)
            // and assign the lists to the PageModel properties.
            (
                ProductNoList, ProductNoListNew, AmendmentList, AmendmentListNew,
                SignatoryList, SignatoryListNew, StateList, StateListNew,
                ApprovedList, ApprovedListNew, StatusList, StatusListNew
            ) = cachedLists;
        }


        /// <summary>
        /// Populates a List of SelectListItem from the current result set of a SqlDataReader.
        /// </summary>

        private async Task PopulateDropdownListFromReaderAsync(SqlDataReader reader, List<SelectListItem> list, string columnName)
        {
            while (await reader.ReadAsync()) // FIX
            {
                var value = reader[columnName]?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    list.Add(new SelectListItem { Value = value, Text = value });
                }
            }
        }
    }
}