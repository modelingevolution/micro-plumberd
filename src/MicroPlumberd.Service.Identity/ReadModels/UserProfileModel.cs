using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Services.Identity.Aggregates;


namespace MicroPlumberd.Services.Identity.ReadModels
{
    [EventHandler]
    [OutputStream("UserProfileModel_v1")]
    public partial class UserProfileModel
    {
        private readonly ConcurrentDictionary<UserIdentifier, ProfileData> _profilesById = new();
        private readonly ConcurrentDictionary<string, UserIdentifier> _userIdsByNormalizedName = new();
        private readonly ConcurrentDictionary<string, UserIdentifier> _userIdsByNormalizedEmail = new();

        public record ProfileData
        {
            public UserIdentifier Id { get; init; }
            public string UserName { get; init; }
            public string NormalizedUserName { get; init; }
            public string Email { get; init; }
            public string NormalizedEmail { get; init; }
            public bool EmailConfirmed { get; init; }
            public string PhoneNumber { get; init; }
            public bool PhoneNumberConfirmed { get; init; }
            
        }

        private async Task Given(Metadata m, UserProfileCreated ev)
        {
            var profile = new ProfileData
            {
                Id = ev.UserId,
                UserName = ev.UserName,
                NormalizedUserName = ev.NormalizedUserName,
                Email = ev.Email,
                NormalizedEmail = ev.NormalizedEmail,
                EmailConfirmed = false,
                PhoneNumber = ev.PhoneNumber,
                PhoneNumberConfirmed = false,
                
            };

            _profilesById[ev.UserId] = profile;
            _userIdsByNormalizedName[ev.NormalizedUserName] = ev.UserId;

            if (!string.IsNullOrEmpty(ev.NormalizedEmail))
            {
                _userIdsByNormalizedEmail[ev.NormalizedEmail] = ev.UserId;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, UserNameChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_profilesById.TryGetValue(userId, out var profile))
            {
                // Remove old normalized name mapping
                _userIdsByNormalizedName.TryRemove(profile.NormalizedUserName, out _);

                // Update profile
                var updatedProfile = profile with
                {
                    UserName = ev.UserName,
                    NormalizedUserName = ev.NormalizedUserName,
                    
                };

                _profilesById[userId] = updatedProfile;
                _userIdsByNormalizedName[ev.NormalizedUserName] = userId;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, EmailChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_profilesById.TryGetValue(userId, out var profile))
            {
                // Remove old normalized email mapping
                if (!string.IsNullOrEmpty(profile.NormalizedEmail))
                {
                    _userIdsByNormalizedEmail.TryRemove(profile.NormalizedEmail, out _);
                }

                // Update profile
                var updatedProfile = profile with
                {
                    Email = ev.Email,
                    NormalizedEmail = ev.NormalizedEmail,
                    EmailConfirmed = false,
                    
                };

                _profilesById[userId] = updatedProfile;

                // Add new normalized email mapping
                if (!string.IsNullOrEmpty(ev.NormalizedEmail))
                {
                    _userIdsByNormalizedEmail[ev.NormalizedEmail] = userId;
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, EmailConfirmed ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_profilesById.TryGetValue(userId, out var profile))
            {
                _profilesById[userId] = profile with
                {
                    EmailConfirmed = true,
                    
                };
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, PhoneNumberChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_profilesById.TryGetValue(userId, out var profile))
            {
                _profilesById[userId] = profile with
                {
                    PhoneNumber = ev.PhoneNumber,
                    PhoneNumberConfirmed = false,
                    
                };
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, PhoneNumberConfirmed ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_profilesById.TryGetValue(userId, out var profile))
            {
                _profilesById[userId] = profile with
                {
                    PhoneNumberConfirmed = true,
                    
                };
            }

            await Task.CompletedTask;
        }


        private async Task Given(Metadata m, UserProfileDeleted ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_profilesById.TryRemove(userId, out var profile))
            {
                _userIdsByNormalizedName.TryRemove(profile.NormalizedUserName, out _);

                if (!string.IsNullOrEmpty(profile.NormalizedEmail))
                {
                    _userIdsByNormalizedEmail.TryRemove(profile.NormalizedEmail, out _);
                }
            }

            await Task.CompletedTask;
        }

        // Query methods
        public ProfileData GetById(UserIdentifier id)
        {
            _profilesById.TryGetValue(id, out var profile);
            return profile;
        }

        public UserIdentifier GetIdByNormalizedUserName(string normalizedUserName)
        {
            _userIdsByNormalizedName.TryGetValue(normalizedUserName, out var id);
            return id;
        }

        public UserIdentifier GetIdByNormalizedEmail(string normalizedEmail)
        {
            _userIdsByNormalizedEmail.TryGetValue(normalizedEmail, out var id);
            return id;
        }

        public ImmutableList<ProfileData> GetAllProfiles()
        {
            return _profilesById.Values.ToImmutableList();
        }
    }
}