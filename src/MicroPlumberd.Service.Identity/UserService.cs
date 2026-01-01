using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MicroPlumberd.Services.Identity.Aggregates;
using MicroPlumberd.Services.Identity.ReadModels;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Implementation of ASP.NET Core Identity stores for users using event sourcing with MicroPlumberd
/// </summary>
public class UserStore :
    IUserStore<User>,
    IUserEmailStore<User>,
    IUserPasswordStore<User>,
    IUserSecurityStampStore<User>,
    IUserLockoutStore<User>,
    IUserTwoFactorStore<User>,
    IUserPhoneNumberStore<User>,
    IUserLoginStore<User>,
    IUserClaimStore<User>,
    IUserRoleStore<User>,
    IUserAuthenticationTokenStore<User>,
    IUserAuthenticatorKeyStore<User>,
    IUserTwoFactorRecoveryCodeStore<User>,
    IQueryableUserStore<User>
{
    private readonly IPlumber _plumber;
    private readonly UsersModel _usersModel;
    private readonly RolesModel _rolesModel;
    private readonly AuthenticationModel _authenticationModel;
    private readonly UserAuthorizationModel _userAuthorizationModel;
    private readonly TokenModel _tokenModel;
    private readonly ExternalLoginModel _externalLoginModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserStore"/> class.
    /// </summary>
    /// <param name="plumber">The plumber instance for event sourcing operations.</param>
    /// <param name="usersModel">The read model for user queries.</param>
    /// <param name="rolesModel">The read model for role queries.</param>
    /// <param name="authenticationModel">The read model for authentication data.</param>
    /// <param name="userAuthorizationModel">The read model for user authorization data.</param>
    /// <param name="tokenModel">The read model for token data.</param>
    /// <param name="externalLoginModel">The read model for external login data.</param>
    /// <exception cref="ArgumentNullException">Thrown when any of the required parameters is null.</exception>
    public UserStore(
        IPlumber plumber,
        UsersModel usersModel,
        RolesModel rolesModel,
        AuthenticationModel authenticationModel,
        UserAuthorizationModel userAuthorizationModel,
        TokenModel tokenModel, ExternalLoginModel externalLoginModel)
    {
        _plumber = plumber ?? throw new ArgumentNullException(nameof(plumber));
        _usersModel = usersModel ?? throw new ArgumentNullException(nameof(usersModel));
        _rolesModel = rolesModel ?? throw new ArgumentNullException(nameof(rolesModel));
        _authenticationModel = authenticationModel ?? throw new ArgumentNullException(nameof(authenticationModel));
        _userAuthorizationModel = userAuthorizationModel ?? throw new ArgumentNullException(nameof(userAuthorizationModel));
        _tokenModel = tokenModel ?? throw new ArgumentNullException(nameof(tokenModel));
        _externalLoginModel = externalLoginModel;
    }

    // Helper method to convert string ID to UserIdentifier
    private UserIdentifier GetUserIdentifier(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

        if (!UserIdentifier.TryParse(userId, null, out var userIdentifier))
            throw new ArgumentException("Invalid user ID format", nameof(userId));

        return userIdentifier;
    }

    // Helper method to extract expected version from concurrency stamp
    private CompositeStreamVersion GetExpectedVersion(string concurrencyStamp)
    {
        if (string.IsNullOrEmpty(concurrencyStamp))
            return CompositeStreamVersion.Empty;

        if (!CompositeStreamVersion.TryParse(concurrencyStamp, null, out var version))
            return CompositeStreamVersion.Empty;

        return version;
    }

    #region IUserStore<User> Implementation

    /// <summary>
    /// Gets a queryable collection of all users in the store.
    /// </summary>
    public IQueryable<User> Users => _usersModel.GetAllUsers().AsQueryable();

    /// <summary>
    /// Creates a new user in the store.
    /// </summary>
    /// <param name="user">The user to create.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing an <see cref="IdentityResult"/> indicating the result of the operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var userId = GetUserIdentifier(user.Id);

            // Create profile aggregate
            var userProfile = UserProfileAggregate.Create(
                userId,
                user.UserName,
                user.NormalizedUserName,
                user.Email,
                user.NormalizedEmail,
                user.PhoneNumber);

            await _plumber.SaveNew(userProfile);

            // Create identity aggregate
            var identityUser = IdentityUserAggregate.Create(
                userId,
                user.PasswordHash,
                user.LockoutEnabled);

            await _plumber.SaveNew(identityUser);

            // Create authorization aggregate
            var authorizationUser = AuthorizationUserAggregate.Create(userId);
            await _plumber.SaveNew(authorizationUser);

            // Create external login aggregate
            var externalLoginAggregate = ExternalLoginAggregate.Create(userId);
            await _plumber.SaveNew(externalLoginAggregate);

            // Create token aggregate
            var tokenAggregate = TokenAggregate.Create(userId);
            await _plumber.SaveNew(tokenAggregate);

            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            return IdentityResult.Failed(new IdentityError { Description = ex.Message });
        }
    }

    /// <summary>
    /// Deletes a user from the store.
    /// </summary>
    /// <param name="user">The user to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing an <see cref="IdentityResult"/> indicating the result of the operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var userId = GetUserIdentifier(user.Id);
            var expectedVersion = GetExpectedVersion(user.ConcurrencyStamp);

            // Delete profile
            var userProfile = await _plumber.Get<UserProfileAggregate>(userId);
            userProfile.Delete(); // We're not using concurrency stamp anymore
            await _plumber.SaveChanges(userProfile);

            // Delete identity
            var identityUser = await _plumber.Get<IdentityUserAggregate>(userId);
            identityUser.Delete();
            await _plumber.SaveChanges(identityUser);

            // Delete authorization
            var authorizationUser = await _plumber.Get<AuthorizationUserAggregate>(userId);
            authorizationUser.Delete();
            await _plumber.SaveChanges(authorizationUser);

            // Delete external logins
            var externalLoginAggregate = await _plumber.Get<ExternalLoginAggregate>(userId);
            externalLoginAggregate.Delete();
            await _plumber.SaveChanges(externalLoginAggregate);

            // Delete tokens
            var tokenAggregate = await _plumber.Get<TokenAggregate>(userId);
            tokenAggregate.Delete();
            await _plumber.SaveChanges(tokenAggregate);

            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            return IdentityResult.Failed(new IdentityError { Description = ex.Message });
        }
    }

    /// <summary>
    /// Finds a user by their unique identifier.
    /// </summary>
    /// <param name="userId">The user ID to search for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the user if found, or null if not found.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="userId"/> is invalid.</exception>
    public async Task<User> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var userIdentifier = GetUserIdentifier(userId);
        return _usersModel.GetById(userIdentifier);
    }

    /// <summary>
    /// Finds a user by their normalized user name.
    /// </summary>
    /// <param name="normalizedUserName">The normalized user name to search for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the user if found, or null if not found.</returns>
    public async Task<User> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _usersModel.GetByNormalizedUserName(normalizedUserName);
    }

    /// <summary>
    /// Gets the normalized user name for the specified user.
    /// </summary>
    /// <param name="user">The user whose normalized user name should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the normalized user name for the specified user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<string> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.NormalizedUserName);
    }

    /// <summary>
    /// Gets the user identifier for the specified user.
    /// </summary>
    /// <param name="user">The user whose identifier should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the user identifier for the specified user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.Id);
    }

    /// <summary>
    /// Gets the user name for the specified user.
    /// </summary>
    /// <param name="user">The user whose user name should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the user name for the specified user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<string> GetUserNameAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.UserName);
    }

    /// <summary>
    /// Sets the normalized user name for the specified user.
    /// </summary>
    /// <param name="user">The user whose normalized user name should be set.</param>
    /// <param name="normalizedName">The normalized user name to set.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task SetNormalizedUserNameAsync(User user, string normalizedName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the user name for the specified user.
    /// </summary>
    /// <param name="user">The user whose user name should be set.</param>
    /// <param name="userName">The user name to set.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task SetUserNameAsync(User user, string userName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.UserName = userName;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates the specified user in the store.
    /// </summary>
    /// <param name="user">The user to update.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing an <see cref="IdentityResult"/> indicating the result of the operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var userId = GetUserIdentifier(user.Id);
          

            // Update profile
            var userProfile = await _plumber.Get<UserProfileAggregate>(userId);

            // Let the aggregate decide if a change is needed
            userProfile.ChangeUserName(user.UserName, user.NormalizedUserName);
            userProfile.ChangeEmail(user.Email, user.NormalizedEmail);

           

            userProfile.ChangePhoneNumber(user.PhoneNumber);

           

            // Only save if there are pending changes
            if (userProfile.HasPendingChanges) await _plumber.SaveChanges(userProfile);

            // Change identity
            var identityUser = await _plumber.Get<IdentityUserAggregate>(userId);

            identityUser.ChangePasswordHash(user.PasswordHash);
            identityUser.ChangeTwoFactorEnabled(user.TwoFactorEnabled);
            identityUser.ChangeLockoutEnabled(user.LockoutEnabled);
            identityUser.ChangeLockoutEnd(user.LockoutEnd);

            // Only save if there are pending changes
            if (identityUser.HasPendingChanges) await _plumber.SaveChanges(identityUser);

            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            return IdentityResult.Failed(new IdentityError { Description = ex.Message });
        }
    }

    #endregion

    #region IUserEmailStore<User> Implementation

    /// <summary>
    /// Finds a user by their normalized email address.
    /// </summary>
    /// <param name="normalizedEmail">The normalized email address to search for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the user if found, or null if not found.</returns>
    public async Task<User> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _usersModel.GetByNormalizedEmail(normalizedEmail);
    }

    /// <summary>
    /// Gets the email address for the specified user.
    /// </summary>
    /// <param name="user">The user whose email address should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the email address for the specified user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<string> GetEmailAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.Email);
    }

    /// <summary>
    /// Gets a flag indicating whether the email address for the specified user has been confirmed.
    /// </summary>
    /// <param name="user">The user whose email confirmation status should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing a flag indicating whether the email address has been confirmed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<bool> GetEmailConfirmedAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.EmailConfirmed);
    }

    /// <summary>
    /// Gets the normalized email address for the specified user.
    /// </summary>
    /// <param name="user">The user whose normalized email address should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the normalized email address for the specified user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<string> GetNormalizedEmailAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.NormalizedEmail);
    }

    /// <summary>
    /// Sets the email address for the specified user.
    /// </summary>
    /// <param name="user">The user whose email address should be set.</param>
    /// <param name="email">The email address to set.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task SetEmailAsync(User user, string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        if (email != user.Email)
        {
            var userId = GetUserIdentifier(user.Id);
            var userProfile = await _plumber.Get<UserProfileAggregate>(userId);
            userProfile.ChangeEmail(email, user.NormalizedEmail);
            if (userProfile.HasPendingChanges)
                await _plumber.SaveChanges(userProfile);
        }

        user.Email = email;
    }

    /// <summary>
    /// Sets a flag indicating whether the specified user's email address has been confirmed.
    /// </summary>
    /// <param name="user">The user whose email confirmation status should be set.</param>
    /// <param name="confirmed">A flag indicating whether the email address is confirmed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task SetEmailConfirmedAsync(User user, bool confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        if (confirmed && !user.EmailConfirmed)
        {
            var userId = GetUserIdentifier(user.Id);
            var userProfile = await _plumber.Get<UserProfileAggregate>(userId);
            userProfile.ConfirmEmail();
            if (userProfile.HasPendingChanges)
                await _plumber.SaveChanges(userProfile);
        }

        
    }

    /// <summary>
    /// Sets the normalized email address for the specified user.
    /// </summary>
    /// <param name="user">The user whose normalized email address should be set.</param>
    /// <param name="normalizedEmail">The normalized email address to set.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task SetNormalizedEmailAsync(User user, string normalizedEmail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    #endregion

    #region IUserPasswordStore<User> Implementation

    /// <summary>
    /// Gets the password hash for the specified user.
    /// </summary>
    /// <param name="user">The user whose password hash should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the password hash for the specified user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<string> GetPasswordHashAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.PasswordHash);
    }

    /// <summary>
    /// Returns a flag indicating whether the specified user has a password.
    /// </summary>
    /// <param name="user">The user to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing true if the user has a password; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<bool> HasPasswordAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));
    }

    /// <summary>
    /// Sets the password hash for the specified user.
    /// </summary>
    /// <param name="user">The user whose password hash should be set.</param>
    /// <param name="passwordHash">The password hash to set.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task SetPasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        if (passwordHash != user.PasswordHash)
        {
            var userId = GetUserIdentifier(user.Id);
            var identityUser = await _plumber.Get<IdentityUserAggregate>(userId);
            if (!identityUser.IsNew)
            {
                identityUser.ChangePasswordHash(passwordHash);
                if (identityUser.HasPendingChanges)
                    await _plumber.SaveChanges(identityUser);
            }
        }

        user.PasswordHash = passwordHash;
    }

    #endregion

    #region IUserSecurityStampStore<User> Implementation

    /// <summary>
    /// Gets the security stamp for the specified user.
    /// </summary>
    /// <param name="user">The user whose security stamp should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the security stamp for the specified user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<string> GetSecurityStampAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.SecurityStamp);
    }

    /// <summary>
    /// Sets the security stamp for the specified user.
    /// </summary>
    /// <param name="user">The user whose security stamp should be set.</param>
    /// <param name="stamp">The security stamp to set.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    #endregion

    #region IUserLockoutStore<User> Implementation

    /// <summary>
    /// Gets the number of failed access attempts for the specified user.
    /// </summary>
    /// <param name="user">The user whose access failed count should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the number of failed access attempts for the specified user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<int> GetAccessFailedCountAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.AccessFailedCount);
    }

    /// <summary>
    /// Gets a flag indicating whether user lockout can be enabled for the specified user.
    /// </summary>
    /// <param name="user">The user whose lockout enabled status should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing true if lockout can be enabled; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<bool> GetLockoutEnabledAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.LockoutEnabled);
    }

    /// <summary>
    /// Gets the date and time, in UTC, when the specified user's lockout should end.
    /// </summary>
    /// <param name="user">The user whose lockout end date should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the lockout end date for the specified user, or null if not locked out.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.LockoutEnd);
    }

    /// <summary>
    /// Increments the access failed count for the specified user.
    /// </summary>
    /// <param name="user">The user whose access failed count should be incremented.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the new access failed count.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task<int> IncrementAccessFailedCountAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        var identityUser = await _plumber.Get<IdentityUserAggregate>(userId);

        var ret = identityUser.IncrementAccessFailedCount();
        await _plumber.SaveChanges(identityUser);

        // Return the updated count from the aggregate
        return ret;
    }

    /// <summary>
    /// Resets the access failed count for the specified user.
    /// </summary>
    /// <param name="user">The user whose access failed count should be reset.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task ResetAccessFailedCountAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        var identityUser = await _plumber.Get<IdentityUserAggregate>(userId);

        identityUser.ResetAccessFailedCount();
        await _plumber.SaveChanges(identityUser);
    }

    /// <summary>
    /// Sets a flag indicating whether the specified user can be locked out.
    /// </summary>
    /// <param name="user">The user whose lockout enabled status should be set.</param>
    /// <param name="enabled">A flag indicating whether the user can be locked out.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task SetLockoutEnabledAsync(User user, bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the date and time, in UTC, when the specified user's lockout should end.
    /// </summary>
    /// <param name="user">The user whose lockout end date should be set.</param>
    /// <param name="lockoutEnd">The date and time when the lockout should end, or null to clear the lockout.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task SetLockoutEndDateAsync(User user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    #endregion

    #region IUserTwoFactorStore<User> Implementation

    /// <summary>
    /// Gets a flag indicating whether two-factor authentication is enabled for the specified user.
    /// </summary>
    /// <param name="user">The user whose two-factor authentication enabled status should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing true if two-factor authentication is enabled; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<bool> GetTwoFactorEnabledAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.TwoFactorEnabled);
    }

    /// <summary>
    /// Sets a flag indicating whether two-factor authentication is enabled for the specified user.
    /// </summary>
    /// <param name="user">The user whose two-factor authentication enabled status should be set.</param>
    /// <param name="enabled">A flag indicating whether two-factor authentication is enabled.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task SetTwoFactorEnabledAsync(User user, bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    #endregion

    #region IUserPhoneNumberStore<User> Implementation

    /// <summary>
    /// Gets the phone number for the specified user.
    /// </summary>
    /// <param name="user">The user whose phone number should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the phone number for the specified user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<string> GetPhoneNumberAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.PhoneNumber);
    }

    /// <summary>
    /// Gets a flag indicating whether the phone number for the specified user has been confirmed.
    /// </summary>
    /// <param name="user">The user whose phone number confirmation status should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing a flag indicating whether the phone number has been confirmed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task<bool> GetPhoneNumberConfirmedAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.PhoneNumberConfirmed);
    }

    /// <summary>
    /// Sets the phone number for the specified user.
    /// </summary>
    /// <param name="user">The user whose phone number should be set.</param>
    /// <param name="phoneNumber">The phone number to set.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task SetPhoneNumberAsync(User user, string phoneNumber, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets a flag indicating whether the specified user's phone number has been confirmed.
    /// </summary>
    /// <param name="user">The user whose phone number confirmation status should be set.</param>
    /// <param name="confirmed">A flag indicating whether the phone number is confirmed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public Task SetPhoneNumberConfirmedAsync(User user, bool confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }

    #endregion

    #region IUserLoginStore<User> Implementation

    /// <summary>
    /// Adds an external login to the specified user.
    /// </summary>
    /// <param name="user">The user to add the login to.</param>
    /// <param name="login">The external login information to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> or <paramref name="login"/> is null.</exception>
    public async Task AddLoginAsync(User user, UserLoginInfo login, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (login == null)
            throw new ArgumentNullException(nameof(login));

        var userId = GetUserIdentifier(user.Id);
        var externalLoginAggregate = await _plumber.Get<ExternalLoginAggregate>(userId);

        var provider = new ExternalLoginProvider(login.LoginProvider);
        var key = new ExternalLoginKey(login.ProviderKey);

        externalLoginAggregate.AddLogin(provider, key, login.ProviderDisplayName);
        await _plumber.SaveChanges(externalLoginAggregate);
    }

    /// <summary>
    /// Finds a user by their external login information.
    /// </summary>
    /// <param name="loginProvider">The login provider name.</param>
    /// <param name="providerKey">The key provided by the login provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the user if found, or null if not found.</returns>
    public async Task<User> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _usersModel.GetByExternalLogin(loginProvider, providerKey);
    }

    /// <summary>
    /// Gets the external logins for the specified user.
    /// </summary>
    /// <param name="user">The user whose external logins should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing a list of external logins for the specified user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task<IList<UserLoginInfo>> GetLoginsAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        var logins = _externalLoginModel.GetLoginsForUser(userId);

        return logins.Select(l => new UserLoginInfo(
            l.ProviderName,
            l.ProviderKey,
            l.DisplayName
        )).ToList();
    }

    /// <summary>
    /// Removes an external login from the specified user.
    /// </summary>
    /// <param name="user">The user to remove the login from.</param>
    /// <param name="loginProvider">The login provider name.</param>
    /// <param name="providerKey">The key provided by the login provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task RemoveLoginAsync(User user, string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        var externalLoginAggregate = await _plumber.Get<ExternalLoginAggregate>(userId);

        var provider = new ExternalLoginProvider(loginProvider);
        var key = new ExternalLoginKey(providerKey);

        externalLoginAggregate.RemoveLogin(provider, key);
        await _plumber.SaveChanges(externalLoginAggregate);
    }

    #endregion

    #region IUserClaimStore<User> Implementation

    /// <summary>
    /// Gets the claims associated with the specified user.
    /// </summary>
    /// <param name="user">The user whose claims should be retrieved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing a list of claims for the specified user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task<IList<Claim>> GetClaimsAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        return _userAuthorizationModel.GetClaims(userId).ToList();
    }

    /// <summary>
    /// Adds claims to the specified user.
    /// </summary>
    /// <param name="user">The user to add claims to.</param>
    /// <param name="claims">The claims to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> or <paramref name="claims"/> is null.</exception>
    public async Task AddClaimsAsync(User user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (claims == null)
            throw new ArgumentNullException(nameof(claims));

        var userId = GetUserIdentifier(user.Id);
        var authorizationAggregate = await _plumber.Get<AuthorizationUserAggregate>(userId);

        foreach (var claim in claims)
        {
            var claimType = new ClaimType(claim.Type);
            var claimValue = new ClaimValue(claim.Value);

            authorizationAggregate.AddClaim(claimType, claimValue);
        }

        await _plumber.SaveChanges(authorizationAggregate);
    }

    /// <summary>
    /// Replaces a claim on the specified user with a new claim.
    /// </summary>
    /// <param name="user">The user to replace the claim on.</param>
    /// <param name="claim">The claim to replace.</param>
    /// <param name="newClaim">The new claim to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/>, <paramref name="claim"/>, or <paramref name="newClaim"/> is null.</exception>
    public async Task ReplaceClaimAsync(User user, Claim claim, Claim newClaim, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (claim == null)
            throw new ArgumentNullException(nameof(claim));
        if (newClaim == null)
            throw new ArgumentNullException(nameof(newClaim));

        var userId = GetUserIdentifier(user.Id);
        var authorizationAggregate = await _plumber.Get<AuthorizationUserAggregate>(userId);

        // Remove the old claim
        var oldClaimType = new ClaimType(claim.Type);
        var oldClaimValue = new ClaimValue(claim.Value);
        authorizationAggregate.RemoveClaim(oldClaimType, oldClaimValue);

        // Add the new claim
        var newClaimType = new ClaimType(newClaim.Type);
        var newClaimValue = new ClaimValue(newClaim.Value);
        authorizationAggregate.AddClaim(newClaimType, newClaimValue);

        await _plumber.SaveChanges(authorizationAggregate);
    }

    /// <summary>
    /// Removes claims from the specified user.
    /// </summary>
    /// <param name="user">The user to remove claims from.</param>
    /// <param name="claims">The claims to remove.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> or <paramref name="claims"/> is null.</exception>
    public async Task RemoveClaimsAsync(User user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (claims == null)
            throw new ArgumentNullException(nameof(claims));

        var userId = GetUserIdentifier(user.Id);
        var authorizationAggregate = await _plumber.Get<AuthorizationUserAggregate>(userId);

        foreach (var claim in claims)
        {
            var claimType = new ClaimType(claim.Type);
            var claimValue = new ClaimValue(claim.Value);

            authorizationAggregate.RemoveClaim(claimType, claimValue);
        }

        await _plumber.SaveChanges(authorizationAggregate);
    }

    /// <summary>
    /// Gets a list of users who possess the specified claim.
    /// </summary>
    /// <param name="claim">The claim to search for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing a list of users who possess the specified claim.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="claim"/> is null.</exception>
    public async Task<IList<User>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (claim == null)
            throw new ArgumentNullException(nameof(claim));

        // This requires a query across all users with this claim
        // Since we don't have a direct index for this, we'll need to search through all users
        // This is potentially inefficient and might need optimization in a real system

        // For now, we'll just return an empty list as a placeholder
        // In a real implementation, you would need to create a specialized read model for this query
        return new List<User>();
    }

    #endregion

    #region IUserRoleStore<User> Implementation

    /// <summary>
    /// Adds a user to a role.
    /// </summary>
    /// <param name="user">The user to add to the role.</param>
    /// <param name="roleName">The name of the role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="roleName"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the specified role does not exist.</exception>
    public async Task AddToRoleAsync(User user, string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name cannot be empty", nameof(roleName));

        // Get the role ID from the normalized name
        string normalizedRoleName = roleName.ToUpperInvariant();
        var roleId = _rolesModel.GetIdByNormalizedName(normalizedRoleName);

        if (roleId.Equals(default(RoleIdentifier)))
            throw new InvalidOperationException($"Role '{roleName}' does not exist");

        var userId = GetUserIdentifier(user.Id);
        var authorizationAggregate = await _plumber.Get<AuthorizationUserAggregate>(userId);

        authorizationAggregate.AddRole(roleId);
        await _plumber.SaveChanges(authorizationAggregate);
    }

    /// <summary>
    /// Removes a user from a role.
    /// </summary>
    /// <param name="user">The user to remove from the role.</param>
    /// <param name="roleName">The name of the role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="roleName"/> is null or whitespace.</exception>
    public async Task RemoveFromRoleAsync(User user, string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name cannot be empty", nameof(roleName));

        // Get the role ID from the normalized name
        string normalizedRoleName = roleName.ToUpperInvariant();
        var roleId = _rolesModel.GetIdByNormalizedName(normalizedRoleName);

        if (roleId.Equals(default(RoleIdentifier)))
            return; // Role doesn't exist, nothing to remove

        var userId = GetUserIdentifier(user.Id);
        var authorizationAggregate = await _plumber.Get<AuthorizationUserAggregate>(userId);

        authorizationAggregate.RemoveRole(roleId);
        await _plumber.SaveChanges(authorizationAggregate);
    }

    /// <summary>
    /// Gets the roles for a user.
    /// </summary>
    /// <param name="user">The user to get roles for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of role names for the user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task<IList<string>> GetRolesAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        return _userAuthorizationModel.GetRoleNames(userId);
        
    }

    /// <summary>
    /// Determines whether a user is in the specified role.
    /// </summary>
    /// <param name="user">The user to check.</param>
    /// <param name="roleName">The name of the role to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the user is in the role; otherwise, false.</returns>
    public async Task<bool> IsInRoleAsync(User user, string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var userId = GetUserIdentifier(user.Id);
        return _userAuthorizationModel.IsInRole(userId, roleName);
    }

    /// <summary>
    /// Gets all users in the specified role.
    /// </summary>
    /// <param name="roleName">The name of the role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of users in the role.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="roleName"/> is null or whitespace.</exception>
    public async Task<IList<User>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name cannot be empty", nameof(roleName));

        // Get the role ID from the normalized name
        string normalizedRoleName = roleName.ToUpperInvariant();
        var roleId = _rolesModel.GetIdByNormalizedName(normalizedRoleName);

        if (roleId.Equals(default(RoleIdentifier)))
            return new List<User>(); // Role doesn't exist

        var userIds = _userAuthorizationModel.GetUsersInRole(roleId);

        // Convert user IDs to User objects
        var users = new List<User>();
        foreach (var userId in userIds)
        {
            var user = _usersModel.GetById(userId);
            if (user != null)
                users.Add(user);
        }

        return users;
    }

    #endregion

    #region IUserAuthenticationTokenStore<User> Implementation

    /// <summary>
    /// Gets a token value for a user.
    /// </summary>
    /// <param name="user">The user to get the token for.</param>
    /// <param name="loginProvider">The login provider associated with the token.</param>
    /// <param name="name">The name of the token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token value, or null if not found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or empty.</exception>
    public async Task<string> GetTokenAsync(User user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Token name cannot be empty", nameof(name));

        var userId = GetUserIdentifier(user.Id);
        return _tokenModel.GetToken(userId, name, loginProvider);
    }

    /// <summary>
    /// Sets a token value for a user.
    /// </summary>
    /// <param name="user">The user to set the token for.</param>
    /// <param name="loginProvider">The login provider associated with the token.</param>
    /// <param name="name">The name of the token.</param>
    /// <param name="value">The token value to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or empty.</exception>
    public async Task SetTokenAsync(User user, string loginProvider, string name, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Token name cannot be empty", nameof(name));

        var userId = GetUserIdentifier(user.Id);
        var tokenAggregate = await _plumber.Get<TokenAggregate>(userId);

        var tokenName = new TokenName(name);
        var tokenValue = new TokenValue(value);

        tokenAggregate.SetToken(tokenName, tokenValue, loginProvider);
        await _plumber.SaveChanges(tokenAggregate);
    }

    /// <summary>
    /// Removes a token for a user.
    /// </summary>
    /// <param name="user">The user to remove the token from.</param>
    /// <param name="loginProvider">The login provider associated with the token.</param>
    /// <param name="name">The name of the token to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or empty.</exception>
    public async Task RemoveTokenAsync(User user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Token name cannot be empty", nameof(name));

        var userId = GetUserIdentifier(user.Id);
        var tokenAggregate = await _plumber.Get<TokenAggregate>(userId);

        var tokenName = new TokenName(name);

        tokenAggregate.RemoveToken(tokenName, loginProvider);
        await _plumber.SaveChanges(tokenAggregate);
    }

    #endregion

    #region IUserAuthenticatorKeyStore<User> Implementation

    /// <summary>
    /// Gets the authenticator key for two-factor authentication.
    /// </summary>
    /// <param name="user">The user to get the authenticator key for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authenticator key, or null if not set.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task<string> GetAuthenticatorKeyAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        var key= _authenticationModel.GetAuthenticationDataKey(userId);

        return key;
    }

    /// <summary>
    /// Sets the authenticator key for two-factor authentication.
    /// </summary>
    /// <param name="user">The user to set the authenticator key for.</param>
    /// <param name="key">The authenticator key to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task SetAuthenticatorKeyAsync(User user, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        var identityUser = await _plumber.Get<IdentityUserAggregate>(userId);

        identityUser.ChangeAuthenticatorKey(key);
        await _plumber.SaveChanges(identityUser);
    }

    #endregion

    #region IUserTwoFactorRecoveryCodeStore<User> Implementation

    /// <summary>
    /// Gets the count of valid two-factor recovery codes for a user.
    /// </summary>
    /// <param name="user">The user to count recovery codes for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of valid recovery codes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    public async Task<int> CountCodesAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        var codes = await GetRecoveryCodesAsync(userId);

        return codes.Count;
    }

    /// <summary>
    /// Attempts to redeem a two-factor recovery code.
    /// </summary>
    /// <param name="user">The user attempting to redeem the code.</param>
    /// <param name="code">The recovery code to redeem.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the code was successfully redeemed; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is null or empty.</exception>
    public async Task<bool> RedeemCodeAsync(User user, string code, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrEmpty(code))
            throw new ArgumentException("Recovery code cannot be empty", nameof(code));

        var userId = GetUserIdentifier(user.Id);
        var tokenAggregate = await _plumber.Get<TokenAggregate>(userId);

        var codes = await GetRecoveryCodesAsync(userId);
        var normalizedCode = code.Trim();

        if (!codes.Contains(normalizedCode))
            return false;

        // Remove the used code
        codes.Remove(normalizedCode);

        // Save the updated codes
        await SetRecoveryCodesAsync(tokenAggregate, codes);

        return true;
    }

    /// <summary>
    /// Replaces the current set of two-factor recovery codes for a user.
    /// </summary>
    /// <param name="user">The user to replace recovery codes for.</param>
    /// <param name="recoveryCodes">The new collection of recovery codes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> or <paramref name="recoveryCodes"/> is null.</exception>
    public async Task ReplaceCodesAsync(User user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (recoveryCodes == null)
            throw new ArgumentNullException(nameof(recoveryCodes));

        var userId = GetUserIdentifier(user.Id);
        var tokenAggregate = await _plumber.Get<TokenAggregate>(userId);

        // Save the new codes
        await SetRecoveryCodesAsync(tokenAggregate, recoveryCodes.ToList());
    }

    // Helper methods for recovery codes
    private async Task<List<string>> GetRecoveryCodesAsync(UserIdentifier userId)
    {
        var tokenValue = _tokenModel.GetToken(userId, "RecoveryCodes", null);

        if (string.IsNullOrEmpty(tokenValue))
            return new List<string>();

        return tokenValue.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private async Task SetRecoveryCodesAsync(TokenAggregate tokenAggregate, List<string> codes)
    {
        var tokenValue = string.Join(";", codes);
        var tokenName = new TokenName("RecoveryCodes");

        tokenAggregate.SetToken(tokenName, new TokenValue(tokenValue), null);
        await _plumber.SaveChanges(tokenAggregate);
    }

    #endregion

    /// <summary>
    /// Disposes of resources used by the user store.
    /// </summary>
    public void Dispose()
    {
        // Nothing to dispose
    }
}