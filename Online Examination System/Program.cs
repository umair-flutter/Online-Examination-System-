using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ---------- Database & Identity ----------
builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 6;
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Deactivated users are signed out within a minute because changing status updates the security stamp.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Staff", p => p.RequireRole(Roles.Admin, Roles.Teacher))
    .AddPolicy("AdminOnly", p => p.RequireRole(Roles.Admin))
    .AddPolicy("StudentOnly", p => p.RequireRole(Roles.Student));

// ---------- Localization ----------
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    // Only the UI language changes. Number/date formatting and model binding stay in one culture,
    // so decimals ("0.25") and datetime-local values parse the same way in every language.
    o.DefaultRequestCulture = new RequestCulture(Lang.DefaultCulture);
    o.SupportedCultures = [new CultureInfo(Lang.DefaultCulture)];
    o.SupportedUICultures = Lang.Supported.Select(l => new CultureInfo(l.Code)).ToList();
    o.FallBackToParentCultures = false;
    o.RequestCultureProviders = [new CookieRequestCultureProvider()];
});

// ---------- Application services ----------
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<Lang>();
builder.Services.AddSingleton<ExcelService>();
builder.Services.AddSingleton<PdfService>();
builder.Services.AddSingleton<FileStorageService>();
builder.Services.AddTransient<IAppEmailSender, AppEmailSender>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<GradingService>();
builder.Services.AddScoped<ExamService>();
builder.Services.AddHostedService<ExpiredAttemptsWorker>();

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Dashboard");
    o.Conventions.AuthorizeFolder("/Notifications");
    o.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
    o.Conventions.AuthorizeFolder("/Manage", "Staff");
    o.Conventions.AuthorizeFolder("/Student", "StudentOnly");
    o.Conventions.AuthorizePage("/Account/Profile");
    o.Conventions.AuthorizePage("/Account/ChangePassword");
});

builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

// Language switcher: /set-language?culture=ar&returnUrl=/Dashboard
app.MapGet("/set-language", async (string culture, string? returnUrl, HttpContext ctx, UserManager<ApplicationUser> users) =>
{
    if (Lang.Supported.All(l => l.Code != culture)) culture = Lang.DefaultCulture;
    ctx.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true, SameSite = SameSiteMode.Lax });

    if (ctx.User.Identity?.IsAuthenticated == true && await users.GetUserAsync(ctx.User) is { } u)
    {
        u.PreferredLanguage = culture;
        await users.UpdateAsync(u);
    }
    return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//") ? "/" : returnUrl);
});

// Unread notification count for the navbar badge (polled every 60 s).
app.MapGet("/api/notifications/unread", async (HttpContext ctx, NotificationService n) =>
    Results.Ok(new { count = await n.UnreadCountAsync(ctx.User.UserId()) })).RequireAuthorization();

app.Run();
