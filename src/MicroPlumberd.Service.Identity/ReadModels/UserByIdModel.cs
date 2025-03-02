using System.Collections.Concurrent;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Service.Identity.Aggregates;

namespace MicroPlumberd.Service.Identity.ReadModels
{
    [EventHandler]
    [OutputStream("UserByIdModel_v1")]
    public partial class UserByIdModel
    {
        private readonly ConcurrentDictionary<UserIdentifier, User> _usersById = new();

        private async Task Given(Metadata m, UserProfileCreated ev)
        {
            // Create a new user object when a profile is created
            var user = new User
            {
                Id = ev.UserId.ToString(),
                UserName = ev.UserName,
                NormalizedUserName = ev.NormalizedUserName,
                Email = ev.Email,
                NormalizedEmail = ev.NormalizedEmail,
                EmailConfirmed = false,
                PhoneNumber = ev.PhoneNumber,
                PhoneNumberConfirmed = false,
                ConcurrencyStamp = ev.ConcurrencyStamp
            };

            _usersById[ev.UserId] = user;

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, IdentityUserCreated ev)
        {
            // Update user with identity-specific data when available
            if (_usersById.TryGetValue(ev.UserId, out var user))
            {
                user.PasswordHash = ev.PasswordHash;
                user.SecurityStamp = ev.SecurityStamp;
                user.LockoutEnabled = ev.LockoutEnabled;
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, UserNameChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.UserName = ev.UserName;
                user.NormalizedUserName = ev.NormalizedUserName;
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, EmailChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.Email = ev.Email;
                user.NormalizedEmail = ev.NormalizedEmail;
                user.EmailConfirmed = false; // Reset confirmation on email change
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, EmailConfirmed ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.EmailConfirmed = true;
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, PhoneNumberChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.PhoneNumber = ev.PhoneNumber;
                user.PhoneNumberConfirmed = false; // Reset confirmation on phone change
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, PhoneNumberConfirmed ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.PhoneNumberConfirmed = true;
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, PasswordChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.PasswordHash = ev.PasswordHash;
                user.SecurityStamp = ev.SecurityStamp;
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, SecurityStampChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.SecurityStamp = ev.SecurityStamp;
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, TwoFactorChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.TwoFactorEnabled = ev.TwoFactorEnabled;
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, LockoutEnabledChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.LockoutEnabled = ev.LockoutEnabled;
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, LockoutEndChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.LockoutEnd = ev.LockoutEnd;
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, AccessFailedCountChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.AccessFailedCount = ev.AccessFailedCount;
                user.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, UserProfileDeleted ev)
        {
            var userId = new UserIdentifier(m.Id);
            _usersById.TryRemove(userId, out _);

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, IdentityUserDeleted ev)
        {
            var userId = new UserIdentifier(m.Id);
            _usersById.TryRemove(userId, out _);

            await Task.CompletedTask;
        }

        // Query methods
        public User GetById(UserIdentifier id)
        {
            if (_usersById.TryGetValue(id, out var user))
            {
                return user;
            }

            return null;
        }
    }
}