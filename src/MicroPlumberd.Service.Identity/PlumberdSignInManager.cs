using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Custom SignInManager that supports login with either username or email.
/// </summary>
public class PlumberdSignInManager : SignInManager<User>
{
    public PlumberdSignInManager(
        UserManager<User> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<User> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<User>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<User> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
    }

    /// <summary>
    /// Attempts to sign in the user using either username or email.
    /// First tries to find by username, then falls back to email.
    /// </summary>
    public override async Task<SignInResult> PasswordSignInAsync(
        string userNameOrEmail,
        string password,
        bool isPersistent,
        bool lockoutOnFailure)
    {
        var user = await UserManager.FindByNameAsync(userNameOrEmail);

        if (user == null)
        {
            user = await UserManager.FindByEmailAsync(userNameOrEmail);
        }

        if (user == null)
        {
            return SignInResult.Failed;
        }

        return await PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure);
    }
}
