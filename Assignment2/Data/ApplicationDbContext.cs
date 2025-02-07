using Assignment2.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Member> Members { get; set; }
    public DbSet<AuditLog> AuditLog { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Ensure Email is unique
        modelBuilder.Entity<Member>()
            .HasIndex(m => m.Email)
            .IsUnique();
    }
}
