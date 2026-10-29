using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using ClientShowcase.Data;
using ClientShowcase.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.WebEncoders;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

var dataPath = builder.Configuration["Storage:DataPath"];
var storage = new StoragePaths(string.IsNullOrWhiteSpace(dataPath)
    ? Path.Combine(builder.Environment.ContentRootPath, "App_Data")
    : Path.GetFullPath(dataPath, builder.Environment.ContentRootPath));

builder.Services.AddSingleton(storage);
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<ClientRepository>();
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<AdminRepository>();
builder.Services.AddSingleton<ImageStore>();
builder.Services.AddSingleton<SetupCode>();
builder.Services.AddSingleton<IPasswordHasher<Admin>, PasswordHasher<Admin>>();
builder.Services.AddScoped<AdminAuth>();

// Persist encryption keys so admin sessions survive restarts. Keys are deliberately not wrapped with
// DPAPI: user-scoped DPAPI needs a loaded Windows user profile, which shared IIS app pools usually
// don't have, so every login form and auth cookie failed with a CryptographicException in production.
// The keys folder sits in App_Data, which is never served over HTTP.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(storage.KeysRoot))
    .SetApplicationName("ClientShowcase");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "showcase_admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/admin/login";
        options.LogoutPath = "/admin/logout";
        options.AccessDeniedPath = "/admin/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = AdminAuth.ValidatePrincipal;
    });
builder.Services.AddAuthorization();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "showcase_csrf";
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin");
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
    options.Conventions.AllowAnonymousToPage("/Admin/Setup");
});
builder.Services.AddRouting(options => options.LowercaseUrls = true);
// Emit Arabic and other non-Latin names as-is instead of as numeric entities.
builder.Services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("search", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

var app = builder.Build();

app.Services.GetRequiredService<Database>().Migrate();
InitialAdmin.EnsureCreated(app.Services, app.Configuration, app.Logger);
var setup = app.Services.GetRequiredService<SetupCode>();
if (setup.Code is not null)
{
    app.Logger.LogWarning(
        "No admin account exists yet. Open /admin/setup and enter this one-time setup code: {Code} (also saved in {File})",
        setup.Code, Path.Combine(storage.DataRoot, "setup-code.txt"));
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/error", "?code={0}");

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-Frame-Options"] = "DENY";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; " +
        "base-uri 'self'; form-action 'self'; object-src 'none'";
    if (context.Request.Path.StartsWithSegments("/admin"))
    {
        headers["Cache-Control"] = "no-store";
        headers["X-Robots-Tag"] = "noindex, nofollow";
    }
    await next();
});

app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=604800",
});
app.UseStaticFiles(new StaticFileOptions
{
    RequestPath = "/media",
    FileProvider = new PhysicalFileProvider(storage.MediaRoot),
    // Media files get random, never-reused names, so they can be cached forever.
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable",
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Live search suggestions for the public site: published clients only, public fields only.
app.MapGet("/api/search", (string? q, ClientRepository clients, HttpContext http) =>
{
    var query = (q ?? "").Trim();
    if (query.Length > 60) query = query[..60];
    http.Response.Headers.CacheControl = "no-store";
    if (query.Length == 0) return Results.Ok(Array.Empty<object>());
    var results = clients.SearchPublished(query, 8).Select(r => new
    {
        name = r.Client.FullName,
        role = r.Client.Headline,
        location = r.Client.Location,
        rank = r.Rank,
        url = "/clients/" + Uri.EscapeDataString(r.Client.Slug),
        thumb = r.Client.Cover?.ThumbUrl,
        initials = r.Client.Initials,
    });
    return Results.Ok(results);
}).RequireRateLimiting("search");

app.MapRazorPages();

app.Run();
