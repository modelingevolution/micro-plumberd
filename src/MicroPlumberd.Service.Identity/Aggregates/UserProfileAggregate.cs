using System;
using System.Threading.Tasks;
using MicroPlumberd;

namespace MicroPlumberd.Services.Identity.Aggregates
{
    [Aggregate]
    public partial class UserProfileAggregate : AggregateBase<UserIdentifier, UserProfileAggregate.UserProfileState>
    {
        public UserProfileAggregate(UserIdentifier id) : base(id) { }

        public record UserProfileState
        {
            public UserIdentifier Id { get; init; }
            public string UserName { get; init; }
            public string NormalizedUserName { get; init; }
            public string Email { get; init; }
            public string NormalizedEmail { get; init; }
            public bool EmailConfirmed { get; init; }
            public string PhoneNumber { get; init; }
            public bool PhoneNumberConfirmed { get; init; }
            
            public bool IsDeleted { get; init; }
        }

        // Event application methods
        private static UserProfileState Given(UserProfileState state, UserProfileCreated ev)
        {
            return new UserProfileState
            {
                Id = ev.UserId,
                UserName = ev.UserName,
                NormalizedUserName = ev.NormalizedUserName,
                Email = ev.Email,
                NormalizedEmail = ev.NormalizedEmail,
                EmailConfirmed = false,
                PhoneNumber = ev.PhoneNumber,
                PhoneNumberConfirmed = false,
                
                IsDeleted = false
            };
        }

        private static UserProfileState Given(UserProfileState state, UserNameChanged ev)
        {
            return state with
            {
                UserName = ev.UserName,
                NormalizedUserName = ev.NormalizedUserName,
                
            };
        }

        private static UserProfileState Given(UserProfileState state, EmailChanged ev)
        {
            return state with
            {
                Email = ev.Email,
                NormalizedEmail = ev.NormalizedEmail,
                // Changing email resets confirmation
                EmailConfirmed = false,
                
            };
        }

        private static UserProfileState Given(UserProfileState state, EmailConfirmed ev)
        {
            return state with
            {
                EmailConfirmed = true,
                
            };
        }

        private static UserProfileState Given(UserProfileState state, PhoneNumberChanged ev)
        {
            return state with
            {
                PhoneNumber = ev.PhoneNumber,
                // Changing phone resets confirmation
                PhoneNumberConfirmed = false,
                
            };
        }

        private static UserProfileState Given(UserProfileState state, PhoneNumberConfirmed ev)
        {
            return state with
            {
                PhoneNumberConfirmed = true,
                
            };
        }



        private static UserProfileState Given(UserProfileState state, UserProfileDeleted ev)
        {
            return state with { IsDeleted = true };
        }

        // Command methods
        public static UserProfileAggregate Create(
            UserIdentifier id,
            string userName,
            string normalizedUserName,
            string email,
            string normalizedEmail,
            string phoneNumber)
        {
            var aggregate = Empty(id);

            // Validation
            if (string.IsNullOrWhiteSpace(userName))
                throw new ArgumentException("Username cannot be empty", nameof(userName));

            if (string.IsNullOrWhiteSpace(normalizedUserName))
                throw new ArgumentException("Normalized username cannot be empty", nameof(normalizedUserName));

            if (!string.IsNullOrEmpty(email) && string.IsNullOrEmpty(normalizedEmail))
                throw new ArgumentException("Normalized email must be provided if email is provided", nameof(normalizedEmail));

            // Email format validation could be added here

            aggregate.AppendPendingChange(new UserProfileCreated
            {
                Id = Guid.NewGuid(),
                UserId = id,
                UserName = userName,
                NormalizedUserName = normalizedUserName,
                Email = email,
                NormalizedEmail = normalizedEmail,
                PhoneNumber = phoneNumber,
                
            });

            return aggregate;
        }

        public void ChangeUserName(string userName, string normalizedUserName )
        {
            EnsureNotDeleted();


            // Validation
            if (State.UserName != userName || State.NormalizedUserName != normalizedUserName)
            {
                // Validation
                if (string.IsNullOrWhiteSpace(userName))
                    throw new ArgumentException("Username cannot be empty", nameof(userName));

                if (string.IsNullOrWhiteSpace(normalizedUserName))
                    throw new ArgumentException("Normalized username cannot be empty", nameof(normalizedUserName));

                AppendPendingChange(new UserNameChanged
                {
                    Id = Guid.NewGuid(),
                    UserName = userName,
                    NormalizedUserName = normalizedUserName
                });
            }
        }

        public void ChangeEmail(string email, string normalizedEmail )
        {
            EnsureNotDeleted();


            // Only emit an event if the email has actually changed
            if (State.Email != email || State.NormalizedEmail != normalizedEmail)
            {
                // Validation
                if (!string.IsNullOrEmpty(email) && string.IsNullOrEmpty(normalizedEmail))
                    throw new ArgumentException("Normalized email must be provided if email is provided", nameof(normalizedEmail));

                // Email format validation could be added here

                AppendPendingChange(new EmailChanged
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    NormalizedEmail = normalizedEmail
                });
                
            }
        }

        public void ConfirmEmail()
        {
            EnsureNotDeleted();


            // Only emit an event if the email is not already confirmed
            if (!State.EmailConfirmed)
            {
                // Validation
                if (string.IsNullOrEmpty(State.Email))
                    throw new InvalidOperationException("Cannot confirm email when no email is set");

                AppendPendingChange(new EmailConfirmed
                {
                    Id = Guid.NewGuid()
                });
            }
        }

        public void ChangePhoneNumber(string phoneNumber )
        {
            EnsureNotDeleted();


            // Only emit an event if the phone number has actually changed
            if (State.PhoneNumber != phoneNumber)
            {
                // Phone number format validation could be added here

                AppendPendingChange(new PhoneNumberChanged
                {
                    Id = Guid.NewGuid(),
                    PhoneNumber = phoneNumber
                });
            }
        }

        public void ConfirmPhoneNumber()
        {
            EnsureNotDeleted();


            // Only emit an event if the phone number is not already confirmed
            if (!State.PhoneNumberConfirmed)
            {
                // Validation
                if (string.IsNullOrEmpty(State.PhoneNumber))
                    throw new InvalidOperationException("Cannot confirm phone number when no phone number is set");

                AppendPendingChange(new PhoneNumberConfirmed
                {
                    Id = Guid.NewGuid()
                });
            }
        }


        public void Delete()
        {
            if (State.IsDeleted)
                return;

            

            AppendPendingChange(new UserProfileDeleted
            {
                Id = Guid.NewGuid()
            });
        }

        // Helper methods
        private void EnsureNotDeleted()
        {
            if (State.IsDeleted)
                throw new InvalidOperationException("Cannot modify a deleted user profile");
        }


    }


}