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

    public IQueryable<User> Users => _usersModel.GetAllUsers().AsQueryable();

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

    public async Task<User> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var userIdentifier = GetUserIdentifier(userId);
        return _usersModel.GetById(userIdentifier);
    }

    public async Task<User> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _usersModel.GetByNormalizedUserName(normalizedUserName);
    }

    public Task<string> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.NormalizedUserName);
    }

    public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.Id);
    }

    public Task<string> GetUserNameAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.UserName);
    }

    public Task SetNormalizedUserNameAsync(User user, string normalizedName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task SetUserNameAsync(User user, string userName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.UserName = userName;
        return Task.CompletedTask;
    }

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

    public async Task<User> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _usersModel.GetByNormalizedEmail(normalizedEmail);
    }

    public Task<string> GetEmailAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.Email);
    }

    public Task<bool> GetEmailConfirmedAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.EmailConfirmed);
    }

    public Task<string> GetNormalizedEmailAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.NormalizedEmail);
    }

    public Task SetEmailAsync(User user, string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.Email = email;
        return Task.CompletedTask;
    }

    public Task SetEmailConfirmedAsync(User user, bool confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

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

    public Task<string> GetPasswordHashAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.PasswordHash);
    }

    public Task<bool> HasPasswordAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));
    }

    public Task SetPasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    #endregion

    #region IUserSecurityStampStore<User> Implementation

    public Task<string> GetSecurityStampAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.SecurityStamp);
    }

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

    public Task<int> GetAccessFailedCountAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.AccessFailedCount);
    }

    public Task<bool> GetLockoutEnabledAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.LockoutEnabled);
    }

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.LockoutEnd);
    }

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

    public Task SetLockoutEnabledAsync(User user, bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

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

    public Task<bool> GetTwoFactorEnabledAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.TwoFactorEnabled);
    }

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

    public Task<string> GetPhoneNumberAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.PhoneNumber);
    }

    public Task<bool> GetPhoneNumberConfirmedAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        return Task.FromResult(user.PhoneNumberConfirmed);
    }

    public Task SetPhoneNumberAsync(User user, string phoneNumber, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

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

    public async Task<User> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _usersModel.GetByExternalLogin(loginProvider, providerKey);
    }

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

    public async Task<IList<Claim>> GetClaimsAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        return _userAuthorizationModel.GetClaims(userId).ToList();
    }

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

    public async Task<IList<string>> GetRolesAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        return _userAuthorizationModel.GetRoleNames(userId);
        
    }

    public async Task<bool> IsInRoleAsync(User user, string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var userId = GetUserIdentifier(user.Id);
        return _userAuthorizationModel.IsInRole(userId, roleName);
    }

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

    public async Task<string> GetAuthenticatorKeyAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        var key= _authenticationModel.GetAuthenticationDataKey(userId);

        return key;
    }

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

    public async Task<int> CountCodesAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var userId = GetUserIdentifier(user.Id);
        var codes = await GetRecoveryCodesAsync(userId);

        return codes.Count;
    }

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

    public void Dispose()
    {
        // Nothing to dispose
    }
}