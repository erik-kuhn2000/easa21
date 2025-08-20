
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;


namespace QApp.Pages
{
    [Authorize(Policy = "SignatoryRoleRequired")]

    public class AddModel : PageModel
    {
        [BindProperty]
        [Required(ErrorMessage = "This field is mandatory.")]
        public string ProductNo { get; set; }
        public List<SelectListItem> ProductNoList { get; set; }

        [BindProperty]
        public string ProductDescription { get; set; }

        [BindProperty]
        public string ProductType { get; set; } // New property for Product Type

        [BindProperty]
        public string Manufacturer { get; set; } // New property for Manufacturer

        [BindProperty]
        [Required(ErrorMessage = "This field is mandatory.")]
        public string SerialNo { get; set; }

        [BindProperty]
        public string Serialization { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "This field is mandatory.")]
        public List<string> Amendment { get; set; }
        public List<SelectListItem> AmendmentList { get; set; }
        [BindProperty]
        [Required(ErrorMessage = "This field is mandatory.")]
        public string Signatory { get; set; }
       

        [BindProperty]
        [Required(ErrorMessage = "This field is mandatory.")]
        public string Date { get; set; }
        [BindProperty]
        [Required(ErrorMessage = "This field is mandatory.")]
        public string Edition { get; set; }
        [BindProperty]
        public string Remarks1 { get; set; }
        [BindProperty]
        public string Remarks2 { get; set; }
        [BindProperty]
        public string Remarks3 { get; set; }
        [BindProperty]
        public string Remarks4 { get; set; }
        [BindProperty]
        [Required(ErrorMessage = "This field is mandatory.")]
        public string Quantity { get; set; }
        [BindProperty]
        public string Authorisation { get; set; }
        [BindProperty]
        public string Item { get; set; }
        [BindProperty]
        public string Status { get; set; }
        [BindProperty]
        public string Approved { get; set; }

        [BindProperty]
        public List<SelectListItem> ApprovedList { get; set; }

        [BindProperty]
        public List<SelectListItem> StatusList { get; set; }

        [BindProperty]
        public string Comment { get; set; }


        [BindProperty]
        public string State { get; set; }

        [BindProperty]
        public List<SelectListItem> StatesList { get; set; }

        public string SuccessMessage { get; set; }
        public bool IsSuccess { get; set; } = false;

        private readonly ILogger<AddModel> _logger;
        private readonly IConfiguration _configuration;

        private readonly IAuthorizationService _authorizationService;

        public AddModel(IConfiguration configuration, IAuthorizationService authorizationService, ILogger<AddModel> logger)
        {
            _configuration = configuration;
            _authorizationService = authorizationService;
            _logger = logger;
        }

        public void OnGet()
        {
            LoadProductNumbers();
            LoadAmendment();
            LoadSignatory();
            LoadAuthorisationNumber();
            LoadApproved();
            LoadStatus();
            LoadState();

            if (TempData["SuccessMessage"] != null)
            {
                SuccessMessage = TempData["SuccessMessage"].ToString();
                IsSuccess = true;
            }
        }



        public IActionResult OnPost()
        {
            LoadProductNumbers();
            LoadAmendment();
            LoadSignatory();
            LoadApproved();
            LoadStatus();
            LoadState();

            string CertNo = null;

            if (string.IsNullOrWhiteSpace(ProductNo) ||
            string.IsNullOrWhiteSpace(SerialNo) ||
            Amendment == null || !Amendment.Any() ||
            string.IsNullOrWhiteSpace(Signatory) ||
            string.IsNullOrWhiteSpace(Date) ||
            string.IsNullOrWhiteSpace(Edition) ||
            string.IsNullOrWhiteSpace(Quantity)||
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

            if (string.IsNullOrWhiteSpace(Quantity) || !System.Text.RegularExpressions.Regex.IsMatch(Quantity, @"^(0[1-9]|[1-9][0-9]{1,3})$"))
            {
                ModelState.AddModelError(nameof(Quantity), "Wrong input format.");
                return Page();
            }

            string connectionString = _configuration.GetConnectionString("SQLConnection");

            
            string productDescriptionFromDb = null;
            string productTypeFromDb = null;
            string manufacturerFromDb = null;
            string serializationFromDb = null;

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT ProductDesc, ProductType, Manufacturer, Serialization FROM PartNumbers WHERE ProductNo = @ProductNo";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@ProductNo", ProductNo);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            productDescriptionFromDb = reader["ProductDesc"]?.ToString();
                            productTypeFromDb = reader["ProductType"]?.ToString();
                            manufacturerFromDb = reader["Manufacturer"]?.ToString();
                            serializationFromDb = reader["Serialization"]?.ToString();
                        }
                    }
                }
            } 


          

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                int currentYear = DateTime.Now.Year;
                string getPrefixQuery = "SELECT Code FROM Prefixes WHERE Year=@Year";
                string prefix = null;
                using (SqlCommand getCmd = new SqlCommand(getPrefixQuery, conn))
                {
                    getCmd.Parameters.AddWithValue("@Year", currentYear);
                    var result = getCmd.ExecuteScalar();
                    prefix = result != null ? result.ToString() : null;
                }

                if (string.IsNullOrEmpty(prefix))
                {
                    throw new Exception("No prefix found for the current year.");
                }

                string getCertNoQuery = "SELECT TOP 1 CertNo FROM Certificates WHERE CertNo LIKE @PrefixLike ORDER BY CertNo DESC";
                string latestCertNo = null;
                using (SqlCommand getCmd = new SqlCommand(getCertNoQuery, conn))
                {
                    getCmd.Parameters.AddWithValue("@PrefixLike", prefix + "93%");
                    var result2 = getCmd.ExecuteScalar();
                    latestCertNo = result2 != null ? result2.ToString() : null;
                }

                int nextNumber = 6000;
                if (!string.IsNullOrEmpty(latestCertNo) && latestCertNo.Length >= 4)
                {
                    if (int.TryParse(latestCertNo.Substring(latestCertNo.Length - 4), out int lastFour))
                    {
                        if (lastFour >= 6000 && lastFour < 9999)
                        {
                            nextNumber = lastFour + 1;
                        }
                    }
                }

                CertNo = prefix + "93" + nextNumber.ToString("D4");



                string amendmentValue = string.Join(", ", Amendment);

                var insertQuery = @"INSERT INTO Certificates 
        (CertNo, ProductNo, ProductDescription, ProductType, Manufacturer, SerialNo, Serialization, Amendment, Signatory, Date, Edition, Remarks1, Remarks2, Remarks3, Remarks4, Quantity, Authorisation, Item, Status, Approved, State, Comment) 
        VALUES 
        (@CertNo, @ProductNo, @ProductDescription, @ProductType, @Manufacturer, @SerialNo, @Serialization, @Amendment, @Signatory, @Date, @Edition, @Remarks1, @Remarks2, @Remarks3, @Remarks4, @Quantity, @Authorisation, @Item, @Status, @Approved, @State, @Comment)";

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@CertNo", CertNo);
                    cmd.Parameters.AddWithValue("@ProductNo", ProductNo);
                    cmd.Parameters.AddWithValue("@ProductDescription", productDescriptionFromDb);
                    cmd.Parameters.AddWithValue("@ProductType", productTypeFromDb);
                    cmd.Parameters.AddWithValue("@Manufacturer", manufacturerFromDb);
                    cmd.Parameters.AddWithValue("@SerialNo", SerialNo);
                    cmd.Parameters.AddWithValue("@Serialization", serializationFromDb);
                    cmd.Parameters.AddWithValue("@Amendment", amendmentValue);
                    cmd.Parameters.AddWithValue("@Signatory", Signatory);
                    cmd.Parameters.AddWithValue("@Date", Date);
                    cmd.Parameters.AddWithValue("@Edition", Edition);
                    cmd.Parameters.AddWithValue("@Remarks1", Remarks1 ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Remarks2", Remarks2 ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Remarks3", Remarks3 ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Remarks4", Remarks4 ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Quantity", Quantity);
                    cmd.Parameters.AddWithValue("@Authorisation", Authorisation);
                    cmd.Parameters.AddWithValue("@Item", Item);
                    cmd.Parameters.AddWithValue("@Status", Status);
                    cmd.Parameters.AddWithValue("@Approved", Approved);
                    cmd.Parameters.AddWithValue("@State", State);
                    cmd.Parameters.AddWithValue("@Comment", Comment ?? (object)DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                var logquery = "INSERT INTO Log_Certificates (CertNo, Action, Performed_By, Datetime, ProductNo, ProductDescription, ProductType, Manufacturer, SerialNo, Serialization, Amendment, Signatory, Date, Edition, Remarks1, Remarks2, Remarks3, Remarks4, Quantity, Authorisation, Item, Status, Approved, State, Comment) VALUES (@CertNo, @Action, @ID, @Time, @ProductNo, @ProductDescription, @ProductType, @Manufacturer, @SerialNo, @Serialization, @Amendment, @Signatory, @Date, @Edition, @Remarks1, @Remarks2, @Remarks3, @Remarks4, @Quantity, @Authorisation, @Item, @Status, @Approved, @State, @Comment)";
                using (SqlCommand cmd = new SqlCommand(logquery, conn))
                {
                    cmd.Parameters.AddWithValue("@CertNo", CertNo);
                    cmd.Parameters.AddWithValue("@Action", "Add");
                    cmd.Parameters.AddWithValue("@ID", User.Identity?.Name);
                    cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@ProductNo", ProductNo);
                    cmd.Parameters.AddWithValue("@ProductDescription", productDescriptionFromDb);
                    cmd.Parameters.AddWithValue("@ProductType", productTypeFromDb);
                    cmd.Parameters.AddWithValue("@Manufacturer", manufacturerFromDb);
                    cmd.Parameters.AddWithValue("@SerialNo", SerialNo);
                    cmd.Parameters.AddWithValue("@Serialization", serializationFromDb);
                    cmd.Parameters.AddWithValue("@Amendment", amendmentValue);
                    cmd.Parameters.AddWithValue("@Signatory", Signatory);
                    cmd.Parameters.AddWithValue("@Date", Date);
                    cmd.Parameters.AddWithValue("@Edition", Edition);
                    cmd.Parameters.AddWithValue("@Remarks1", Remarks1 ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Remarks2", Remarks2 ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Remarks3", Remarks3 ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Remarks4", Remarks4 ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Quantity", Quantity);
                    cmd.Parameters.AddWithValue("@Authorisation", Authorisation);
                    cmd.Parameters.AddWithValue("@Item", Item);
                    cmd.Parameters.AddWithValue("@Status", Status);
                    cmd.Parameters.AddWithValue("@Approved", Approved);
                    cmd.Parameters.AddWithValue("@State", State);
                    cmd.Parameters.AddWithValue("@Comment", Comment ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }


            // Store the success message in TempData to survive the redirect
            TempData["SuccessMessage"] = $"Entry added successfully. Certificate Number: {CertNo}";

            // Redirect to the GET handler of the current page
            return RedirectToPage();
        }


        // Fixed OnGetProductDetails method in AddModel.cs
        public JsonResult OnGetProductDetails(string productNo)
        {
            // Initialize with default values
            string serialization = "";
            string productDescription = "";

            try
            {
                if (!string.IsNullOrEmpty(productNo))
                {
                    string connectionString = _configuration.GetConnectionString("SQLConnection");
                    using (var conn = new SqlConnection(connectionString))
                    {
                        conn.Open();
                        // Only fetch ProductDesc and Serialization
                        string query = @"SELECT ProductDesc, Serialization 
                               FROM PartNumbers 
                               WHERE ProductNo = @ProductNo";

                        using (var cmd = new SqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@ProductNo", productNo);
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
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
                // Return error indication in JSON
                return new JsonResult(new
                {
                    error = true,
                    message = "Failed to fetch product details",
                    productDescription = "",
                    serialization = ""
                });
            }

            // Return only productDescription and serialization
            return new JsonResult(new
            {
                productDescription,
                serialization
            });
        }


        private void LoadProductNumbers()
        {
            ProductNoList = new List<SelectListItem>();
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT ProductNo FROM PartNumbers ORDER BY ProductNo";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ProductNoList.Add(new SelectListItem { Value = reader["ProductNo"].ToString(), Text = reader["ProductNo"].ToString() });
                    }
                }
            }
        }

        private void LoadAmendment()
        {
            AmendmentList = new List<SelectListItem>();
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT Amendment FROM Amendments ORDER BY Amendment";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        AmendmentList.Add(new SelectListItem { Value = reader["Amendment"].ToString(), Text = reader["Amendment"].ToString() });
                    }
                }
            }
        }

        private void LoadApproved()
        {
            ApprovedList = new List<SelectListItem>();
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT ApprovedDesignIndicator FROM ApprovedDesignIndicators ORDER BY ApprovedDesignIndicator";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ApprovedList.Add(new SelectListItem { Value = reader["ApprovedDesignIndicator"].ToString(), Text = reader["ApprovedDesignIndicator"].ToString() });
                    }
                }
            }
        }

        private void LoadStatus()
        {
            StatusList = new List<SelectListItem>();
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT Status FROM Statuses ORDER BY Status";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        StatusList.Add(new SelectListItem { Value = reader["Status"].ToString(), Text = reader["Status"].ToString() });
                    }
                }
            }
        }

        private void LoadState()
        {
            StatesList = new List<SelectListItem>();
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT State FROM States ORDER BY State";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        StatesList.Add(new SelectListItem { Value = reader["State"].ToString(), Text = reader["State"].ToString() });
                    }
                }
            }
        }




        private void LoadSignatory()
        {
            // Clear any previous value
            this.Signatory = string.Empty;

            var userId = User.Identity?.Name;
            if (string.IsNullOrEmpty(userId))
            {
                // If user is not logged in, do nothing.
                return;
            }

            string connectionString = _configuration.GetConnectionString("SQLConnection");
            // Query to get the Name for the current user
            string query = "SELECT Name FROM Users WHERE TGI = @UserId";

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        var result = cmd.ExecuteScalar();
                        if (result != null)
                        {
                            // Populate the Signatory property directly
                            this.Signatory = result.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading signatory name for user {UserId}", userId);
            }
        }

        
        private void LoadAuthorisationNumber()
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT TOP 1 AuthorisationNo FROM AuthorisationNumber";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        Authorisation = result.ToString();
                    }
                }
            }
        }


    }

}