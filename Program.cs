using Microsoft.EntityFrameworkCore;
using FinGuard.Data;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Add Database Connection
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Email Service
builder.Services.AddTransient<FinGuard.Services.EmailService>();

// Add Banking & Simulated Disbursement Services
builder.Services.AddScoped<FinGuard.Services.IBankAccountService, FinGuard.Services.BankAccountService>();
builder.Services.AddScoped<FinGuard.Services.ISimulatedDisbursementService, FinGuard.Services.SimulatedDisbursementService>();

// Configure Razorpay Test Mode Settings & Service
builder.Services.Configure<FinGuard.Models.RazorpaySettings>(
    builder.Configuration.GetSection(FinGuard.Models.RazorpaySettings.SectionName));
builder.Services.AddScoped<FinGuard.Services.IRazorpayPaymentService, FinGuard.Services.RazorpayPaymentService>();

// Add Dual-Scheme Cookie Authentication (Admin + Borrower Portal Isolation)
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/Auth/Login";
    options.AccessDeniedPath = "/Auth/Login";
    options.Cookie.Name = "FinGuard.Admin.Session";
    options.ExpireTimeSpan = TimeSpan.FromHours(2);
    options.SlidingExpiration = true;
})
.AddCookie("BorrowerAuth", options =>
{
    options.LoginPath = "/BorrowerAuth/Login";
    options.AccessDeniedPath = "/Portal/AccessDenied";
    options.Cookie.Name = "FinGuard.Borrower.Session";
    options.ExpireTimeSpan = TimeSpan.FromHours(4);
    options.SlidingExpiration = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseRouting();

// UseAuthentication always come before UseAuthorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();