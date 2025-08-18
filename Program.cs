using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.IISIntegration;
using QApp.Pages.Authorization; 

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(IISDefaults.AuthenticationScheme);


builder.Services.AddAuthorization(options =>
{
    // Policy for admin-only access
    options.AddPolicy("AdminOnly", policy =>
        policy.Requirements.Add(new AdminRequirement()));

    // Policy for signatory access (includes admins)
    options.AddPolicy("SignatoryAccess", policy =>
        policy.Requirements.Add(new SignatoryRequirement()));

    options.AddPolicy("SignatoryRoleRequired", policy =>
     policy.Requirements.Add(new HasSignatoryRoleRequirement()));

    // Policy for any authenticated user in the system
    options.AddPolicy("AuthenticatedUser", policy =>
        policy.Requirements.Add(new AuthenticatedUserRequirement()));
});
// --- END: Added Authorization Configuration ---
builder.Services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, SignatoryAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, AuthenticatedUserAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, HasSignatoryRoleHandler>();
// Add services to the container.
builder.Services.AddRazorPages();

// Add HttpContextAccessor to make User context available in services
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// --- START: Added Status Code Handling ---
// This catches 403 (Forbidden) responses and re-routes the request to our ErrorHandler page.
app.UseStatusCodePagesWithReExecute("/ErrorHandler", "?statusCode={0}");
// --- END: Added Status Code Handling ---

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapFallbackToPage("/Home");

app.Run();
