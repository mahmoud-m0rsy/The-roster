using System.Security.Claims;
using System.Security.Cryptography;
using ClientShowcase.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace ClientShowcase.Services;

public sealed class AdminAuth(AdminRepository admins, IPasswordHasher<Admin> hasher, ILogger<AdminAuth> logger)
{
    public const string StampClaim = "stamp";
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public enum LoginResult { Success, Invalid, LockedOut }

    public LoginResult Validate(string username, string password, out Admin? admin)
    {
        admin = admins.Find(username.Trim());
        if (admin is null)
        {
            // Spend comparable time so response timing doesn't reveal valid usernames.
            hasher.HashPassword(new Admin(), password);
            return LoginResult.Invalid;
        }
        if (admin.LockedUntil is { } until && until > DateTime.UtcNow) return LoginResult.LockedOut;

        var result = hasher.VerifyHashedPassword(admin, admin.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            var failures = admin.FailedLogins + 1;
            DateTime? lockUntil = failures >= MaxFailures ? DateTime.UtcNow.Add(LockoutDuration) : null;
            admins.RecordFailure(admin.Id, failures >= MaxFailures ? 0 : failures, lockUntil);
            if (lockUntil is not null) logger.LogWarning("Admin account {User} locked after repeated failed logins", admin.Username);
            return lockUntil is null ? LoginResult.Invalid : LoginResult.LockedOut;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            admin.SecurityStamp = admins.UpdatePassword(admin.Id, hasher.HashPassword(admin, password));
        admins.ResetFailures(admin.Id);
        return LoginResult.Success;
    }

    public string Hash(string password) => hasher.HashPassword(new Admin(), password);

    public bool Verify(Admin admin, string password) =>
        hasher.VerifyHashedPassword(admin, admin.PasswordHash, password) != PasswordVerificationResult.Failed;

    public static Task SignInAsync(HttpContext http, Admin admin)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, admin.Username),
                new Claim(StampClaim, admin.SecurityStamp),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });
    }

    /// <summary>Rejects cookies for deleted admins or sessions issued before a password change.</summary>
    public static async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var repo = context.HttpContext.RequestServices.GetRequiredService<AdminRepository>();
        var idClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal?.FindFirstValue(StampClaim);
        var admin = long.TryParse(idClaim, out var id) ? repo.FindById(id) : null;
        if (admin is null || admin.SecurityStamp != stamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public static long CurrentAdminId(ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// One-time code required to create the first admin account, so nobody else can claim a
/// freshly deployed site. Printed to the console and written to App_Data/setup-code.txt.
/// </summary>
public sealed class SetupCode
{
    private readonly string _file;
    public string? Code { get; private set; }

    public SetupCode(StoragePaths paths, AdminRepository admins)
    {
        _file = Path.Combine(paths.DataRoot, "setup-code.txt");
        if (admins.Any())
        {
            Clear();
            return;
        }
        Code = File.Exists(_file) ? File.ReadAllText(_file).Trim() : null;
        if (string.IsNullOrEmpty(Code))
        {
            Code = Convert.ToHexString(RandomNumberGenerator.GetBytes(5));
            File.WriteAllText(_file, Code);
        }
    }

    public bool Matches(string? candidate) =>
        Code is not null && candidate is not null &&
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(Code.ToUpperInvariant()),
            System.Text.Encoding.UTF8.GetBytes(candidate.Trim().ToUpperInvariant()));

    public void Clear()
    {
        Code = null;
        if (File.Exists(_file)) File.Delete(_file);
    }
}
