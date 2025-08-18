using System.ComponentModel.DataAnnotations;

namespace QApp.Pages.Authorization
{
    public class User
    {
        [Required]
        [StringLength(100)]
        public string TGI { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        public UserRole Role { get; set; }

        // For UI display
        public string RoleDisplayName => Role == UserRole.Admin ? "Administrator" : "Signatory";
    }

    public enum UserRole
    {
        Admin = 1,
        Signatory = 2
    }
}
