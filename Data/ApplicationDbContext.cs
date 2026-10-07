using Microsoft.EntityFrameworkCore;
using FinGuard.Models;

namespace FinGuard.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Customer> Customers { get; set; }
        public DbSet<Loan> Loans { get; set; }
        public DbSet<Repayment> Repayments { get; set; }
        public DbSet<AdminUser> AdminUsers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Customer>()
                .HasIndex(c => c.Email)
                .IsUnique();

            modelBuilder.Entity<Customer>()
                .HasIndex(c => c.Phone)
                .IsUnique();

            modelBuilder.Entity<AdminUser>().HasData(
                new AdminUser { 
                    AdminId = 1, 
                    Username = "kumarhimanshu3132", 
                    Email = "kumarhimanshu3132@gmail.com",
                    Password = null, 
                    Role = "SuperAdmin",
                    IsVerified = false
                },
                new AdminUser { 
                    AdminId = 2, 
                    Username = "aadityakr85390", 
                    Email = "aadityakr85390@gmail.com",
                    Password = null, 
                    Role = "Admin",
                    IsVerified = false
                },
                new AdminUser { 
                    AdminId = 3, 
                    Username = "rounakkeshri79", 
                    Email = "rounakkeshri79@gmail.com",
                    Password = null, 
                    Role = "Admin",
                    IsVerified = false
                }
            );
        }
    }
}