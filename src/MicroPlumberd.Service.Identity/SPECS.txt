Specifications for Integrating ASP.NET Core Identity with MicroPlumberd and EventStore
Introduction
This document outlines the specifications for integrating ASP.NET Core Identity with MicroPlumberd and EventStore in an ASP.NET Core application. The integration uses event sourcing (ES) and Command Query Responsibility Segregation (CQRS) to manage user and role data, providing scalability, auditability, and consistency. Designed with a Blazor application in mind, the solution is adaptable to any ASP.NET Core app. It supports all ASP.NET Core Identity features—user management, roles, claims, external logins, and two-factor authentication—while adhering to domain-driven design (DDD) principles.
Architecture Overview
The integration follows a CQRS/ES architecture:
Aggregates: Handle the write side, encapsulating domain logic and emitting events to EventStore via MicroPlumberd. Aggregates respond to method invocations.

Read Models: Handle the read side, subscribing to events and maintaining query-optimized data structures that can span multiple aggregates for efficient querying.

Stores: Implement ASP.NET Core Identity store interfaces, bridging aggregates (writes) and read models (reads).
Eventual consistency is embraced, with read models updating asynchronously as events are processed. Given EventStore’s low latency, this is suitable for most Identity operations.
Value Types
Value types enhance type safety and expressiveness:
UserIdentifier: record struct UserIdentifier(Guid Id) – Uniquely identifies a user.

RoleIdentifier: record struct RoleIdentifier(Guid Id) – Uniquely identifies a role.

ClaimType: record struct ClaimType(string Value) – Represents a claim type (e.g., "Permission").

ClaimValue: record struct ClaimValue(string Value) – Represents a claim value (e.g., "Read").

ExternalLoginProvider: record struct ExternalLoginProvider(string Name) – Identifies an external login provider (e.g., "Google").

ExternalLoginKey: record struct ExternalLoginKey(string Value) – Identifies a user within an external provider.

TokenName: record struct TokenName(string Value) – Names a token (e.g., "RefreshToken").

TokenValue: record struct TokenValue(string Value) – Holds a token’s value.
These types prevent misuse of primitive strings or GUIDs and clarify domain intent.
Aggregates
Aggregates enforce business rules and maintain consistency within their boundaries. Each operates on a specific event stream and handles a distinct set of responsibilities.
1. IdentityUserAggregate
Stream: UserIdentity-{UserIdentifier}

Purpose: Manages user authentication data (passwords, lockouts, two-factor settings).

Business Rules:
Passwords must be provided as plaintext and hashed internally (e.g., using PBKDF2); direct hash setting is disallowed.

If LockoutEnabled is true and LockoutEnd is in the future, login is blocked.

Increments AccessFailedCount on failed login attempts; resets it to 0 on success.

Triggers lockout when AccessFailedCount reaches a threshold (e.g., 5).

Updating the security stamp invalidates all user sessions.

Two-factor authentication requires a valid authenticator key to enable; disabling removes the key.
Validation Rules:
Password Hash: Must be a valid hash string (format depends on the algorithm).

Security Stamp: Must be non-empty.

LockoutEnd: If set, must be a future date.

Authenticator Key: Must be a valid format (e.g., Base32 for TOTP) if two-factor is enabled.
2. UserProfileAggregate
Stream: UserProfile-{UserIdentifier}

Purpose: Manages user profile data (username, email, phone).

Business Rules:
EmailConfirmed requires a verified confirmation process (e.g., email token).

PhoneNumberConfirmed requires verification (e.g., SMS code).
Validation Rules:
Username: Non-empty, unique (checked via read model).

Email: Valid format (e.g., regex), unique (checked via read model).

Phone Number: If provided, must match a valid format (e.g., E.164).
3. AuthorizationUserAggregate
Stream: UserAuthorization-{UserIdentifier}

Purpose: Manages user authorization data (roles, claims).

Business Rules:
Supports adding/removing roles (role existence checked via read models).

Supports adding/removing claims; ensures claim uniqueness by type within the user’s set.
Validation Rules:
Roles: Non-empty strings.

Claims: ClaimType and ClaimValue must be non-empty.
4. ExternalLoginAggregate
Stream: UserExternalLogins-{UserIdentifier}

Purpose: Manages external login providers linked to a user.

Business Rules:
Ensures each ExternalLoginProvider and ExternalLoginKey combination is unique per user.
Validation Rules:
Provider: Non-empty (e.g., "Google").

Key: Non-empty (provided by the external provider).
5. TokenAggregate
Stream: UserTokens-{UserIdentifier}

Purpose: Manages authentication tokens (e.g., refresh tokens).

Business Rules:
Ensures TokenName uniqueness within the user’s token set.
Validation Rules:
Token Name: Non-empty (e.g., "RefreshToken").

Token Value: Non-empty (e.g., a secure token).
6. RoleAggregate
Stream: Role-{RoleIdentifier}

Purpose: Manages system-wide roles.

Business Rules:
Ensures role name uniqueness (checked via read model).
Validation Rules:
Name: Non-empty (e.g., "Admin").

NormalizedName: Uppercase version of Name (e.g., "ADMIN").
Read Models
Read models are event-driven projections optimized for querying, capable of spanning multiple aggregates to provide a unified view. Below are the read models and their query methods:
User-Related Read Models
UserByIdModel
Query: User GetById(UserIdentifier id)

Purpose: Retrieves a complete user object for FindByIdAsync.
UserByNameModel
Query: UserIdentifier GetIdByNormalizedUsername(string normalizedUsername)

Purpose: Maps a normalized username to a user ID for FindByNameAsync.
UserByEmailModel
Query: UserIdentifier GetIdByNormalizedEmail(string normalizedEmail)

Purpose: Maps a normalized email to a user ID for FindByEmailAsync.
UserByLoginModel
Query: UserIdentifier GetIdByLogin(string provider, string key)

Purpose: Finds a user by external login for FindByLoginAsync.
UsersInRoleModel
Query: IEnumerable<UserIdentifier> GetUsersInRole(string normalizedRoleName)

Purpose: Lists users in a role for GetUsersInRoleAsync.
UserClaimsModel
Query: IEnumerable<Claim> GetClaims(UserIdentifier id)

Purpose: Retrieves user claims for GetClaimsAsync.
AuthenticationModel
Query: AuthenticationData GetAuthenticationData(UserIdentifier id)

Purpose: Provides authentication data (e.g., password hash) for login checks.
UserQueryableModel
Query: IQueryable<User> Users

Purpose: Enables advanced user searches via IQueryableUserStore<User>.
Role-Related Read Models
RoleByIdModel
Query: Role GetById(RoleIdentifier id)

Purpose: Retrieves a role by ID.
RoleByNameModel
Query: RoleIdentifier GetIdByNormalizedName(string normalizedName)

Purpose: Maps a normalized role name to a role ID for FindByNameAsync.
RoleQueryableModel
Query: IQueryable<Role> Roles

Purpose: Enables role queries via IQueryableRoleStore<Role>.
Configuration
Dependency Injection
Read Models: Register as singletons (AddSingleton<UserByIdModel>()) to maintain state.

MicroPlumberd: Register per its documentation (e.g., AddScoped<IPlumber, Plumber>()).

Stores: Register as scoped services (AddScoped<UserStore>(), AddScoped<RoleStore>()).
ASP.NET Core Identity Setup
Configure Identity with custom stores:
csharp
builder.Services.AddIdentity<User, Role>()
    .AddUserStore<UserStore>()
    .AddRoleStore<RoleStore>()
    .AddDefaultTokenProviders();

Testing
Unit Tests:
Test aggregate methods (e.g., IdentityUserAggregate.SetPasswordHash emits correct events).

Verify read model updates with simulated event streams.
Integration Tests:
Use an in-memory EventStore to test end-to-end flows (e.g., user creation, login).
Eventual Consistency:
Simulate delays to ensure system resilience.
Design Rationale
Aggregate Split: User data is divided into IdentityUserAggregate, UserProfileAggregate, and AuthorizationUserAggregate to keep responsibilities focused and aggregates manageable, supporting future complexity (e.g., Active Directory integration).

Querying: Read models span aggregates, ensuring flexible and efficient queries without compromising write-side consistency.

Eventual Consistency: Accepted due to EventStore’s low latency; critical operations can use application-layer checks if needed.

Coordination: Sagas/process managers (e.g., CreateUserSaga) handle multi-aggregate operations like user creation. (But the inital implementation shall skip hard process-menagers)
Memo: Concurrency Control with Composite Version in Event-Sourced ASP.NET Core Identity
Date: [Insert Date]
Subject: Decision on Concurrency Control Using Composite Version for Aggregates
Audience: Development Team  
Background
In our event-sourced system, user data is managed across three distinct aggregates:
IdentityUserAggregate: Handles authentication-related data (e.g., passwords, lockouts).

UserProfileAggregate: Manages user profile information (e.g., email, username).

AuthorizationUserAggregate: Controls roles and claims.
Each aggregate maintains its own event stream and version number (tracked as a long via Metadata.SourceStreamPosition). However, ASP.NET Core Identity expects a single ConcurrencyStamp (typically a GUID) for concurrency control on the IdentityUser object. To bridge this gap, we’ve decided to implement a composite version approach.
Decision
We will adopt a composite version to unify the versioning of all three aggregates into a single ConcurrencyStamp. This composite version will:
Be represented as a string in the format "identityVersion.profileVersion.authorizationVersion" (e.g., "1.2.3").

Enable extraction of individual aggregate versions for precise concurrency checks.

Be maintained in read models, updated as events from each aggregate’s stream are processed.
This approach ensures:
Compatibility with ASP.NET Core Identity’s ConcurrencyStamp requirement.

Granular control over concurrency at the aggregate level.

Simplicity in tracking and verifying versions.
Implementation Details
1. Composite Version Structure
A CompositeVersion value type will be created with:
Properties: 
IdentityVersion (long): Version of the IdentityUserAggregate.

ProfileVersion (long): Version of the UserProfileAggregate.

AuthorizationVersion (long): Version of the AuthorizationUserAggregate.
Methods:
ToString(): Outputs the composite version as a string (e.g., "1.2.3").

Parse(string): Converts a string back into a CompositeVersion.

GetVersionFor(AggregateType): Retrieves the version for a specified aggregate.
2. Read Model Updates
Read models (e.g., UserByIdModel) will update the composite version as events are processed from each aggregate.

The IdentityUser.ConcurrencyStamp property will store the stringified composite version.
3. Concurrency Validation in UserStore
Parse the incoming ConcurrencyStamp into a CompositeVersion.

Compare it against the current composite version from the read model:
If they differ, reject the update due to a concurrency conflict.
Use GetVersionFor to extract the relevant aggregate’s version and pass it as the expectedVersion when appending events.
Special Instruction for Initial Implementation
For the first implementation of any aggregate, skip the ConcurrencyStamp check. This decision is made because:
Early implementations may lack fully populated read models or event streams.

Skipping the check prevents unnecessary concurrency failures during initial development and testing.
Once the system stabilizes and all aggregates are fully integrated, the ConcurrencyStamp check should be activated to enforce proper concurrency control.
Rationale
Event Sourcing Alignment: Utilizes existing aggregate versioning, avoiding redundant concurrency mechanisms.

ASP.NET Core Identity Support: Meets the framework’s ConcurrencyStamp requirement seamlessly.

Independent Updates: Allows each aggregate to evolve independently while preserving user data consistency.
This memo should be added to specs.md under a section such as "Concurrency Control Decisions" or "Implementation Notes" to document our approach and guide the team effectively.
