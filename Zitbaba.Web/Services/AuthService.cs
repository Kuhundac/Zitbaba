using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Services;

public enum LoginOutcome
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
    AccountSuspended,
    AccountDeactivated
}

public interface IAuthService
{
    Task<LoginOutcome> LoginAsync(string email, string password, bool rememberMe);
    Task LogoutAsync();
    Task SignInAsync(ApplicationUser user);
    Task RequestPasswordResetAsync(string email, Func<string, string> buildResetLink);
    Task<IdentityResult> ResetPasswordAsync(string email, string encodedToken, string newPassword);
    Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal);
}

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        ILogger<AuthService> logger)
    {
        _users = users;
        _signIn = signIn;
        _logger = logger;
    }

    public async Task<LoginOutcome> LoginAsync(string email, string password, bool rememberMe)
    {
        var user = await _users.FindByEmailAsync(email.Trim());
        if (user is null) return LoginOutcome.InvalidCredentials;

        // Check the password first (this also counts failures toward lockout),
        // so account status is never revealed to someone who doesn't know the password.
        var check = await _signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (check.IsLockedOut) return LoginOutcome.LockedOut;
        if (!check.Succeeded) return LoginOutcome.InvalidCredentials;

        if (user.AccountStatus == AccountStatus.Suspended) return LoginOutcome.AccountSuspended;
        if (user.AccountStatus == AccountStatus.Deactivated) return LoginOutcome.AccountDeactivated;

        await _signIn.SignInAsync(user, isPersistent: rememberMe);
        return LoginOutcome.Succeeded;
    }

    public Task LogoutAsync() => _signIn.SignOutAsync();

    public Task SignInAsync(ApplicationUser user) => _signIn.SignInAsync(user, isPersistent: false);

    public async Task RequestPasswordResetAsync(string email, Func<string, string> buildResetLink)
    {
        var user = await _users.FindByEmailAsync(email.Trim());
        if (user is null) return; // never reveal whether an email exists

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        // No email provider yet: log the link so a developer can copy it.
        _logger.LogWarning("PASSWORD RESET LINK for {Email}: {Link}", user.Email, buildResetLink(encoded));
    }

    public async Task<IdentityResult> ResetPasswordAsync(string email, string encodedToken, string newPassword)
    {
        var user = await _users.FindByEmailAsync(email.Trim());
        if (user is null) return IdentityResult.Success; // don't leak existence

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
        }
        catch (FormatException)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidToken",
                Description = "This reset link is invalid or has expired."
            });
        }

        return await _users.ResetPasswordAsync(user, token, newPassword);
    }

    public Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal) => _users.GetUserAsync(principal);
}
