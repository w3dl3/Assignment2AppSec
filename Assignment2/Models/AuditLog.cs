namespace Assignment2.Models
{
    using System;
    public class AuditLog
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Activity { get; set; }
        public DateTime Timestamp { get; set; }
    }
}