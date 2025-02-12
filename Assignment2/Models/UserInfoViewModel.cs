namespace Assignment2.Models
{
    using System;

    public class UserInfoViewModel
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Gender { get; set; }
        public string EncryptedNRIC { get; set; }

        public string Email { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string WhoAmI { get; set; }

        public string ResumePath { get; set; }

        public string EncryptedData { get; set; }
        public bool TwoFactorEnabled { get; set; }
    }
}

