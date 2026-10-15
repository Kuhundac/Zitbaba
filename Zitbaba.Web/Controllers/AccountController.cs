using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Infrastructure.Extensions;
using Zitbaba.Web.Models;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Controllers;

public class AccountController : Controller
{
    private readonly IRegistrationService _registration;
    private readonly IAuthService _auth;
    private readonly UserManager<ApplicationUser> _users;

    public AccountController(
        IRegistrationService registration, IAuthService auth, UserManager<ApplicationUser> users)
    {
        _registration = registration;
        _auth = auth;
        _users = users;
    }

    // ---------- Register ----------
    [HttpGet, AllowAnonymous]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    [EnableRateLimiting(ServiceCollectionExtensions.AuthRateLimitPolicy)]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _registration.RegisterAsync(
            new RegistrationRequest(model.FullName, model.Email, model.Phone, model.Password));

        if (!result.Succeeded)
        {
            foreach (var error in result.Result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await _auth.SignInAsync(result.User!);
        return RedirectToAction("Index", "Home");
    }

    // ---------- Login / Logout ----------
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    [EnableRateLimiting(ServiceCollectionExtensions.AuthRateLimitPolicy)]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var outcome = await _auth.LoginAsync(model.Email, model.Password, model.RememberMe);
        switch (outcome)
        {
            case LoginOutcome.Succeeded:
                return !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)
                    ? LocalRedirect(model.ReturnUrl)
                    : RedirectToAction("Index", "Home");
            case LoginOutcome.LockedOut:
                ModelState.AddModelError(string.Empty, "Too many failed attempts. Try again in 15 minutes.");
                break;
            case LoginOutcome.AccountSuspended:
                ModelState.AddModelError(string.Empty, "This account has been suspended. Please contact support.");
                break;
            case LoginOutcome.AccountDeactivated:
                ModelState.AddModelError(string.Empty, "This account has been deactivated.");
                break;
            default:
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                break;
        }
        return View(model);
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _auth.LogoutAsync();
        return RedirectToAction("Index", "Home");
    }

    // ---------- Forgot / reset password ----------
    [HttpGet, AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    [EnableRateLimiting(ServiceCollectionExtensions.AuthRateLimitPolicy)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        await _auth.RequestPasswordResetAsync(model.Email, token =>
            Url.Action(nameof(ResetPassword), "Account",
                new { email = model.Email, token }, Request.Scheme)!);

        return View("ForgotPasswordConfirmation");
    }

    [HttpGet, AllowAnonymous]
    public IActionResult ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token)) return BadRequest();
        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    [EnableRateLimiting(ServiceCollectionExtensions.AuthRateLimitPolicy)]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _auth.ResetPasswordAsync(model.Email, model.Token, model.Password);
        if (result.Succeeded) return View("ResetPasswordConfirmation");

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
        return View(model);
    }

    // ---------- Profile (private, own data only) ----------
    [HttpGet, Authorize]
    public async Task<IActionResult> Profile()
    {
        var user = await _auth.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        var roles = await _users.GetRolesAsync(user);
        return View(new ProfileViewModel
        {
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Phone = user.Phone,
            AccountStatus = user.AccountStatus.ToString(),
            Roles = roles.ToList()
        });
    }

    [HttpGet, AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
