using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QApp.Pages
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class ErrorHandlerModel : PageModel
    {
        public IActionResult OnGet(int statusCode)
        {
            if (statusCode == 403) // HTTP 403 is "Forbidden"
            {
                TempData["ErrorMessage"] = "You do not have permission to access that page.";
            }

            // Redirect to the home page, where the layout will display the error message.
            return RedirectToPage("/Home");
        }
    }
}