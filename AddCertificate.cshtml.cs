using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;


namespace QApp.Pages
{
    [Authorize(Policy = "SignatoryRoleRequired")]
    public class AddModel : PageModel
    {
        // BindProperty attributes remain the same
        [BindProperty] public string ProductNo { get; set; }
        public List<SelectListItem> ProductNoList { get; set; }

        [BindProperty] public string ProductDescription { get; set; }
        [BindProperty] public string ProductType { get; set; }
        [BindProperty] public string Manufacturer { get; set; }
        [BindProperty] public string SerialNo { get; set; }
        [BindProperty] public string Serialization { get; set; }

        [BindProperty] public List<string> Amendment { get; set; }
        public List<SelectListItem> AmendmentList { get; set; }

        [BindProperty] public string Signatory { get; set; }
        [BindProperty] public string Date { get; set; }
        [BindProperty] public string Edition { get; set; }
        [BindProperty] public string Remarks1 { get; set; }
        [BindProperty] public string Remarks2 { get; set; }
        [BindProperty] public string Remarks3 { get; set; }
        [BindProperty] public string Remarks4 { get; set; }
        [BindProperty] public string Quantity { get; set; }
        [BindProperty] public string Authorisation { get; set; }
        [BindProperty] public string Item { get; set; }
        [BindProperty] public string Status { get; set; }
        [BindProperty] public string Approved { get; set; }
        public List<SelectListItem> ApprovedList { get; set; }
        public List<SelectListItem> StatusList { get; set; }
        [BindProperty] public string Comment { get; set; }
        [BindProperty] public string State { get; set; }
        public List<SelectListItem> StatesList { get; set; }

        public string SuccessMessage { get; set; }
        public bool IsSuccess { get; set; } = false;

        private readonly ILogger<AddModel> _logger;
        private readonly IConfiguration _configuration;

        // IAuthorizationService is not used in the provided code, but kept in the constructor
        private readonly IAuthorizationService _authorizationService;

        public AddModel(IConfiguration configuration, IAuthorizationService authorizationService, ILogger<AddModel> logger)
        {
            _configuration = configuration;
            _authorizationService = authorizationService;
            _logger = logger;
        }

        // OnGet is now async
        public async Task OnGetAsync()
        {
            await LoadInitialDataAsync();

            if (TempData["SuccessMessage"] != null)
            {
                SuccessMessage = TempData["SuccessMessage"].ToString();
                IsSuccess = true;
            }
        }

        // OnPost is now async and returns Task<IActionResult>
        public async Task<IActionResult> OnPostAsync()
        {
            // Load dropdowns again in case of validation error and page reload
            await LoadInitialDataAsync();

            // --- Validation Logic (unchanged) ---
            if (string.IsNullOrWhiteSpace(ProductNo) ||
                string.IsNullOrWhiteSpace(SerialNo) ||
                Amendment == null || !Amendment.Any() ||
                string.IsNullOrWhiteSpace(Signatory) ||
                string.IsNullOrWhiteSpace(Date) ||
                string.IsNullOrWhiteSpace(Edition) ||
                string.IsNullOrWhiteSpace(Quantity) ||
                string.IsNullOrWhiteSpace(Approved) ||
                string.IsNullOrWhiteSpace(Status))
            {
                ModelState.AddModelError(string.Empty, "All mandatory fields need to be filled.");
                return Page();
            }
            if (string.IsNullOrWhiteSpace(Edition) || !System.Text.RegularExpressions.Regex.IsMatch(Edition, @"^(0[0-9]|1[0-2])$"))
            {
                ModelState.AddModelError(nameof(Edition), "Wrong input format.");
                return Page();
            }
            if (int.TryParse(Quantity, out int qtyValue))
            {
                if (qtyValue > 0 && qtyValue <= 9999)
                {
                    Quantity = qtyValue.ToString("D2");
                }
                else
                {
                    ModelState.AddModelError(nameof(Quantity), "Quantity must be a positive number.");
                    return Page();
                }
            }
            else
            {
                ModelState.AddModelError(nameof(Quantity), "Quantity must be a valid number.");
                return Page();
            }
            // --- End of Validation Logic ---

            string certNo = null;
            string connectionString = _configuration.GetConnectionString("SQLConnection");

            // All database operations are now within a single connection block for efficiency
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                // 1. Get Product Details from Database
                string productDescriptionFromDb = null;
                string productTypeFromDb = null;
                string manufacturerFromDb = null;
                string serializationFromDb = null;

                string detailsQuery = "SELECT ProductDesc, ProductType, Manufacturer, Serialization FROM PartNumbers WHERE ProductNo = @ProductNo";
                using (var cmd = new SqlCommand(detailsQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@ProductNo", ProductNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            productDescriptionFromDb = reader["ProductDesc"]?.ToString();
                            productTypeFromDb = reader["ProductType"]?.ToString();
                            manufacturerFromDb = reader["Manufacturer"]?.ToString();
                            serializationFromDb = reader["Serialization"]?.ToString();
                        }
                    }
                }

                // 2. Generate the next Certificate Number
                int currentYear = DateTime.Now.Year;
                string prefixQuery = "SELECT Code FROM Prefixes WHERE Year=@Year";
                var prefixCmd = new SqlCommand(prefixQuery, conn);
                prefixCmd.Parameters.AddWithValue("@Year", currentYear);
                var prefix = (await prefixCmd.ExecuteScalarAsync())?.ToString();

                if (string.IsNullOrEmpty(prefix))
                {
                    throw new Exception("No prefix found for the current year.");
                }

                string certNoQuery = "SELECT TOP 1 CertNo FROM Certificates WHERE CertNo LIKE @PrefixLike ORDER BY CertNo DESC";
                var certNoCmd = new SqlCommand(certNoQuery, conn);
                certNoCmd.Parameters.AddWithValue("@PrefixLike", prefix + "93%");
                var latestCertNo = (await certNoCmd.ExecuteScalarAsync())?.ToString();

                int nextNumber = 6000;
                if (!string.IsNullOrEmpty(latestCertNo) && latestCertNo.Length >= 4 && int.TryParse(latestCertNo.Substring(latestCertNo.Length - 4), out int lastFour))
                {
                    if (lastFour >= 6000 && lastFour < 9999)
                    {
                        nextNumber = lastFour + 1;
                    }
                }
                certNo = prefix + "93" + nextNumber.ToString("D4");

                // 3. Insert new certificate record
                string amendmentValue = string.Join(", ", Amendment);
                var insertQuery = @"INSERT INTO Certificates (CertNo, ProductNo, ProductDescription, ProductType, Manufacturer, SerialNo, Serialization, Amendment, Signatory, Date, Edition, Remarks1, Remarks2, Remarks3, Remarks4, Quantity, Authorisation, Item, Status, Approved, State, Comment) 
                                    VALUES (@CertNo, @ProductNo, @ProductDescription, @ProductType, @Manufacturer, @SerialNo, @Serialization, @Amendment, @Signatory, @Date, @Edition, @Remarks1, @Remarks2, @Remarks3, @Remarks4, @Quantity, @Authorisation, @Item, @Status, @Approved, @State, @Comment)";
                using (var insertCmd = new SqlCommand(insertQuery, conn))
                {
                    insertCmd.Parameters.AddWithValue("@CertNo", certNo);
                    insertCmd.Parameters.AddWithValue("@ProductNo", ProductNo);
                    insertCmd.Parameters.AddWithValue("@ProductDescription", productDescriptionFromDb ?? (object)DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@ProductType", productTypeFromDb ?? (object)DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Manufacturer", manufacturerFromDb ?? (object)DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@SerialNo", SerialNo);
                    insertCmd.Parameters.AddWithValue("@Serialization", serializationFromDb ?? (object)DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Amendment", amendmentValue);
                    insertCmd.Parameters.AddWithValue("@Signatory", Signatory);
                    insertCmd.Parameters.AddWithValue("@Date", Date);
                    insertCmd.Parameters.AddWithValue("@Edition", Edition);
                    insertCmd.Parameters.AddWithValue("@Remarks1", Remarks1 ?? (object)DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Remarks2", Remarks2 ?? (object)DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Remarks3", Remarks3 ?? (object)DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Remarks4", Remarks4 ?? (object)DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Quantity", Quantity);
                    insertCmd.Parameters.AddWithValue("@Authorisation", Authorisation);
                    insertCmd.Parameters.AddWithValue("@Item", Item ?? (object)DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Status", Status);
                    insertCmd.Parameters.AddWithValue("@Approved", Approved);
                    insertCmd.Parameters.AddWithValue("@State", State ?? (object)DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Comment", Comment ?? (object)DBNull.Value);
                    await insertCmd.ExecuteNonQueryAsync();
                }

                // 4. Insert log record
                var logQuery = @"INSERT INTO Log_Certificates (CertNo, Action, Performed_By, Datetime, ProductNo, ProductDescription, ProductType, Manufacturer, SerialNo, Serialization, Amendment, Signatory, Date, Edition, Remarks1, Remarks2, Remarks3, Remarks4, Quantity, Authorisation, Item, Status, Approved, State, Comment) 
                                 VALUES (@CertNo, @Action, @ID, @Time, @ProductNo, @ProductDescription, @ProductType, @Manufacturer, @SerialNo, @Serialization, @Amendment, @Signatory, @Date, @Edition, @Remarks1, @Remarks2, @Remarks3, @Remarks4, @Quantity, @Authorisation, @Item, @Status, @Approved, @State, @Comment)";
                using (var logCmd = new SqlCommand(logQuery, conn))
                {
                    logCmd.Parameters.AddWithValue("@CertNo", certNo);
                    logCmd.Parameters.AddWithValue("@Action", "Add");
                    logCmd.Parameters.AddWithValue("@ID", User.Identity?.Name);
                    logCmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                    logCmd.Parameters.AddWithValue("@ProductNo", ProductNo);
                    logCmd.Parameters.AddWithValue("@ProductDescription", productDescriptionFromDb ?? (object)DBNull.Value);
                    logCmd.Parameters.AddWithValue("@ProductType", productTypeFromDb ?? (object)DBNull.Value);
                    logCmd.Parameters.AddWithValue("@Manufacturer", manufacturerFromDb ?? (object)DBNull.Value);
                    logCmd.Parameters.AddWithValue("@SerialNo", SerialNo);
                    logCmd.Parameters.AddWithValue("@Serialization", serializationFromDb ?? (object)DBNull.Value);
                    logCmd.Parameters.AddWithValue("@Amendment", amendmentValue);
                    logCmd.Parameters.AddWithValue("@Signatory", Signatory);
                    logCmd.Parameters.AddWithValue("@Date", Date);
                    logCmd.Parameters.AddWithValue("@Edition", Edition);
                    logCmd.Parameters.AddWithValue("@Remarks1", Remarks1 ?? (object)DBNull.Value);
                    logCmd.Parameters.AddWithValue("@Remarks2", Remarks2 ?? (object)DBNull.Value);
                    logCmd.Parameters.AddWithValue("@Remarks3", Remarks3 ?? (object)DBNull.Value);
                    logCmd.Parameters.AddWithValue("@Remarks4", Remarks4 ?? (object)DBNull.Value);
                    logCmd.Parameters.AddWithValue("@Quantity", Quantity);
                    logCmd.Parameters.AddWithValue("@Authorisation", Authorisation);
                    logCmd.Parameters.AddWithValue("@Item", Item ?? (object)DBNull.Value);
                    logCmd.Parameters.AddWithValue("@Status", Status);
                    logCmd.Parameters.AddWithValue("@Approved", Approved);
                    logCmd.Parameters.AddWithValue("@State", State ?? (object)DBNull.Value);
                    logCmd.Parameters.AddWithValue("@Comment", Comment ?? (object)DBNull.Value);
                    await logCmd.ExecuteNonQueryAsync();
                }
            }

            TempData["SuccessMessage"] = $"Entry added successfully. Certificate Number: {certNo}";
            return RedirectToPage();
        }

        // AJAX handler is now async
        public async Task<JsonResult> OnGetProductDetailsAsync(string productNo)
        {
            string productDescription = "";
            string serialization = "";

            try
            {
                if (!string.IsNullOrEmpty(productNo))
                {
                    string connectionString = _configuration.GetConnectionString("SQLConnection");
                    using (var conn = new SqlConnection(connectionString))
                    {
                        await conn.OpenAsync();
                        string query = "SELECT ProductDesc, Serialization FROM PartNumbers WHERE ProductNo = @ProductNo";
                        using (var cmd = new SqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@ProductNo", productNo);
                            using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    productDescription = reader["ProductDesc"]?.ToString() ?? "";
                                    serialization = reader["Serialization"]?.ToString() ?? "";
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching product details for ProductNo: {ProductNo}", productNo);
                return new JsonResult(new { error = true, message = "Failed to fetch product details" });
            }

            return new JsonResult(new { productDescription, serialization });
        }

        /// <summary>
        /// Loads all initial data for the page, including dropdowns, signatory, and authorisation number,
        /// using a single database connection for efficiency.
        /// </summary>
        private async Task LoadInitialDataAsync()
        {
            // Initialize lists
            ProductNoList = new List<SelectListItem>();
            AmendmentList = new List<SelectListItem>();
            ApprovedList = new List<SelectListItem>();
            StatusList = new List<SelectListItem>();
            StatesList = new List<SelectListItem>();

            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                // Batch multiple SELECT statements into one command for performance
                string query = @"
                    SELECT ProductNo FROM PartNumbers ORDER BY ProductNo;
                    SELECT Amendment FROM Amendments ORDER BY Amendment;
                    SELECT ApprovedDesignIndicator FROM ApprovedDesignIndicators ORDER BY ApprovedDesignIndicator;
                    SELECT Status FROM Statuses ORDER BY Status;
                    SELECT State FROM States ORDER BY State;
                    SELECT Name FROM Users WHERE TGI = @UserId;
                    SELECT TOP 1 AuthorisationNo FROM AuthorisationNumber;";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", User.Identity?.Name ?? (object)DBNull.Value);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        // Result 1: Product Numbers
                        while (await reader.ReadAsync())
                        {
                            ProductNoList.Add(new SelectListItem { Value = reader["ProductNo"].ToString(), Text = reader["ProductNo"].ToString() });
                        }

                        // Result 2: Amendments
                        await reader.NextResultAsync();
                        while (await reader.ReadAsync())
                        {
                            AmendmentList.Add(new SelectListItem { Value = reader["Amendment"].ToString(), Text = reader["Amendment"].ToString() });
                        }

                        // Result 3: Approved Designators
                        await reader.NextResultAsync();
                        while (await reader.ReadAsync())
                        {
                            ApprovedList.Add(new SelectListItem { Value = reader["ApprovedDesignIndicator"].ToString(), Text = reader["ApprovedDesignIndicator"].ToString() });
                        }

                        // Result 4: Statuses
                        await reader.NextResultAsync();
                        while (await reader.ReadAsync())
                        {
                            StatusList.Add(new SelectListItem { Value = reader["Status"].ToString(), Text = reader["Status"].ToString() });
                        }

                        // Result 5: States
                        await reader.NextResultAsync();
                        while (await reader.ReadAsync())
                        {
                            StatesList.Add(new SelectListItem { Value = reader["State"].ToString(), Text = reader["State"].ToString() });
                        }

                        // Result 6: Signatory
                        await reader.NextResultAsync();
                        if (await reader.ReadAsync())
                        {
                            Signatory = reader["Name"]?.ToString();
                        }

                        // Result 7: Authorisation Number
                        await reader.NextResultAsync();
                        if (await reader.ReadAsync())
                        {
                            Authorisation = reader["AuthorisationNo"]?.ToString();
                        }
                    }
                }
            }
        }
    }
}