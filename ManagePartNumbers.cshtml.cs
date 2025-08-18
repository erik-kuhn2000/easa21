using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QApp.Pages
{
    [Authorize(Policy = "AdminOnly")]
    public class ManageProductNumbersModel : PageModel
    {
        public class ProductNumber
        {
            public string ProductNo { get; set; }
            public string ProductDesc { get; set; }
            public string Serialization { get; set; }
            public string ProductType { get; set; }
            public string Manufacturer { get; set; }
        }



        [BindProperty]
        public ProductNumber NewProduct { get; set; } = new ProductNumber();

        public List<ProductNumber> ProductNumbers { get; set; } = new List<ProductNumber>();
        public List<SelectListItem> SerializationList { get; set; }
        public List<SelectListItem> ProductTypeList { get; set; }

        [BindProperty]
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalResults { get; set; } = 0;
        public int TotalPages => (int)Math.Ceiling((double)TotalResults / PageSize);

        public string SuccessMessage { get; set; }
        public string ErrorMessage { get; set; }

        private readonly IConfiguration _configuration;
        private readonly ILogger<ManageProductNumbersModel> _logger;
        private readonly IAuthorizationService _authorizationService;

        public ManageProductNumbersModel(IConfiguration configuration, ILogger<ManageProductNumbersModel> logger, IAuthorizationService authorizationService)
        {
            _configuration = configuration;
            _logger = logger;
            _authorizationService = authorizationService;
        }

        public void OnGet()
        {
            LoadDropdowns();
            LoadProductNumbers();
        }

        public IActionResult OnPost(string handler)
        {
            if (handler == "add")
            {
                return OnPostAddProduct();
            }

            LoadDropdowns();
            LoadProductNumbers();
            return Page();
        }

        public IActionResult OnPostAddProduct()
        {
            var errors = new List<string>();

            // Validation
            if (string.IsNullOrWhiteSpace(NewProduct.ProductNo))
                errors.Add("Part No. is required.");
            if (string.IsNullOrWhiteSpace(NewProduct.ProductDesc))
                errors.Add("Description is required.");
            if (string.IsNullOrWhiteSpace(NewProduct.Serialization))
                errors.Add("Serialization is required.");
            if (string.IsNullOrWhiteSpace(NewProduct.ProductType))
                errors.Add("Part Type is required.");
            if (string.IsNullOrWhiteSpace(NewProduct.Manufacturer))
                errors.Add("Manufacturer is required.");

            // Check if Product Number already exists - ONLY for ADD operations
            if (!string.IsNullOrWhiteSpace(NewProduct.ProductNo) && ProductNumberExists(NewProduct.ProductNo))
            {
                errors.Add("Part No. already exists.");
            }

            if (errors.Any())
            {
                ErrorMessage = string.Join(" ", errors);
                LoadDropdowns();
                LoadProductNumbers();
                return Page();
            }

            try
            {
                bool success = AddProductNumber(NewProduct);
                if (success)
                {
                    LogAddAction(NewProduct);
                    SuccessMessage = "Part No. added successfully.";
                    NewProduct = new ProductNumber(); // Clear form
                }
                else
                {
                    ErrorMessage = "Failed to add Part Number.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding part number {ProductNo}", NewProduct.ProductNo);
                ErrorMessage = $"Error adding Part Number: {ex.Message}";
            }

            LoadDropdowns();
            LoadProductNumbers();
            return Page();
        }

        public async Task<IActionResult> OnPostUpdateProduct(
    string productNo, string productDesc, string serialization, string productType, string manufacturer)
        {
            try
            {
                var errors = new List<string>();
                if (string.IsNullOrWhiteSpace(productDesc)) errors.Add("Description is required.");
                if (string.IsNullOrWhiteSpace(serialization)) errors.Add("Serialization is required.");
                if (string.IsNullOrWhiteSpace(productType)) errors.Add("Part Type is required.");
                if (string.IsNullOrWhiteSpace(manufacturer)) errors.Add("Manufacturer is required.");

                if (errors.Any())
                {
                    return new JsonResult(new { success = false, message = string.Join(" ", errors) });
                }

                // Get current product details to compare changes
                var currentProduct = GetProductDetails(productNo);
                if (currentProduct == null)
                {
                    return new JsonResult(new { success = false, message = "Product not found." });
                }

                var product = new ProductNumber
                {
                    ProductNo = productNo,
                    ProductDesc = productDesc,
                    Serialization = serialization,
                    ProductType = productType,
                    Manufacturer = manufacturer
                };

                var success = UpdateProductNumber(product);

                if (success)
                {
                    // Pass both current and new product for change comparison
                    LogUpdateAction(product, currentProduct);
                    return new JsonResult(new { success = true, message = "Part No. updated successfully." });
                }
                else
                {
                    return new JsonResult(new { success = false, message = "Failed to update Part Number." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating part number {ProductNo}", productNo);
                return new JsonResult(new { success = false, message = $"Update error: {ex.Message}" });
            }
        }

        public async Task<IActionResult> OnPostDeleteProduct(string productNo)
        {
            // 1. Add validation for the incoming productNo
            if (string.IsNullOrWhiteSpace(productNo))
            {
                return new JsonResult(new { success = false, message = "Part Number is required." });
            }

            try
            {
                // 2. First check if the product exists
                var productToDelete = GetProductDetails(productNo);
                if (productToDelete == null)
                {
                    return new JsonResult(new { success = false, message = "Part Number not found." });
                }

                // 3. Call the delete method
                var success = DeleteProductNumber(productToDelete);

                if (success)
                {
                    // 4. Log the action
                    LogDeleteAction(productToDelete);
                    return new JsonResult(new { success = true, message = "Part No. deleted successfully." });
                }
                else
                {
                    return new JsonResult(new { success = false, message = "Failed to delete Part Number." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting part number {ProductNo}", productNo);
                return new JsonResult(new { success = false, message = $"Delete error: {ex.Message}" });
            }
        }

        public IActionResult OnGetProductDetails(string productNo)
        {
            if (string.IsNullOrWhiteSpace(productNo))
            {
                return new JsonResult(new { success = false, message = "Part No. is required." });
            }

            try
            {
                var product = GetProductDetails(productNo);
                if (product == null)
                {
                    return new JsonResult(new { success = false, message = "Part not found." });
                }

                return new JsonResult(new { success = true, data = product });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"Database error: {ex.Message}" });
            }
        }

        private void LoadDropdowns()
        {
            SerializationList = new List<SelectListItem>
            {
                new SelectListItem { Value = "Yes", Text = "Yes" },
                new SelectListItem { Value = "No", Text = "No" }
            };

            ProductTypeList = new List<SelectListItem>
            {
                new SelectListItem { Value = "Finished Product LRU", Text = "Finished Product LRU" },
                new SelectListItem { Value = "Not LRU Product", Text = "Not LRU Product" }
            };
        }

        private void LoadProductNumbers()
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    // Get total count
                    var countSql = "SELECT COUNT(*) FROM PartNumbers";
                    using (SqlCommand countCommand = new SqlCommand(countSql, connection))
                    {
                        object countResult = countCommand.ExecuteScalar();
                        TotalResults = countResult != null ? Convert.ToInt32(countResult) : 0;
                    }

                    // Get paginated data
                    int offset = (PageNumber - 1) * PageSize;
                    var selectSql = @"SELECT ProductNo, ProductDesc, Serialization, ProductType, Manufacturer 
                                      FROM PartNumbers 
                                      ORDER BY ProductNo 
                                      OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                    using (SqlCommand selectCommand = new SqlCommand(selectSql, connection))
                    {
                        selectCommand.Parameters.AddWithValue("@Offset", offset);
                        selectCommand.Parameters.AddWithValue("@PageSize", PageSize);

                        using (SqlDataReader reader = selectCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                ProductNumbers.Add(new ProductNumber
                                {
                                    ProductNo = reader["ProductNo"]?.ToString() ?? "",
                                    ProductDesc = reader["ProductDesc"]?.ToString() ?? "",
                                    Serialization = reader["Serialization"]?.ToString() ?? "",
                                    ProductType = reader["ProductType"]?.ToString() ?? "",
                                    Manufacturer = reader["Manufacturer"]?.ToString() ?? ""
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading part numbers");
                ErrorMessage = "Error loading part numbers.";
            }
        }

        private bool ProductNumberExists(string productNo)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sql = "SELECT COUNT(*) FROM PartNumbers WHERE ProductNo = @ProductNo";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ProductNo", productNo);
                        int count = Convert.ToInt32(command.ExecuteScalar());
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if part number exists {ProductNo}", productNo);
                throw;
            }
        }

        private bool AddProductNumber(ProductNumber product)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sql = @"INSERT INTO PartNumbers (ProductNo, ProductDesc, Serialization, ProductType, Manufacturer) 
                                      VALUES (@ProductNo, @ProductDesc, @Serialization, @ProductType, @Manufacturer)";

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ProductNo", product.ProductNo ?? "");
                        command.Parameters.AddWithValue("@ProductDesc", product.ProductDesc ?? "");
                        command.Parameters.AddWithValue("@Serialization", product.Serialization ?? "");
                        command.Parameters.AddWithValue("@ProductType", product.ProductType ?? "");
                        command.Parameters.AddWithValue("@Manufacturer", product.Manufacturer ?? "");

                        int rowsAffected = command.ExecuteNonQuery();
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding part number {ProductNo} to database", product.ProductNo);
                throw;
            }
        }

        private bool UpdateProductNumber(ProductNumber product)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sql = @"UPDATE PartNumbers SET 
                                      ProductDesc = @ProductDesc, 
                                      Serialization = @Serialization, 
                                      ProductType = @ProductType, 
                                      Manufacturer = @Manufacturer 
                                      WHERE ProductNo = @ProductNo";

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ProductNo", product.ProductNo ?? "");
                        command.Parameters.AddWithValue("@ProductDesc", product.ProductDesc ?? "");
                        command.Parameters.AddWithValue("@Serialization", product.Serialization ?? "");
                        command.Parameters.AddWithValue("@ProductType", product.ProductType ?? "");
                        command.Parameters.AddWithValue("@Manufacturer", product.Manufacturer ?? "");

                        int rowsAffected = command.ExecuteNonQuery();
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating part number {ProductNo} in database", product.ProductNo);
                throw;
            }
        }

        private bool DeleteProductNumber(ProductNumber product)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sql = "DELETE FROM PartNumbers WHERE ProductNo = @ProductNo";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ProductNo", product.ProductNo ?? "");
                        int rowsAffected = command.ExecuteNonQuery();

                        // Log the actual rows affected for debugging
                        _logger.LogInformation("Delete operation for {ProductNo} affected {RowsAffected} rows", product.ProductNo, rowsAffected);

                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting part number {ProductNo} from database", product.ProductNo);
                throw;
            }
        }

        private ProductNumber GetProductDetails(string productNo)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var sql = @"SELECT ProductNo, ProductDesc, Serialization, ProductType, Manufacturer 
                                  FROM PartNumbers WHERE ProductNo = @ProductNo";

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ProductNo", productNo);
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return new ProductNumber
                                {
                                    ProductNo = reader["ProductNo"]?.ToString() ?? "",
                                    ProductDesc = reader["ProductDesc"]?.ToString() ?? "",
                                    Serialization = reader["Serialization"]?.ToString() ?? "",
                                    ProductType = reader["ProductType"]?.ToString() ?? "",
                                    Manufacturer = reader["Manufacturer"]?.ToString() ?? ""
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting part details for {ProductNo}", productNo);
                throw;
            }
            return null;
        }

        private void LogAddAction(ProductNumber product)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    var logquery = "INSERT INTO Log_PartNumbers (Action, Performed_By, Datetime, ProductNo, ProductDescription, Serialization, PartType, Manufacturer) VALUES (@Action, @ID, @Time, @ProductNo, @ProductDescription,@Serialization, @PartType, @Manufacturer)";
                    using (SqlCommand cmd = new SqlCommand(logquery, conn))
                    {
                      
                        cmd.Parameters.AddWithValue("@Action", "Add");
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@ProductNo", product.ProductNo ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@ProductDescription", product.ProductDesc ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Serialization", product.Serialization ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@PartType", product.ProductType ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Manufacturer", product.Manufacturer ?? (object)DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging add action for part number {ProductNo}", product.ProductNo);
            }
        }

        private void LogUpdateAction(ProductNumber newProduct, ProductNumber currentProduct = null)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    var logquery = "INSERT INTO Log_PartNumbers (Action, Performed_By, Datetime, ProductNo, ProductDescription, Serialization, PartType, Manufacturer) VALUES (@Action, @ID, @Time, @ProductNo, @ProductDescription,@Serialization, @PartType, @Manufacturer)";
                    using (SqlCommand cmd = new SqlCommand(logquery, conn))
                    {
                        cmd.Parameters.AddWithValue("@Action", "Update");
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@ProductNo", newProduct.ProductNo ?? (object)DBNull.Value);

                        // Only log fields that actually changed
                        if (currentProduct != null)
                        {
                            // ProductDescription - only log if changed
                            if (!string.Equals(currentProduct.ProductDesc?.Trim(), newProduct.ProductDesc?.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                cmd.Parameters.AddWithValue("@ProductDescription", newProduct.ProductDesc ?? (object)DBNull.Value);
                            }
                            else
                            {
                                cmd.Parameters.AddWithValue("@ProductDescription", (object)DBNull.Value);
                            }

                            // Serialization - only log if changed
                            if (!string.Equals(currentProduct.Serialization?.Trim(), newProduct.Serialization?.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                cmd.Parameters.AddWithValue("@Serialization", newProduct.Serialization ?? (object)DBNull.Value);
                            }
                            else
                            {
                                cmd.Parameters.AddWithValue("@Serialization", (object)DBNull.Value);
                            }

                            // PartType - only log if changed
                            if (!string.Equals(currentProduct.ProductType?.Trim(), newProduct.ProductType?.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                cmd.Parameters.AddWithValue("@PartType", newProduct.ProductType ?? (object)DBNull.Value);
                            }
                            else
                            {
                                cmd.Parameters.AddWithValue("@PartType", (object)DBNull.Value);
                            }

                            // Manufacturer - only log if changed
                            if (!string.Equals(currentProduct.Manufacturer?.Trim(), newProduct.Manufacturer?.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                cmd.Parameters.AddWithValue("@Manufacturer", newProduct.Manufacturer ?? (object)DBNull.Value);
                            }
                            else
                            {
                                cmd.Parameters.AddWithValue("@Manufacturer", (object)DBNull.Value);
                            }
                        }
                        else
                        {
                            // Fallback: if no current product provided, log all fields
                            cmd.Parameters.AddWithValue("@ProductDescription", newProduct.ProductDesc ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Serialization", newProduct.Serialization ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@PartType", newProduct.ProductType ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Manufacturer", newProduct.Manufacturer ?? (object)DBNull.Value);
                        }

                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging update action for part number {ProductNo}", newProduct.ProductNo);
            }
        }

        private void LogDeleteAction(ProductNumber product)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    var logquery = "INSERT INTO Log_PartNumbers (Action, Performed_By, Datetime, ProductNo) VALUES (@Action, @ID, @Time, @ProductNo)";
                    using (SqlCommand cmd = new SqlCommand(logquery, conn))
                    {
                        cmd.Parameters.AddWithValue("@Action", "Delete");
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("dd.MMM.yyyy HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@ProductNo", product.ProductNo ?? (object)DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging delete action for part number {ProductNo}", product.ProductNo);
            }
        }
    }
}