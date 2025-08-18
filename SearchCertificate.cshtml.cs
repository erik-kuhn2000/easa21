using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
// --- PDFSHARP USINGS ---
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

        public SearchModel(IConfiguration configuration, ILogger<SearchModel> logger, IAuthorizationService authorizationService, IWebHostEnvironment hostingEnvironment)
        {
            _configuration = configuration;
            _logger = logger;
            _authorizationService = authorizationService;
            _hostingEnvironment = hostingEnvironment;

            string fontPath = Path.Combine(_hostingEnvironment.WebRootPath, "Fonts");
            FontResolver.Initialize(fontPath);
            GlobalFontSettings.FontResolver = new FontResolver();
        }

        [Authorize(Policy = "SignatoryAccess")]
        [Authorize(Policy = "SignatoryAccess")]
        public async Task<IActionResult> OnPostPrintCertificate(string certNo, string edition = null)
        {
            // Step 1: Get the certificate's original details for logging
            var originalDetails = GetCertificateDetails(certNo, edition);
            if (originalDetails == null)
            {
                return NotFound("Certificate not found.");
            }

            // Step 2: Update the state to "Printed" ONLY if current state is "Valid"
            bool stateChanged = false;
            if (string.Equals(originalDetails.State, "Valid", StringComparison.OrdinalIgnoreCase))
            {
                var stateUpdateSuccess = UpdateCertificateState(certNo, "Printed", edition);
                if (stateUpdateSuccess)
                {
                    stateChanged = true;
                    // Update the object for PDF generation
                    originalDetails.State = "Printed";
                }
            }

            // Step 3: Log the print action (regardless of state change)
            LogPrintAction(certNo, edition, stateChanged);

            // Step 4: Generate the PDF and return it
            byte[] pdfBytes = GeneratePdfCertificate(originalDetails);

            return File(
                pdfBytes,
                "application/pdf",
                $"Certificate_{certNo}_Ed{edition}_{DateTime.Now:ddMMMyyyy_hhmmss}.pdf"
            );
        }

        private string GetCurrentUserSignatoryName()
        {
            var userId = User.Identity?.Name;
            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT Name FROM Users WHERE TGI = @UserId AND Role = @SignatoryRole";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@SignatoryRole", 2); // Assuming 2 is the Signatory Role ID
                    return cmd.ExecuteScalar()?.ToString();
                }
            }
        }
        private byte[] GeneratePdfCertificate(CertificateDetails details)
        {
            string relativePath = _configuration["FilePaths:TemplatePath"];
            string webRootPath = _hostingEnvironment.WebRootPath;
            string templatePath = Path.Combine(webRootPath, relativePath);

            if (!System.IO.File.Exists(templatePath))
            {
                throw new FileNotFoundException("PDF template not found at the specified path.", templatePath);
            }

            PdfDocument document = PdfReader.Open(templatePath, PdfDocumentOpenMode.Modify);

            if (document.AcroForm != null)
            {
                var form = document.AcroForm;

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
                else
                {SetFormField(form, "amendment", details.Amendment, "Arial", 10); }

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

            using (MemoryStream stream = new MemoryStream())
            {
                document.Save(stream, false);
                return stream.ToArray();
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

        public void OnGet()
        {
            LoadDropdowns();
            CurrentUserSignatoryName = GetCurrentUserSignatoryName();

        }

        public IActionResult OnPost(string handler)
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
                            LoadDropdowns();
                            return Page();
                        }
                    }
                }

                if (PageNumber < 1) PageNumber = 1;

                SearchResults = SearchCertificates(PageNumber, PageSize, out int totalResults);
                TotalResults = totalResults;
                Searched = true;

                if (SearchResults == null || !SearchResults.Any())
                {
                    SearchErrorMessage = "No certificates found matching your criteria.";
                }
            }
            else if (handler == "export")
            {
                // Delegate to the updated export handler
                return OnPostExport();
            }
            LoadDropdowns();
            CurrentUserSignatoryName = GetCurrentUserSignatoryName();
            return Page();
        }

        private bool UpdateCertificateState(string certNo, string newState, string edition = null)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string sql;
                    if (!string.IsNullOrEmpty(edition))
                    {
                        sql = "UPDATE Certificates SET State = @NewState WHERE CertNo = @CertNo AND Edition = @Edition";
                    }
                    else
                    {
                        sql = "UPDATE Certificates SET State = @NewState WHERE CertNo = @CertNo";
                    }

                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@NewState", newState ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@CertNo", certNo);
                        if (!string.IsNullOrEmpty(edition))
                        {
                            command.Parameters.AddWithValue("@Edition", edition);
                        }
                        int rowsAffected = command.ExecuteNonQuery();
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

        public IActionResult OnPostExport()
        {
            var allResults = GetAllCertificatesForExport();

            if (!allResults.Any())
            {
                SearchErrorMessage = "No results to export.";
                LoadDropdowns();
                return Page();
            }

            // Using ClosedXML to create the Excel file
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Certificate Details");
                int currentRow = 1;

                // Add headers in the first row
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

                // Style the header row
                var headerRow = worksheet.Row(currentRow);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;
                headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Add data rows
                foreach (var cert in allResults)
                {
                    currentRow++;
                    int col = 1;
                    worksheet.Cell(currentRow, col++).Value = cert.CertNo;
                    worksheet.Cell(currentRow, col++).Value = cert.Edition;
                    worksheet.Cell(currentRow, col++).Value = cert.Item;
                    worksheet.Cell(currentRow, col++).Value = cert.ProductDescription;
                    worksheet.Cell(currentRow, col++).Value = cert.ProductNo;
                    worksheet.Cell(currentRow, col++).Value = cert.Quantity;
                    worksheet.Cell(currentRow, col++).Value = cert.SerialNo;
                    worksheet.Cell(currentRow, col++).Value = cert.Status;
                    worksheet.Cell(currentRow, col++).Value = cert.Remarks1;
                    worksheet.Cell(currentRow, col++).Value = cert.Remarks2;
                    worksheet.Cell(currentRow, col++).Value = cert.Remarks3;
                    worksheet.Cell(currentRow, col++).Value = cert.Remarks4;
                    worksheet.Cell(currentRow, col++).Value = cert.Amendment;
                    worksheet.Cell(currentRow, col++).Value = cert.Approved;
                    worksheet.Cell(currentRow, col++).Value = cert.Authorisation;
                    worksheet.Cell(currentRow, col++).Value = cert.Signatory;
                    worksheet.Cell(currentRow, col++).Value = cert.Date;
                    worksheet.Cell(currentRow, col++).Value = cert.ProductType;
                    worksheet.Cell(currentRow, col++).Value = cert.Manufacturer;
                    worksheet.Cell(currentRow, col++).Value = cert.Serialization;
                    worksheet.Cell(currentRow, col++).Value = cert.State;
                    worksheet.Cell(currentRow, col++).Value = cert.Comment;

                }

                // Auto-fit columns for better readability
                worksheet.Columns().AdjustToContents();

                // Save to a memory stream
                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string excelName = $"Certificates-Export-{DateTime.Now:ddMMMyyyy_hh:mm:ss}.xlsx";

                    // Return the stream as a file to the browser
                    return File(
                        content,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        excelName
                    );
                }
            }
        }

        private List<CertificateDetails> GetAllCertificatesForExport()
        {
            var results = new List<CertificateDetails>();
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                // Removed the CTE to get all versions instead of just latest
                var selectSql = @"SELECT CertNo, ProductNo, SerialNo, Amendment, Signatory, Date, Edition,Remarks1, Remarks2, Remarks3, Remarks4,  Quantity, Authorisation, Item, Status,Approved,  ProductDescription,Serialization, ProductType, Manufacturer, State, Comment FROM Certificates WHERE 1=1";
                var conditions = new List<string>();
                var parameters = new List<SqlParameter>();

                // Query building logic remains the same
                if (!string.IsNullOrWhiteSpace(SearchCriteria.CertNo)) { conditions.Add("CertNo LIKE @CertNo"); parameters.Add(new SqlParameter("@CertNo", $"%{SearchCriteria.CertNo}%")); }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.ProductNo)) { conditions.Add("ProductNo = @ProductNo"); parameters.Add(new SqlParameter("@ProductNo", SearchCriteria.ProductNo)); }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.Edition)) { conditions.Add("Edition LIKE @Edition"); parameters.Add(new SqlParameter("@Edition", SearchCriteria.Edition)); }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.Quantity)) { conditions.Add("Quantity LIKE @Quantity"); parameters.Add(new SqlParameter("@Quantity", SearchCriteria.Quantity)); }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.SerialNo)) { conditions.Add("SerialNo LIKE @SerialNo"); parameters.Add(new SqlParameter("@SerialNo", SearchCriteria.SerialNo)); }
                // Handle the List<string> for Amendment search
                if (SearchCriteria.Amendment != null && SearchCriteria.Amendment.Any())
                {
                    var amendmentValue = string.Join(", ", SearchCriteria.Amendment);
                    conditions.Add("Amendment = @Amendment");
                    parameters.Add(new SqlParameter("@Amendment", amendmentValue));
                }

                // Date filtering logic
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


                if (conditions.Count > 0)
                {
                    selectSql += " AND " + string.Join(" AND ", conditions);
                }
                // Order by CertNo DESC, then by Edition DESC to show latest versions first for each certificate
                selectSql += " ORDER BY CertNo DESC, CAST(Edition AS INT) DESC";

                using (SqlCommand selectCommand = new SqlCommand(selectSql, connection))
                {
                    selectCommand.Parameters.AddRange(parameters.ToArray());
                    using (SqlDataReader reader = selectCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(new CertificateDetails
                            {
                                CertNo = reader["CertNo"].ToString(),
                                ProductNo = reader["ProductNo"].ToString(),
                                ProductDescription = reader["ProductDescription"].ToString(),
                                ProductType = reader["ProductType"].ToString(),
                                Manufacturer = reader["Manufacturer"].ToString(),
                                SerialNo = reader["SerialNo"].ToString(),
                                Serialization = reader["Serialization"].ToString(),
                                Amendment = reader["Amendment"].ToString(),
                                Signatory = reader["Signatory"].ToString(),
                                Date = reader["Date"] != DBNull.Value ? Convert.ToDateTime(reader["Date"]).ToString("dd MMM yyyy") : "",
                                Quantity = reader["Quantity"].ToString(),
                                Edition = reader["Edition"].ToString(),
                                Remarks1 = reader["Remarks1"].ToString(),
                                Remarks2 = reader["Remarks2"].ToString(),
                                Remarks3 = reader["Remarks3"].ToString(),
                                Remarks4 = reader["Remarks4"].ToString(),
                                Authorisation = reader["Authorisation"].ToString(),
                                Item = reader["Item"].ToString(),
                                Status = reader["Status"].ToString(),
                                Approved = reader["Approved"].ToString(),
                                State = reader["State"].ToString(),
                                Comment = reader["Comment"].ToString()
                            });
                        }
                    }
                }
            }
            return results;
        }

        public async Task<bool> IsUserAuthorizedToUpdate(string certNo)
        {
            // CHANGED: This now checks the user's general permission policy ("SignatoryAccess")
            // instead of checking against a specific certificate number.
            var authorizationResult = await _authorizationService.AuthorizeAsync(User, "SignatoryRoleRequired");
            return authorizationResult.Succeeded;
        }


        public IActionResult OnGetCertificateDetails(string certNo, string edition = null)
        {
            if (string.IsNullOrWhiteSpace(certNo))
            {
                return new JsonResult(new { success = false, message = "Certificate number is required." });
            }

            try
            {
                var certificateDetails = GetCertificateDetails(certNo, edition);
                if (certificateDetails == null)
                {
                    return new JsonResult(new { success = false, message = "Certificate not found." });
                }

                return new JsonResult(new { success = true, data = certificateDetails });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"Database error: {ex.Message}" });
            }
        }


        public async Task<IActionResult> OnPostUpdateCertificate(
      string certNo, string productNo, string productDescription,
      string serialNo, string serialization, string amendment, string signatory, string date,
      string edition, string remarks1, string remarks2, string remarks3, string remarks4,
      string quantity, string authorisation, string item, string status, string approved,
      string state, string comment, bool incrementEdition)
        {
            var originalDetails = GetCertificateDetails(certNo);
            if (originalDetails == null)
            {
                return new JsonResult(new { success = false, message = "Certificate not found." });
            }

            string currentUserSignatory = GetCurrentUserSignatoryName();
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
                else if (int.TryParse(edition, out int editionNum))
                {
                    // Always increment edition when creating new entry
                    editionNum++;

                    if (editionNum < 0 || editionNum > 99)
                    {
                        errors.Add("Edition increment would result in a value outside the range 00-99.");
                    }
                    else
                    {
                        edition = editionNum.ToString("D2");
                    }
                }
                else
                {
                    errors.Add("Edition has an invalid format.");
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
                    State = "Valid",
                    Comment = comment,
                    ProductType = originalDetails.ProductType,
                    Manufacturer = originalDetails.Manufacturer,
                };

                var success = InsertNewCertificateVersion(originalDetails, newDetails, certNo);

                if (success)
                {
                    LogUpdateAction(originalDetails, newDetails, certNo);
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

        // Add this new method to insert a new certificate version
        private bool InsertNewCertificateVersion(CertificateDetails originalDetails, CertificateDetails newDetails, string certNo)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

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

                    using (SqlCommand command = new SqlCommand(sql, connection))
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

        private bool UpdateCertificateInDatabase(CertificateDetails originalDetails, CertificateDetails newDetails, string certNo)
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
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    // Dynamically join the SET clauses
                    var sql = $"UPDATE Certificates SET {string.Join(", ", setClauses)} WHERE CertNo = @CertNo";

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddRange(parameters.ToArray());
                        command.Parameters.AddWithValue("@CertNo", certNo); // Add the WHERE clause parameter

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

        private void LogUpdateAction(CertificateDetails originalDetails, CertificateDetails newDetails, string certNo)
        {
            try
            {
                // Check if any changes were made at all.
                var changesExist = originalDetails.GetType().GetProperties()
                    .Any(prop => !string.Equals(prop.GetValue(originalDetails)?.ToString(), prop.GetValue(newDetails)?.ToString()));

                if (!changesExist)
                {
                    _logger.LogInformation("Update log for certificate {CertNo} skipped as no fields were changed.", certNo);
                    return; // Nothing to log
                }

                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
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

                    using (SqlCommand cmd = new SqlCommand(logquery, conn))
                    {
                        // Helper function to add the parameter's value only if it has changed
                        void AddParamIfChanged(string paramName, string originalValue, string newValue)
                        {
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

                        // Use the helper for each certificate field
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

                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging update action for certificate {CertNo}", certNo);
            }
        }
        private void LogPrintAction(string certNo, string edition = null, bool stateChanged = false)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                var logquery = "INSERT INTO Log_Certificates (CertNo, Action, Performed_By, Datetime, State, Edition) VALUES (@CertNo, @Action, @ID, @Time, @State, @Edition)";
                using (SqlCommand cmd = new SqlCommand(logquery, conn))
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
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private CertificateDetails GetCertificateDetails(string certNo, string edition = null)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

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

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.Add(new SqlParameter("@CertNo", certNo));
                        if (!string.IsNullOrEmpty(edition))
                        {
                            command.Parameters.Add(new SqlParameter("@Edition", edition));
                        }

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
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


        private List<CertificateSummary> SearchCertificates(int pageNumber, int pageSize, out int totalResults)
        {
            var results = new List<CertificateSummary>();
            totalResults = 0;
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Modified query to include max edition information
                var countSql = @"SELECT COUNT(*) FROM Certificates WHERE 1=1";
                var selectSql = @"SELECT CertNo, ProductNo, SerialNo, Amendment, Signatory, Date, Edition, Quantity, State,
                         CAST(Edition AS INT) as EditionInt,
                         MAX(CAST(Edition AS INT)) OVER (PARTITION BY CertNo) as MaxEdition
                         FROM Certificates WHERE 1=1";

                var conditions = new List<string>();
                var parameters = new List<SqlParameter>();

                // Your existing search conditions remain the same
                if (!string.IsNullOrWhiteSpace(SearchCriteria.CertNo)) { conditions.Add("CertNo LIKE @CertNo"); parameters.Add(new SqlParameter("@CertNo", $"%{SearchCriteria.CertNo}%")); }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.ProductNo)) { conditions.Add("ProductNo = @ProductNo"); parameters.Add(new SqlParameter("@ProductNo", SearchCriteria.ProductNo)); }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.SerialNo)) { conditions.Add("SerialNo LIKE @SerialNo"); parameters.Add(new SqlParameter("@SerialNo", $"%{SearchCriteria.SerialNo}%")); }
                if (SearchCriteria.Amendment != null && SearchCriteria.Amendment.Any())
                {
                    var amendmentValue = string.Join(", ", SearchCriteria.Amendment);
                    conditions.Add("Amendment = @Amendment");
                    parameters.Add(new SqlParameter("@Amendment", amendmentValue));
                }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.Signatory)) { conditions.Add("Signatory = @Signatory"); parameters.Add(new SqlParameter("@Signatory", SearchCriteria.Signatory)); }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.StartDate) && !string.IsNullOrWhiteSpace(SearchCriteria.EndDate)) { conditions.Add("CONVERT(date, Date) >= @StartDate AND CONVERT(date, Date) <= @EndDate"); parameters.Add(new SqlParameter("@StartDate", DateTime.Parse(SearchCriteria.StartDate))); parameters.Add(new SqlParameter("@EndDate", DateTime.Parse(SearchCriteria.EndDate))); }
                else if (!string.IsNullOrWhiteSpace(SearchCriteria.StartDate)) { conditions.Add("CONVERT(date, Date) >= @StartDate"); parameters.Add(new SqlParameter("@StartDate", DateTime.Parse(SearchCriteria.StartDate))); }
                else if (!string.IsNullOrWhiteSpace(SearchCriteria.EndDate)) { conditions.Add("CONVERT(date, Date) <= @EndDate"); parameters.Add(new SqlParameter("@EndDate", DateTime.Parse(SearchCriteria.EndDate))); }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.Edition)) { conditions.Add("Edition LIKE @Edition"); parameters.Add(new SqlParameter("@Edition", $"%{SearchCriteria.Edition}%")); }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.Quantity)) { conditions.Add("Quantity LIKE @Quantity"); parameters.Add(new SqlParameter("@Quantity", $"%{SearchCriteria.Quantity}%")); }
                if (!string.IsNullOrWhiteSpace(SearchCriteria.State)) { conditions.Add("State LIKE @State"); parameters.Add(new SqlParameter("@State", $"%{SearchCriteria.State}%")); }

                if (conditions.Count > 0)
                {
                    var whereClause = " AND " + string.Join(" AND ", conditions);
                    countSql += whereClause;
                    selectSql += whereClause;
                }

                // Order by CertNo DESC, then by Edition DESC to show latest versions first for each certificate
                selectSql += " ORDER BY CertNo DESC, CAST(Edition AS INT) DESC";

                // Get total count
                using (SqlCommand countCommand = new SqlCommand(countSql, connection))
                {
                    foreach (var param in parameters) { countCommand.Parameters.Add(new SqlParameter(param.ParameterName, param.Value)); }
                    object countResult = countCommand.ExecuteScalar();
                    totalResults = countResult != null ? Convert.ToInt32(countResult) : 0;
                }

                // Get paginated results
                int offset = (pageNumber - 1) * pageSize;
                selectSql += " OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                using (SqlCommand selectCommand = new SqlCommand(selectSql, connection))
                {
                    selectCommand.Parameters.AddRange(parameters.ToArray());
                    selectCommand.Parameters.AddWithValue("@Offset", offset);
                    selectCommand.Parameters.AddWithValue("@PageSize", pageSize);
                    using (SqlDataReader reader = selectCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
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
                                IsLatestEdition = editionInt == maxEdition // Add this property
                            });
                        }
                    }
                }
            }
            return results;
        }

        private void LoadDropdowns()
        {
            ProductNoList = new List<SelectListItem>();
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("SQLConnection"))) { conn.Open(); string query = "SELECT DISTINCT ProductNo FROM Certificates ORDER BY ProductNo"; using (SqlCommand cmd = new SqlCommand(query, conn)) using (SqlDataReader reader = cmd.ExecuteReader()) { while (reader.Read()) { ProductNoList.Add(new SelectListItem { Value = reader["ProductNo"].ToString(), Text = reader["ProductNo"].ToString() }); } } }
            ProductNoListNew = new List<SelectListItem>();
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("SQLConnection"))) { conn.Open(); string query = "SELECT DISTINCT ProductNo FROM PartNumbers ORDER BY ProductNo"; using (SqlCommand cmd = new SqlCommand(query, conn)) using (SqlDataReader reader = cmd.ExecuteReader()) { while (reader.Read()) { ProductNoListNew.Add(new SelectListItem { Value = reader["ProductNo"].ToString(), Text = reader["ProductNo"].ToString() }); } } }
            AmendmentList = new List<SelectListItem>();
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("SQLConnection"))) { conn.Open(); string query = "SELECT DISTINCT Amendment FROM Certificates ORDER BY Amendment"; using (SqlCommand cmd = new SqlCommand(query, conn)) using (SqlDataReader reader = cmd.ExecuteReader()) { while (reader.Read()) { AmendmentList.Add(new SelectListItem { Value = reader["Amendment"].ToString(), Text = reader["Amendment"].ToString() }); } } }
            AmendmentListNew = new List<SelectListItem>();
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("SQLConnection"))) { conn.Open(); string query = "SELECT DISTINCT Amendment FROM Amendments ORDER BY Amendment"; using (SqlCommand cmd = new SqlCommand(query, conn)) using (SqlDataReader reader = cmd.ExecuteReader()) { while (reader.Read()) { AmendmentListNew.Add(new SelectListItem { Value = reader["Amendment"].ToString(), Text = reader["Amendment"].ToString() }); } } }
            SignatoryList = new List<SelectListItem>();
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("SQLConnection"))) { conn.Open(); string query = "SELECT DISTINCT Signatory FROM Certificates ORDER BY Signatory"; using (SqlCommand cmd = new SqlCommand(query, conn)) using (SqlDataReader reader = cmd.ExecuteReader()) { while (reader.Read()) { SignatoryList.Add(new SelectListItem { Value = reader["Signatory"].ToString(), Text = reader["Signatory"].ToString() }); } } }
            SignatoryListNew = new List<SelectListItem>();
            
            StateList = new List<SelectListItem>();
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("SQLConnection"))) { conn.Open(); string query = "SELECT DISTINCT State FROM Certificates WHERE State IS NOT NULL AND State != '' ORDER BY State"; using (SqlCommand cmd = new SqlCommand(query, conn)) using (SqlDataReader reader = cmd.ExecuteReader()) { while (reader.Read()) { StateList.Add(new SelectListItem { Value = reader["State"].ToString(), Text = reader["State"].ToString() }); } } }
            StateListNew = new List<SelectListItem>();
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("SQLConnection"))) { conn.Open(); string query = "SELECT DISTINCT State FROM States ORDER BY State"; using (SqlCommand cmd = new SqlCommand(query, conn)) using (SqlDataReader reader = cmd.ExecuteReader()) { while (reader.Read()) { StateListNew.Add(new SelectListItem { Value = reader["State"].ToString(), Text = reader["State"].ToString() }); } } }


            ApprovedList = new List<SelectListItem>();
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("SQLConnection"))) { conn.Open(); string query = "SELECT DISTINCT Approved FROM Certificates ORDER BY Approved"; using (SqlCommand cmd = new SqlCommand(query, conn)) using (SqlDataReader reader = cmd.ExecuteReader()) { while (reader.Read()) { ApprovedList.Add(new SelectListItem { Value = reader["Approved"].ToString(), Text = reader["Approved"].ToString() }); } } }
            ApprovedListNew = new List<SelectListItem>();
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("SQLConnection"))) { conn.Open(); string query = "SELECT DISTINCT ApprovedDesignIndicator FROM ApprovedDesignIndicators ORDER BY ApprovedDesignIndicator"; using (SqlCommand cmd = new SqlCommand(query, conn)) using (SqlDataReader reader = cmd.ExecuteReader()) { while (reader.Read()) {ApprovedListNew.Add(new SelectListItem { Value = reader["ApprovedDesignIndicator"].ToString(), Text = reader["ApprovedDesignIndicator"].ToString() }); } } }


            StatusList = new List<SelectListItem>();
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("SQLConnection"))) { conn.Open(); string query = "SELECT DISTINCT Status FROM Certificates ORDER BY Status"; using (SqlCommand cmd = new SqlCommand(query, conn)) using (SqlDataReader reader = cmd.ExecuteReader()) { while (reader.Read()) { StatusList.Add(new SelectListItem { Value = reader["Status"].ToString(), Text = reader["Status"].ToString() }); } } }
            StatusListNew = new List<SelectListItem>();
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("SQLConnection"))) { conn.Open(); string query = "SELECT DISTINCT Status FROM Statuses ORDER BY Status"; using (SqlCommand cmd = new SqlCommand(query, conn)) using (SqlDataReader reader = cmd.ExecuteReader()) { while (reader.Read()) { StatusListNew.Add(new SelectListItem { Value = reader["Status"].ToString(), Text = reader["Status"].ToString() }); } } }
            
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            var userId = User.Identity?.Name;





            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT DISTINCT Name FROM Users WHERE Role = @SignatoryRole ORDER BY Name";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    // Assuming '2' is the Role ID for Signatories in your Users table.
                    cmd.Parameters.AddWithValue("@SignatoryRole", 2);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            SignatoryListNew.Add(new SelectListItem { Value = reader["Name"].ToString(), Text = reader["Name"].ToString() });
                        }
                    }
                }
            }
        }
    }
    }
