using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using FinGuard.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace FinGuard.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ProfileController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var username = User.Identity!.Name;
            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);

            if (admin == null) return NotFound();

            return View(admin);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string fullName, DateTime? dateOfBirth)
        {
            var username = User.Identity!.Name;
            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);

            if (admin != null)
            {
                bool isChanged = false;

                // Strictly format name on backend (strip digits, single space only)
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    fullName = Regex.Replace(fullName, @"[^a-zA-Z\s]", "");
                    fullName = Regex.Replace(fullName, @"\s{2,}", " ").Trim();
                }

                if (admin.FullName != fullName)
                {
                    admin.FullName = fullName;
                    isChanged = true;
                }

                // Enforce UTC formatting for PostgreSQL compatibility
                DateTime? newDobUtc = dateOfBirth.HasValue
                    ? DateTime.SpecifyKind(dateOfBirth.Value, DateTimeKind.Utc)
                    : null;

                if (admin.DateOfBirth != newDobUtc)
                {
                    admin.DateOfBirth = newDobUtc;
                    isChanged = true;
                }

                if (isChanged)
                {
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Profile details updated successfully!";
                }
                else
                {
                    TempData["Info"] = "No changes were made to your profile.";
                }
            }

            return RedirectToAction("Index");
        }
    }
}