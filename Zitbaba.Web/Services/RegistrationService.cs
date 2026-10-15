using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Infrastructure.Security;

namespace Zitbaba.Web.Services;

public record RegistrationRequest(string FullName, string Email, string Phone, string Password);

public record RegistrationResult(IdentityResult Result, ApplicationUser? User)
{
    public bool Succeeded => Result.Succeeded;
}

public interface IRegistrationService
{
    Task<RegistrationResult> RegisterAsync(RegistrationRequest request);
}

public class RegistrationService : IRegistrationService
{
    private readonly UserManager<ApplicationUser> _users;

    public RegistrationService(UserManager<ApplicationUser> users) => _users = users;

    public async Task<RegistrationResult> RegisterAsync(RegistrationRequest request)
    {
        var email = request.Email.Trim();
        var phone = request.Phone.Trim();

        if (await _users.Users.AnyAsync(u => u.Phone == phone))
        {
            return new RegistrationResult(
                IdentityResult.Failed(new IdentityError
                {
                    Code = "DuplicatePhone",
                    Description = "That phone number is already registered."
                }), null);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = request.FullName.Trim(),
            Phone = phone
        };

        var created = await _users.CreateAsync(user, request.Password);
        if (!created.Succeeded) return new RegistrationResult(created, null);

        // Self-registration only ever grants Buyer. Admin/Seller/etc. are granted by admins.
        var roleResult = await _users.AddToRoleAsync(user, RoleNames.Buyer);
        if (!roleResult.Succeeded)
        {
            await _users.DeleteAsync(user); // don't leave a role-less account behind
            return new RegistrationResult(roleResult, null);
        }

        return new RegistrationResult(IdentityResult.Success, user);
    }
}
