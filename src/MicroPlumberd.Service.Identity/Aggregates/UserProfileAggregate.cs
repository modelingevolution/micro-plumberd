using System;
using System.Threading.Tasks;
using MicroPlumberd;

namespace MicroPlumberd.Services.Identity.Aggregates
{
    /// <summary>
    /// Aggregate root managing user profile information including username, email, and phone number.
    /// </summary>
    [Aggregate]
    [OutputStream("UserProfile")]
    public partial class UserProfileAggregate : AggregateBase<UserIdentifier, UserProfileAggregate.UserProfileState>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UserProfileAggregate"/> class.
        /// </summary>
        /// <param name="id">The unique identifier for the user.</param>
        public UserProfileAggregate(UserIdentifier id) : base(id) { }

        /// <summary>
        /// Represents the state of a user profile including contact information and confirmation status.
        /// </summary>
        public readonly record struct UserProfileState
        {
            /// <summary>
            /// Gets the username.
            /// </summary>
            public string UserName { get; init; }

            /// <summary>
            /// Gets the normalized username for case-insensitive comparisons.
            /// </summary>
            public string NormalizedUserName { get; init; }

            /// <summary>
            /// Gets the email address.
            /// </summary>
            public string Email { get; init; }

            /// <summary>
            /// Gets the normalized email address for case-insensitive comparisons.
            /// </summary>
            public string NormalizedEmail { get; init; }

            /// <summary>
            /// Gets a value indicating whether the email address has been confirmed.
            /// </summary>
            public bool EmailConfirmed { get; init; }

            /// <summary>
            /// Gets the phone number.
            /// </summary>
            public string PhoneNumber { get; init; }

            /// <summary>
            /// Gets a value indicating whether the phone number has been confirmed.
            /// </summary>
            public bool PhoneNumberConfirmed { get; init; }

            /// <summary>
            /// Gets a value indicating whether this user profile has been deleted.
            /// </summary>
            public bool IsDeleted { get; init; }
        }

        // Event application methods
        private static UserProfileState Given(UserProfileState state, UserProfileCreated ev)
        {
            return new UserProfileState
            {
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

        /// <summary>
        /// Creates a new user profile aggregate with the specified information.
        /// </summary>
        /// <param name="id">The unique identifier for the user.</param>
        /// <param name="userName">The username. Cannot be null or whitespace.</param>
        /// <param name="normalizedUserName">The normalized username. Cannot be null or whitespace.</param>
        /// <param name="email">The email address.</param>
        /// <param name="normalizedEmail">The normalized email address.</param>
        /// <param name="phoneNumber">The phone number.</param>
        /// <returns>A new <see cref="UserProfileAggregate"/> instance.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="userName"/> or <paramref name="normalizedUserName"/> is null or whitespace, or when <paramref name="email"/> is provided without <paramref name="normalizedEmail"/>.</exception>
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
                UserName = userName,
                NormalizedUserName = normalizedUserName,
                Email = email,
                NormalizedEmail = normalizedEmail,
                PhoneNumber = phoneNumber,
            });

            return aggregate;
        }

        /// <summary>
        /// Changes the username for the user profile.
        /// </summary>
        /// <param name="userName">The new username. Cannot be null or whitespace.</param>
        /// <param name="normalizedUserName">The new normalized username. Cannot be null or whitespace.</param>
        /// <exception cref="InvalidOperationException">Thrown when attempting to modify a deleted user profile.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="userName"/> or <paramref name="normalizedUserName"/> is null or whitespace.</exception>
        public void ChangeUserName(string userName, string normalizedUserName )
        {
            EnsureNotDeleted();

            // Validation
            if (State.UserName == userName && State.NormalizedUserName == normalizedUserName) return;
            // Validation
            if (string.IsNullOrWhiteSpace(userName))
                throw new ArgumentException("Username cannot be empty", nameof(userName));

            if (string.IsNullOrWhiteSpace(normalizedUserName))
                throw new ArgumentException("Normalized username cannot be empty", nameof(normalizedUserName));

            AppendPendingChange(new UserNameChanged
            {
                UserName = userName,
                NormalizedUserName = normalizedUserName
            });
        }

        /// <summary>
        /// Changes the email address for the user profile. This resets email confirmation status.
        /// </summary>
        /// <param name="email">The new email address.</param>
        /// <param name="normalizedEmail">The new normalized email address.</param>
        /// <exception cref="InvalidOperationException">Thrown when attempting to modify a deleted user profile.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="email"/> is provided without <paramref name="normalizedEmail"/>.</exception>
        public void ChangeEmail(string email, string normalizedEmail )
        {
            EnsureNotDeleted();


            // Only emit an event if the email has actually changed
            if (State.Email == email && State.NormalizedEmail == normalizedEmail) return;
            // Validation
            if (!string.IsNullOrEmpty(email) && string.IsNullOrEmpty(normalizedEmail))
                throw new ArgumentException("Normalized email must be provided if email is provided", nameof(normalizedEmail));

            // Email format validation could be added here

            AppendPendingChange(new EmailChanged
            {
                Email = email,
                NormalizedEmail = normalizedEmail
            });
        }

        /// <summary>
        /// Confirms the user's email address.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when attempting to confirm an email when no email is set or when attempting to modify a deleted user profile.</exception>
        public void ConfirmEmail()
        {
            EnsureNotDeleted();


            // Only emit an event if the email is not already confirmed
            if (State.EmailConfirmed) return;
            // Validation
            if (string.IsNullOrEmpty(State.Email))
                throw new InvalidOperationException("Cannot confirm email when no email is set");

            AppendPendingChange(new EmailConfirmed());
        }

        /// <summary>
        /// Changes the phone number for the user profile. This resets phone number confirmation status.
        /// </summary>
        /// <param name="phoneNumber">The new phone number.</param>
        /// <exception cref="InvalidOperationException">Thrown when attempting to modify a deleted user profile.</exception>
        public void ChangePhoneNumber(string phoneNumber )
        {
            EnsureNotDeleted();


            // Only emit an event if the phone number has actually changed
            if (State.PhoneNumber == phoneNumber) return;
            // Phone number format validation could be added here

            AppendPendingChange(new PhoneNumberChanged
            {
                PhoneNumber = phoneNumber
            });
        }

        /// <summary>
        /// Confirms the user's phone number.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when attempting to confirm a phone number when no phone number is set or when attempting to modify a deleted user profile.</exception>
        public void ConfirmPhoneNumber()
        {
            EnsureNotDeleted();


            // Only emit an event if the phone number is not already confirmed
            if (State.PhoneNumberConfirmed) return;
            // Validation
            if (string.IsNullOrEmpty(State.PhoneNumber))
                throw new InvalidOperationException("Cannot confirm phone number when no phone number is set");

            AppendPendingChange(new PhoneNumberConfirmed());
        }


        /// <summary>
        /// Marks the user profile as deleted.
        /// </summary>
        public void Delete()
        {
            if (State.IsDeleted)
                return;

            AppendPendingChange(new UserProfileDeleted());
        }

        // Helper methods
        private void EnsureNotDeleted()
        {
            if (State.IsDeleted)
                throw new InvalidOperationException("Cannot modify a deleted user profile");
        }


    }


}