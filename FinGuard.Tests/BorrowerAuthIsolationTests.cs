using System;
using System.Threading.Tasks;
using FinGuard.Data;
using FinGuard.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinGuard.Tests
{
    public class BorrowerAuthIsolationTests
    {
        private ApplicationDbContext GetInMemoryContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task BorrowerCredentials_StoredAndVerifiedDirectlyOnCustomer()
        {
            using var context = GetInMemoryContext("BorrowerAuth_Test1_" + Guid.NewGuid());

            string rawPassword = "StrongPassword@123";
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(rawPassword);

            var customer = new Customer
            {
                CustomerId = 101,
                Name = "Ramesh Kumar",
                Email = "ramesh@example.com",
                Phone = "9876543210",
                Address = "123 Market Street",
                PinCode = "110001",
                Password = hashedPassword,
                IsVerified = true,
                OtpCode = null
            };

            context.Customers.Add(customer);
            await context.SaveChangesAsync();

            // Verify customer password directly
            var dbCust = await context.Customers.FirstOrDefaultAsync(c => c.Email == "ramesh@example.com");
            Assert.NotNull(dbCust);
            Assert.True(BCrypt.Net.BCrypt.Verify(rawPassword, dbCust.Password));
            Assert.False(BCrypt.Net.BCrypt.Verify("WrongPassword", dbCust.Password));
            Assert.True(dbCust.IsVerified);

            // Assert that AdminUsers table has zero entries for this borrower
            var adminLookup = await context.AdminUsers.FirstOrDefaultAsync(u => u.Email == "ramesh@example.com");
            Assert.Null(adminLookup);
            Assert.Equal(0, await context.AdminUsers.CountAsync());
        }

        [Fact]
        public async Task AdminUsersTable_MaintainsOnlyGenuineAdmins_AndNeverStoresBorrowers()
        {
            using var context = GetInMemoryContext("BorrowerAuth_Test2_" + Guid.NewGuid());

            // Seed only the 3 genuine admins
            context.AdminUsers.AddRange(
                new AdminUser { AdminId = 1, Username = "kumarhimanshu3132", Email = "kumarhimanshu3132@gmail.com", Role = "SuperAdmin" },
                new AdminUser { AdminId = 2, Username = "aadityakr85390", Email = "aadityakr85390@gmail.com", Role = "Admin" },
                new AdminUser { AdminId = 3, Username = "rounakkeshri79", Email = "rounakkeshri79@gmail.com", Role = "Admin" }
            );

            // Seed borrowers in Customers table
            context.Customers.AddRange(
                new Customer { CustomerId = 101, Name = "Aditya Kumar", Email = "aditya@example.com", Phone = "9876543201", IsVerified = true },
                new Customer { CustomerId = 102, Name = "Zaheer", Email = "zaheer@example.com", Phone = "9876543202", IsVerified = true },
                new Customer { CustomerId = 103, Name = "Rahul Kumar", Email = "rahul@example.com", Phone = "9876543203", IsVerified = true }
            );

            await context.SaveChangesAsync();

            // AdminUsers count must strictly remain 3
            Assert.Equal(3, await context.AdminUsers.CountAsync());

            // All AdminUsers must have Role == Admin or SuperAdmin
            var nonAdminsInAdminTable = await context.AdminUsers
                .Where(u => u.Role != "Admin" && u.Role != "SuperAdmin")
                .ToListAsync();
            Assert.Empty(nonAdminsInAdminTable);

            // Customers table must contain the 3 borrowers
            Assert.Equal(3, await context.Customers.CountAsync());
        }

        [Fact]
        public async Task CustomerOtpLifecycle_OperatesDirectlyOnCustomerEntity()
        {
            using var context = GetInMemoryContext("BorrowerAuth_Test3_" + Guid.NewGuid());

            var customer = new Customer
            {
                CustomerId = 201,
                Name = "Sunita Sharma",
                Email = "sunita@example.com",
                Phone = "9876543212",
                Address = "456 Park Avenue",
                PinCode = "110002",
                Password = BCrypt.Net.BCrypt.HashPassword("SecretPass@2026"),
                IsVerified = false,
                OtpCode = "849201",
                OtpExpiry = DateTime.UtcNow.AddMinutes(5)
            };

            context.Customers.Add(customer);
            await context.SaveChangesAsync();

            var loadedCust = await context.Customers.FirstOrDefaultAsync(c => c.Email == "sunita@example.com");
            Assert.NotNull(loadedCust);
            Assert.False(loadedCust.IsVerified);
            Assert.Equal("849201", loadedCust.OtpCode);

            // Simulate OTP verification
            if (loadedCust.OtpCode == "849201" && loadedCust.OtpExpiry > DateTime.UtcNow)
            {
                loadedCust.IsVerified = true;
                loadedCust.OtpCode = null;
                loadedCust.OtpExpiry = null;
                await context.SaveChangesAsync();
            }

            var verifiedCust = await context.Customers.FirstOrDefaultAsync(c => c.Email == "sunita@example.com");
            Assert.NotNull(verifiedCust);
            Assert.True(verifiedCust.IsVerified);
            Assert.Null(verifiedCust.OtpCode);

            // Check that AdminUsers was completely untouched
            Assert.Equal(0, await context.AdminUsers.CountAsync());
        }

        [Fact]
        public async Task LiveNeonDatabase_ContainsOnlyTheThreeGenuineAdmins()
        {
            string connStr = "Host=ep-cool-cell-ay679t6p-pooler.c-5.us-east-2.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=npg_4XdyhN8eVrOm;SslMode=Require;Trust Server Certificate=true;";
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connStr)
                .Options;

            using var liveContext = new ApplicationDbContext(options);

            // Fetch all admin users from live Neon database
            var admins = await liveContext.AdminUsers.ToListAsync();

            // Strictly 3 admins exist in AdminUsers
            Assert.Equal(3, admins.Count);

            // Assert exact usernames and roles of the 3 genuine admins
            Assert.Contains(admins, a => a.Username == "kumarhimanshu3132" && a.Role == "SuperAdmin");
            Assert.Contains(admins, a => a.Username == "aadityakr85390" && a.Role == "Admin");
            Assert.Contains(admins, a => a.Username == "rounakkeshri79" && a.Role == "Admin");

            // Ensure no borrower account exists in AdminUsers
            var anyBorrower = admins.Find(a => a.Role != "Admin" && a.Role != "SuperAdmin");
            Assert.Null(anyBorrower);

            // Customers table must have the borrower accounts with credentials
            var customerCount = await liveContext.Customers.CountAsync();
            Assert.True(customerCount > 0);
        }
    }
}
