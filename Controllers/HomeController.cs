using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using FinGuard.Models;
using FinGuard.Data;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace FinGuard.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;

    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        // Calculate metrics from database
        var activeCustomers = await _context.Customers.CountAsync(c => c.CreditStatus == "Active");
        var totalDisbursed = await _context.Loans.SumAsync(l => (decimal?)l.PrincipalAmount) ?? 0;
        var totalRecovered = await _context.Repayments.SumAsync(r => (decimal?)r.AmountPaid) ?? 0;
        var defaulters = await _context.Loans.CountAsync(l => l.Status == "Defaulter");

        ViewBag.ActiveCustomers = activeCustomers;
        ViewBag.TotalDisbursed = totalDisbursed;
        ViewBag.TotalRecovered = totalRecovered;
        ViewBag.Defaulters = defaulters;

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}