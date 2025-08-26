using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;


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

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalResults { get; set; } = 0;
        public int TotalPages => (int)Math.Ceiling((double)TotalResults / PageSize);

        [BindProperty(SupportsGet = true)]
        public string SearchTerm { get; set; }

        [TempData]
        public string SuccessMessage { get; set; }
        [TempData]
        public string ErrorMessage { get; set; }

        private readonly IConfiguration _configuration;
        private readonly ILogger<ManageProductNumbersModel> _logger;

        public ManageProductNumbersModel(IConfiguration configuration, ILogger<ManageProductNumbersModel> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            await LoadPageDataAsync();
        }

        public async Task<IActionResult> OnPostAsync(string handler)
        {
            _logger.LogInformation("OnPostAsync called with handler: {Handler}", handler);

            if (handler == "add")
            {
                return await OnPostAddProductAsync();
            }

            if (handler == "search")
            {
                PageNumber = 1;
            }

            if (handler == "clear")
            {
                return RedirectToPage();
            }

            await LoadPageDataAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAddProductAsync()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(NewProduct.ProductNo)) errors.Add("Part No. is required.");
            if (string.IsNullOrWhiteSpace(NewProduct.ProductDesc)) errors.Add("Description is required.");
            if (string.IsNullOrWhiteSpace(NewProduct.Serialization)) errors.Add("Serialization is required.");
            if (string.IsNullOrWhiteSpace(NewProduct.ProductType)) errors.Add("Part Type is required.");
            if (string.IsNullOrWhiteSpace(NewProduct.Manufacturer)) errors.Add("Manufacturer is required.");

            if (!string.IsNullOrWhiteSpace(NewProduct.ProductNo) && await ProductNumberExistsAsync(NewProduct.ProductNo))
            {
                errors.Add("Part No. already exists.");
            }

            if (errors.Any())
            {
                ErrorMessage = string.Join(" ", errors);
                await LoadPageDataAsync();
                return Page();
            }

            try
            {
                bool success = await AddProductNumberAsync(NewProduct);
                if (success)
                {
                    await LogAddActionAsync(NewProduct);
                    SuccessMessage = "Part No. added successfully.";
                    return RedirectToPage();
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

            await LoadPageDataAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostUpdateProductAsync(
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

                var currentProduct = await GetProductDetailsAsync(productNo);
                if (currentProduct == null)
                {
                    return new JsonResult(new { success = false, message = "Product not found." });
                }

                var productToUpdate = new ProductNumber
                {
                    ProductNo = productNo,
                    ProductDesc = productDesc,
                    Serialization = serialization,
                    ProductType = productType,
                    Manufacturer = manufacturer
                };

                var success = await UpdateProductNumberAsync(productToUpdate);

                if (success)
                {
                    await LogUpdateActionAsync(productToUpdate, currentProduct);
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

        public async Task<IActionResult> OnPostDeleteProductAsync(string productNo)
        {
            if (string.IsNullOrWhiteSpace(productNo))
            {
                return new JsonResult(new { success = false, message = "Part Number is required." });
            }

            try
            {
                var productToDelete = await GetProductDetailsAsync(productNo);
                if (productToDelete == null)
                {
                    return new JsonResult(new { success = false, message = "Part Number not found." });
                }

                var success = await DeleteProductNumberAsync(productToDelete);

                if (success)
                {
                    await LogDeleteActionAsync(productToDelete);
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

        public async Task<IActionResult> OnGetProductDetailsAsync(string productNo)
        {
            if (string.IsNullOrWhiteSpace(productNo))
            {
                return new JsonResult(new { success = false, message = "Part No. is required." });
            }

            try
            {
                var product = await GetProductDetailsAsync(productNo);
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

        private async Task LoadPageDataAsync()
        {
            LoadDropdowns();
            await LoadProductNumbersAsync();
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

        private async Task LoadProductNumbersAsync()
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            ProductNumbers.Clear();
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    var whereClauses = new List<string>();
                    if (!string.IsNullOrEmpty(SearchTerm))
                    {
                        whereClauses.Add("(ProductNo LIKE @SearchTerm OR ProductDesc LIKE @SearchTerm OR Serialization LIKE @SearchTerm OR ProductType LIKE @SearchTerm OR Manufacturer LIKE @SearchTerm)");
                    }
                    string whereClause = whereClauses.Any() ? "WHERE " + string.Join(" AND ", whereClauses) : "";

                    var countSql = $"SELECT COUNT(*) FROM PartNumbers {whereClause}";
                    using (var countCommand = new SqlCommand(countSql, connection))
                    {
                        if (!string.IsNullOrEmpty(SearchTerm))
                        {
                            countCommand.Parameters.AddWithValue("@SearchTerm", $"%{SearchTerm}%");
                        }
                        object countResult = await countCommand.ExecuteScalarAsync();
                        TotalResults = countResult != null ? Convert.ToInt32(countResult) : 0;
                    }

                    int offset = (PageNumber - 1) * PageSize;
                    var selectSql = $@"SELECT ProductNo, ProductDesc, Serialization, ProductType, Manufacturer 
                                       FROM PartNumbers {whereClause}
                                       ORDER BY ProductNo 
                                       OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                    using (var selectCommand = new SqlCommand(selectSql, connection))
                    {
                        if (!string.IsNullOrEmpty(SearchTerm))
                        {
                            selectCommand.Parameters.AddWithValue("@SearchTerm", $"%{SearchTerm}%");
                        }
                        selectCommand.Parameters.AddWithValue("@Offset", offset);
                        selectCommand.Parameters.AddWithValue("@PageSize", PageSize);

                        using (var reader = await selectCommand.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
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
                _logger.LogError(ex, "Error loading part numbers with search term: {SearchTerm}", SearchTerm);
                ErrorMessage = "Error loading part numbers.";
            }
        }

        private async Task<bool> ProductNumberExistsAsync(string productNo)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    var sql = "SELECT COUNT(*) FROM PartNumbers WHERE ProductNo = @ProductNo";
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ProductNo", productNo);
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
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

        private async Task<bool> AddProductNumberAsync(ProductNumber product)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    var sql = @"INSERT INTO PartNumbers (ProductNo, ProductDesc, Serialization, ProductType, Manufacturer) 
                                VALUES (@ProductNo, @ProductDesc, @Serialization, @ProductType, @Manufacturer)";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ProductNo", product.ProductNo);
                        command.Parameters.AddWithValue("@ProductDesc", product.ProductDesc);
                        command.Parameters.AddWithValue("@Serialization", product.Serialization);
                        command.Parameters.AddWithValue("@ProductType", product.ProductType);
                        command.Parameters.AddWithValue("@Manufacturer", product.Manufacturer);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
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

        private async Task<bool> UpdateProductNumberAsync(ProductNumber product)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    var sql = @"UPDATE PartNumbers SET 
                                ProductDesc = @ProductDesc, 
                                Serialization = @Serialization, 
                                ProductType = @ProductType, 
                                Manufacturer = @Manufacturer 
                                WHERE ProductNo = @ProductNo";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ProductNo", product.ProductNo);
                        command.Parameters.AddWithValue("@ProductDesc", product.ProductDesc);
                        command.Parameters.AddWithValue("@Serialization", product.Serialization);
                        command.Parameters.AddWithValue("@ProductType", product.ProductType);
                        command.Parameters.AddWithValue("@Manufacturer", product.Manufacturer);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
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

        private async Task<bool> DeleteProductNumberAsync(ProductNumber product)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    var sql = "DELETE FROM PartNumbers WHERE ProductNo = @ProductNo";
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ProductNo", product.ProductNo);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
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

        private async Task<ProductNumber> GetProductDetailsAsync(string productNo)
        {
            string connectionString = _configuration.GetConnectionString("SQLConnection");
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    var sql = "SELECT * FROM PartNumbers WHERE ProductNo = @ProductNo";
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ProductNo", productNo);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return new ProductNumber
                                {
                                    ProductNo = reader["ProductNo"].ToString(),
                                    ProductDesc = reader["ProductDesc"].ToString(),
                                    Serialization = reader["Serialization"].ToString(),
                                    ProductType = reader["ProductType"].ToString(),
                                    Manufacturer = reader["Manufacturer"].ToString()
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

        private async Task LogAddActionAsync(ProductNumber product)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    var logquery = "INSERT INTO Log_PartNumbers (Action, Performed_By, Datetime, ProductNo, ProductDescription, Serialization, PartType, Manufacturer) VALUES (@Action, @ID, @Time, @ProductNo, @ProductDescription,@Serialization, @PartType, @Manufacturer)";
                    using (var cmd = new SqlCommand(logquery, conn))
                    {
                        cmd.Parameters.AddWithValue("@Action", "Add");
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now);
                        cmd.Parameters.AddWithValue("@ProductNo", product.ProductNo);
                        cmd.Parameters.AddWithValue("@ProductDescription", product.ProductDesc);
                        cmd.Parameters.AddWithValue("@Serialization", product.Serialization);
                        cmd.Parameters.AddWithValue("@PartType", product.ProductType);
                        cmd.Parameters.AddWithValue("@Manufacturer", product.Manufacturer);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging add action for part number {ProductNo}", product.ProductNo);
            }
        }

        private async Task LogUpdateActionAsync(ProductNumber newProduct, ProductNumber currentProduct)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    var logquery = "INSERT INTO Log_PartNumbers (Action, Performed_By, Datetime, ProductNo, ProductDescription, Serialization, PartType, Manufacturer) VALUES (@Action, @ID, @Time, @ProductNo, @ProductDescription,@Serialization, @PartType, @Manufacturer)";
                    using (var cmd = new SqlCommand(logquery, conn))
                    {
                        cmd.Parameters.AddWithValue("@Action", "Update");
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now);
                        cmd.Parameters.AddWithValue("@ProductNo", newProduct.ProductNo);

                        cmd.Parameters.AddWithValue("@ProductDescription", currentProduct.ProductDesc != newProduct.ProductDesc ? newProduct.ProductDesc : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Serialization", currentProduct.Serialization != newProduct.Serialization ? newProduct.Serialization : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@PartType", currentProduct.ProductType != newProduct.ProductType ? newProduct.ProductType : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Manufacturer", currentProduct.Manufacturer != newProduct.Manufacturer ? newProduct.Manufacturer : (object)DBNull.Value);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging update action for part number {ProductNo}", newProduct.ProductNo);
            }
        }

        private async Task LogDeleteActionAsync(ProductNumber product)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("SQLConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    var logquery = "INSERT INTO Log_PartNumbers (Action, Performed_By, Datetime, ProductNo) VALUES (@Action, @ID, @Time, @ProductNo)";
                    using (var cmd = new SqlCommand(logquery, conn))
                    {
                        cmd.Parameters.AddWithValue("@Action", "Delete");
                        cmd.Parameters.AddWithValue("@ID", User.Identity?.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now);
                        cmd.Parameters.AddWithValue("@ProductNo", product.ProductNo);
                        await cmd.ExecuteNonQueryAsync();
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