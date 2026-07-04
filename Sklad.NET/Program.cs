using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Sklad.Data;
using Sklad.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddControllersWithViews(options =>
    {
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        options.Filters.Add(new AuthorizeFilter(policy));
        options.ModelBinderProviders.Insert(0, new Sklad.ModelBinding.FlexibleDecimalModelBinderProvider());
    })
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(Sklad.SharedResource)));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.Cookie.Name = "Sklad.Auth";
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Always outside Development; Auth:AllowInsecureHttp opts a plain-HTTP
        // LAN install out (the cookie would otherwise never be sent).
        options.Cookie.SecurePolicy =
            builder.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Auth:AllowInsecureHttp")
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        var l = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<Sklad.SharedResource>>();
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            l["Too many sign-in attempts. Wait a minute and try again."], cancellationToken);
    };
});

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "keys")));

var supportedCultures = new[] { new CultureInfo("bg-BG"), new CultureInfo("en-GB") };
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("bg-BG");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    // No AcceptLanguage provider: new visitors always start in Bulgarian until they choose otherwise.
    options.RequestCultureProviders = new IRequestCultureProvider[]
    {
        new QueryStringRequestCultureProvider(),
        new CookieRequestCultureProvider()
    };
});

builder.Services.AddDbContext<SkladDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
        .AddInterceptors(new SqliteFunctionsInterceptor()));

builder.Services.AddScoped<IInventoryService, InventoryService>();

var app = builder.Build();

app.UseRequestLocalization();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Plain status codes (404 on a missing tire, mistyped URL) otherwise render as
// a blank page; re-execute into the styled status view.
app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Tires}/{action=Index}/{id?}")
    .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SkladDbContext>();
    db.Database.Migrate();
    // WAL survives in the database file; setting it once per start is idempotent.
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    if (app.Environment.IsDevelopment())
        DbInitializer.Seed(db);
}

app.Run();
