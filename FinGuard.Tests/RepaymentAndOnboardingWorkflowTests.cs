using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FinGuard.Controllers;
using FinGuard.Data;
using FinGuard.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinGuard.Tests
{
    public class SimpleTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    public class RepaymentAndOnboardingWorkflowTests
    {
        private ApplicationDbContext GetInMemoryContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            return new ApplicationDbContext(options);
        }

        private RepaymentController CreateRepaymentController(ApplicationDbContext context, string adminUser = "TestAdmin")
        {
            var controller = new RepaymentController(context);
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, adminUser),
                new Claim(ClaimTypes.Role, "Admin")
            }, "TestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };

            var tempData = new TempDataDictionary(controller.HttpContext, new SimpleTempDataProvider());
            controller.TempData = tempData;

            return controller;
        }

        [Fact]
        public async Task AdminCollectEMI_RejectsAmountGreaterThanScheduledEMI()
        {
            using var context = GetInMemoryContext("Repay_EMI_Cap_Test1_" + Guid.NewGuid());

            var loan = new Loan
            {
                LoanId = 2001,
                CustomerId = 301,
                PrincipalAmount = 50000m,
                EMIAmount = 4500m,
                RemainingBalance = 45000m,
                TenureMonths = 12,
                Status = "Active"
            };
            context.Loans.Add(loan);
            await context.SaveChangesAsync();

            var controller = CreateRepaymentController(context);

            var repaymentSubmission = new Repayment
            {
                LoanId = 2001,
                AmountPaid = 5000m // Greater than scheduled EMI of 4500
            };

            var result = await controller.Collect(repaymentSubmission);

            // Assert ModelState error on AmountPaid
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("AmountPaid"));
            Assert.Contains("scheduled monthly installment EMI", controller.ModelState["AmountPaid"]!.Errors[0].ErrorMessage);

            // Assert balance was NOT deducted
            var dbLoan = await context.Loans.FindAsync(2001);
            Assert.Equal(45000m, dbLoan!.RemainingBalance);
        }

        [Fact]
        public async Task AdminCollectEMI_RejectsAmountGreaterThanRemainingBalance()
        {
            using var context = GetInMemoryContext("Repay_Bal_Cap_Test2_" + Guid.NewGuid());

            var loan = new Loan
            {
                LoanId = 2002,
                CustomerId = 302,
                PrincipalAmount = 10000m,
                EMIAmount = 2500m,
                RemainingBalance = 1500m, // Remaining balance is less than standard EMI
                TenureMonths = 4,
                Status = "Active"
            };
            context.Loans.Add(loan);
            await context.SaveChangesAsync();

            var controller = CreateRepaymentController(context);

            var repaymentSubmission = new Repayment
            {
                LoanId = 2002,
                AmountPaid = 2000m // Greater than remaining balance of 1500
            };

            var result = await controller.Collect(repaymentSubmission);

            // Assert ModelState error on AmountPaid
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("AmountPaid"));
            Assert.Contains("current outstanding balance", controller.ModelState["AmountPaid"]!.Errors[0].ErrorMessage);

            // Assert balance remained intact
            var dbLoan = await context.Loans.FindAsync(2002);
            Assert.Equal(1500m, dbLoan!.RemainingBalance);
        }

        [Fact]
        public async Task AdminCollectEMI_AllowsValidEMIAmountAndDeductsBalance()
        {
            using var context = GetInMemoryContext("Repay_Success_Test3_" + Guid.NewGuid());

            var customer = new Customer
            {
                CustomerId = 303,
                Name = "Aaditya Kumar",
                Email = "aaditya@example.com",
                Phone = "9876543210",
                CreditStatus = "Active",
                KycStatus = "Verified"
            };
            context.Customers.Add(customer);

            var loan = new Loan
            {
                LoanId = 2003,
                CustomerId = 303,
                PrincipalAmount = 30000m,
                EMIAmount = 3000m,
                RemainingBalance = 30000m,
                TenureMonths = 10,
                NextDueDate = DateTime.UtcNow.AddMonths(1),
                Status = "Active"
            };
            context.Loans.Add(loan);
            await context.SaveChangesAsync();

            var controller = CreateRepaymentController(context, "OpsOfficer1");

            var repaymentSubmission = new Repayment
            {
                LoanId = 2003,
                AmountPaid = 3000m
            };

            var result = await controller.Collect(repaymentSubmission);

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Receipt", redirectResult.ActionName);
            Assert.NotNull(redirectResult.RouteValues?["id"]);

            // Verify loan updated
            var dbLoan = await context.Loans.FindAsync(2003);
            Assert.NotNull(dbLoan);
            Assert.Equal(27000m, dbLoan.RemainingBalance);

            // Verify repayment recorded
            var dbRep = await context.Repayments.FirstOrDefaultAsync(r => r.LoanId == 2003);
            Assert.NotNull(dbRep);
            Assert.Equal(3000m, dbRep.AmountPaid);
            Assert.Equal("OpsOfficer1", dbRep.CollectedBy);
            Assert.False(dbRep.IsTestMode);
            Assert.Equal("Success", dbRep.PaymentStatus);
        }

        [Fact]
        public async Task RepaymentHistory_ReturnsAllRepaymentsAndMetrics()
        {
            using var context = GetInMemoryContext("Repay_History_Test4_" + Guid.NewGuid());

            var customer = new Customer
            {
                CustomerId = 304,
                Name = "Zaheer Khan",
                Email = "zaheer@example.com",
                Phone = "9811223344",
                KycStatus = "Verified"
            };
            context.Customers.Add(customer);

            var loan = new Loan
            {
                LoanId = 2004,
                CustomerId = 304,
                PrincipalAmount = 60000m,
                EMIAmount = 5000m,
                RemainingBalance = 55000m,
                TenureMonths = 12,
                Status = "Active"
            };
            context.Loans.Add(loan);

            var rep1 = new Repayment
            {
                RepaymentId = 801,
                LoanId = 2004,
                AmountPaid = 5000m,
                PaymentDate = DateTime.UtcNow.AddDays(-10),
                CollectedBy = "Digital Gateway (UPI)",
                PaymentMethod = "UPI",
                PaymentStatus = "Success"
            };
            context.Repayments.Add(rep1);
            await context.SaveChangesAsync();

            var controller = CreateRepaymentController(context);

            var result = await controller.History(null, null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<IEnumerable<Repayment>>(viewResult.Model);
            Assert.Single(model);
            Assert.Equal(5000m, (decimal)controller.ViewBag.TotalCollected);
            Assert.Equal(1, (int)controller.ViewBag.TotalCount);
        }
    }
}
