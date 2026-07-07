using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.IO.Compression;
using System.Globalization;
using HemisAudit.Data;
using HemisAudit.Filters;
using HemisAudit.Models;
using HemisAudit.Services;
using Newtonsoft.Json.Serialization;

Console.OutputEncoding = System.Text.Encoding.UTF8;
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5076");
var dataProtectionPath = Path.Combine(builder.Environment.ContentRootPath, ".run", "data-protection-keys");

Directory.CreateDirectory(dataProtectionPath);

// SQLite-backed application database for the current MVC app
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("HemisAudit");

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;

    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;

    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "HemisAudit.Auth.v2";
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "HemisAudit.AntiForgery.v2";
});

builder.Services.AddScoped<IPasswordPolicyService, PasswordPolicyService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<PasswordAgeFilter>();
builder.Services.AddScoped<ISystemDatabaseService, SystemDatabaseService>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "HemisAudit.Session.v1";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});
builder.Services.AddSingleton<IPendingValidationCacheService, PendingValidationCacheService>();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json",
        "application/sql",
        "text/csv"
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<PasswordAgeFilter>();
}).AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
})
  .AddSessionStateTempDataProvider();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRule12Service, Rule12Service>();
builder.Services.AddScoped<IRule10Service, Rule10Service>();
builder.Services.AddScoped<IRule11Service, Rule11Service>();
builder.Services.AddScoped<IRule13Service, Rule13Service>();
builder.Services.AddScoped<IRule14Service, Rule14Service>();
builder.Services.AddScoped<IRule15Service, Rule15Service>();
builder.Services.AddScoped<IRule17Service, Rule17Service>();
builder.Services.AddScoped<IRule16Service, Rule16Service>();
builder.Services.AddScoped<IRule22Service, Rule22Service>();
builder.Services.AddScoped<IRule18Service, Rule18Service>();
builder.Services.AddScoped<IRule19Service, Rule19Service>();
builder.Services.AddScoped<IRule20Service, Rule20Service>();
builder.Services.AddScoped<IRule21Service, Rule21Service>();
builder.Services.AddScoped<IRule23Service, Rule23Service>();
builder.Services.AddScoped<IRule24Service, Rule24Service>();
builder.Services.AddScoped<IRule25Service, Rule25Service>();
builder.Services.AddScoped<IRule26Service, Rule26Service>();
builder.Services.AddScoped<IRule28Service, Rule28Service>();
builder.Services.AddScoped<IRule35Service, Rule35Service>();
builder.Services.AddScoped<IRule36Service, Rule36Service>();
builder.Services.AddScoped<IRule37Service, Rule37Service>();
builder.Services.AddScoped<IRule38Service, Rule38Service>();
builder.Services.AddScoped<IRule39Service, Rule39Service>();
builder.Services.AddScoped<IRule40Service, Rule40Service>();
builder.Services.AddScoped<IRule41Service, Rule41Service>();
builder.Services.AddScoped<IRule45Service, Rule45Service>();
builder.Services.AddScoped<IRule47Service, Rule47Service>();
builder.Services.AddScoped<IRule48Service, Rule48Service>();
builder.Services.AddScoped<IRule46Service, Rule46Service>();
builder.Services.AddScoped<IRule44Service, Rule44Service>();
builder.Services.AddScoped<IRule34Service, Rule34Service>();
builder.Services.AddScoped<IRule32Service, Rule32Service>();
builder.Services.AddScoped<IRule31Service, Rule31Service>();
builder.Services.AddScoped<IRule30Service, Rule30Service>();
builder.Services.AddScoped<IRule29Service, Rule29Service>();
builder.Services.AddScoped<IRule27Service, Rule27Service>();
builder.Services.AddScoped<IRule51Service, Rule51Service>();
builder.Services.AddScoped<IRule52Service, Rule52Service>();
builder.Services.AddScoped<IRule53Service, Rule53Service>();
builder.Services.AddScoped<IRule54Service, Rule54Service>();
builder.Services.AddScoped<IRule55Service, Rule55Service>();
builder.Services.AddScoped<IRule57Service, Rule57Service>();
builder.Services.AddScoped<IRule58Service, Rule58Service>();
builder.Services.AddScoped<IRule59Service, Rule59Service>();
builder.Services.AddScoped<IRule60Service, Rule60Service>();
builder.Services.AddScoped<IRule61Service, Rule61Service>();
builder.Services.AddScoped<IRule62Service, Rule62Service>();
builder.Services.AddScoped<IRule63Service, Rule63Service>();
builder.Services.AddScoped<IRule64Service, Rule64Service>();
builder.Services.AddScoped<IRule65Service, Rule65Service>();
builder.Services.AddScoped<IRule66Service, Rule66Service>();
builder.Services.AddScoped<IRule67Service, Rule67Service>();
builder.Services.AddScoped<IRule68Service, Rule68Service>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddSingleton<IValidationOperationService, ValidationOperationService>();

var app = builder.Build();

await SystemDatabaseBootstrapper.EnsureCreatedAsync(app.Configuration);

using (var scope = app.Services.CreateScope())
{
    var systemDb = scope.ServiceProvider.GetRequiredService<ISystemDatabaseService>();
    await systemDb.EnsurePerformanceObjectsAsync();
    await DbInitializer.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseResponseCompression();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.WebRootPath, "uploads", "messages")),
    RequestPath = "/uploads/messages",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    }
});
app.UseRouting();
app.UseSession();
app.Use(async (context, next) =>
{
    if (context.Request.Path.Value?.EndsWith("/GenerateRScript", StringComparison.OrdinalIgnoreCase) == true)
    {
        context.Response.StatusCode = StatusCodes.Status410Gone;
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            error = "R script generation has been removed from the system."
        });
        return;
    }

    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (HttpMethods.IsGet(context.Request.Method) &&
            !context.Request.Path.StartsWithSegments("/css") &&
            !context.Request.Path.StartsWithSegments("/js") &&
            !context.Request.Path.StartsWithSegments("/lib") &&
            !context.Request.Path.StartsWithSegments("/images") &&
            !context.Request.Path.StartsWithSegments("/uploads"))
        {
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0, must-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
        }

        return Task.CompletedTask;
    });

    await next();
});

app.MapControllerRoute(
    name: "dashboard-short",
    pattern: "Dashboard",
    defaults: new { controller = "Dashboard", action = "Index" });

for (var integrityRuleNumber = 1; integrityRuleNumber <= 10; integrityRuleNumber++)
{
    app.MapControllerRoute(
        name: $"rule{integrityRuleNumber}-short",
        pattern: $"Rule{integrityRuleNumber}/{{action=Index}}/{{id?}}",
        defaults: new { controller = "Rule10", action = "Index", ruleNumber = integrityRuleNumber });
}

app.MapControllerRoute(
    name: "rule11-short",
    pattern: "Rule11",
    defaults: new { controller = "Rule11", action = "Index" });

app.MapControllerRoute(
    name: "rule12-short",
    pattern: "Rule12",
    defaults: new { controller = "Rule12", action = "Index" });

app.MapControllerRoute(
    name: "rule13-short",
    pattern: "Rule13",
    defaults: new { controller = "Rule13", action = "Index" });

app.MapControllerRoute(
    name: "rule14-short",
    pattern: "Rule14",
    defaults: new { controller = "Rule14", action = "Index" });

app.MapControllerRoute(
    name: "rule15-short",
    pattern: "Rule15",
    defaults: new { controller = "Rule15", action = "Index" });

app.MapControllerRoute(
    name: "rule16-short",
    pattern: "Rule16",
    defaults: new { controller = "Rule16", action = "Index" });

app.MapControllerRoute(
    name: "rule17-short",
    pattern: "Rule17",
    defaults: new { controller = "Rule17", action = "Index" });

app.MapControllerRoute(
    name: "rule18-short",
    pattern: "Rule18",
    defaults: new { controller = "Rule18", action = "Index" });

app.MapControllerRoute(
    name: "rule19-short",
    pattern: "Rule19",
    defaults: new { controller = "Rule19", action = "Index" });

app.MapControllerRoute(
    name: "rule20-short",
    pattern: "Rule20",
    defaults: new { controller = "Rule20", action = "Index" });

app.MapControllerRoute(
    name: "rule21-short",
    pattern: "Rule21",
    defaults: new { controller = "Rule21", action = "Index" });

app.MapControllerRoute(
    name: "rule22-short",
    pattern: "Rule22",
    defaults: new { controller = "Rule22", action = "Index" });

app.MapControllerRoute(
    name: "rule23-short",
    pattern: "Rule23",
    defaults: new { controller = "Rule23", action = "Index" });

app.MapControllerRoute(
    name: "rule24-short",
    pattern: "Rule24",
    defaults: new { controller = "Rule24", action = "Index" });

app.MapControllerRoute(
    name: "rule25-short",
    pattern: "Rule25",
    defaults: new { controller = "Rule25", action = "Index" });

app.MapControllerRoute(
    name: "rule26-short",
    pattern: "Rule26",
    defaults: new { controller = "Rule26", action = "Index" });

app.MapControllerRoute(
    name: "rule28-short",
    pattern: "Rule28",
    defaults: new { controller = "Rule28", action = "Index" });

app.MapControllerRoute(
    name: "rule29-short",
    pattern: "Rule29",
    defaults: new { controller = "Rule29", action = "Index" });

app.MapControllerRoute(
    name: "rule27-short",
    pattern: "Rule27",
    defaults: new { controller = "Rule27", action = "Index" });

app.MapControllerRoute(
    name: "rule30-short",
    pattern: "Rule30",
    defaults: new { controller = "Rule30", action = "Index" });

app.MapControllerRoute(
    name: "rule31-short",
    pattern: "Rule31",
    defaults: new { controller = "Rule31", action = "Index" });

app.MapControllerRoute(
    name: "rule32-short",
    pattern: "Rule32",
    defaults: new { controller = "Rule32", action = "Index" });

app.MapControllerRoute(
    name: "rule35-short",
    pattern: "Rule35",
    defaults: new { controller = "Rule35", action = "Index" });

app.MapControllerRoute(
    name: "rule36-short",
    pattern: "Rule36",
    defaults: new { controller = "Rule36", action = "Index" });

app.MapControllerRoute(
    name: "rule37-short",
    pattern: "Rule37",
    defaults: new { controller = "Rule37", action = "Index" });

app.MapControllerRoute(
    name: "rule38-short",
    pattern: "Rule38",
    defaults: new { controller = "Rule38", action = "Index" });

app.MapControllerRoute(
    name: "rule39-short",
    pattern: "Rule39",
    defaults: new { controller = "Rule39", action = "Index" });

app.MapControllerRoute(
    name: "rule40-short",
    pattern: "Rule40",
    defaults: new { controller = "Rule40", action = "Index" });

app.MapControllerRoute(
    name: "rule41-short",
    pattern: "Rule41",
    defaults: new { controller = "Rule41", action = "Index" });

app.MapControllerRoute(
    name: "rule44-short",
    pattern: "Rule44",
    defaults: new { controller = "Rule44", action = "Index" });

app.MapControllerRoute(
    name: "rule45-short",
    pattern: "Rule45",
    defaults: new { controller = "Rule45", action = "Index" });

app.MapControllerRoute(
    name: "rule47-short",
    pattern: "Rule47",
    defaults: new { controller = "Rule47", action = "Index" });

app.MapControllerRoute(
    name: "rule48-short",
    pattern: "Rule48",
    defaults: new { controller = "Rule48", action = "Index" });

app.MapControllerRoute(
    name: "rule46-short",
    pattern: "Rule46",
    defaults: new { controller = "Rule46", action = "Index" });

app.MapControllerRoute(
    name: "rule34-short",
    pattern: "Rule34",
    defaults: new { controller = "Rule34", action = "Index" });

app.MapControllerRoute(
    name: "rule51-run",
    pattern: "Rule51/Run/{id:int}",
    defaults: new { controller = "Rule51", action = "Run" });

app.MapControllerRoute(
    name: "rule51-short",
    pattern: "Rule51",
    defaults: new { controller = "Rule51", action = "Index" });

app.MapControllerRoute(
    name: "rule52-run",
    pattern: "Rule52/Run/{id:int}",
    defaults: new { controller = "Rule52", action = "Run" });

app.MapControllerRoute(
    name: "rule52-short",
    pattern: "Rule52",
    defaults: new { controller = "Rule52", action = "Index" });

app.MapControllerRoute(
    name: "rule53-run",
    pattern: "Rule53/Run/{id:int}",
    defaults: new { controller = "Rule53", action = "Run" });

app.MapControllerRoute(
    name: "rule53-short",
    pattern: "Rule53",
    defaults: new { controller = "Rule53", action = "Index" });

app.MapControllerRoute(
    name: "rule54-run",
    pattern: "Rule54/Run/{id:int}",
    defaults: new { controller = "Rule54", action = "Run" });

app.MapControllerRoute(
    name: "rule54-short",
    pattern: "Rule54",
    defaults: new { controller = "Rule54", action = "Index" });

app.MapControllerRoute(
    name: "rule55-run",
    pattern: "Rule55/Run/{id:int}",
    defaults: new { controller = "Rule55", action = "Run" });

app.MapControllerRoute(
    name: "rule55-short",
    pattern: "Rule55",
    defaults: new { controller = "Rule55", action = "Index" });

app.MapControllerRoute(
    name: "rule59-run",
    pattern: "Rule59/Run/{id:int}",
    defaults: new { controller = "Rule59", action = "Run" });

app.MapControllerRoute(
    name: "rule59-short",
    pattern: "Rule59",
    defaults: new { controller = "Rule59", action = "Index" });

app.MapControllerRoute(
    name: "rule60-run",
    pattern: "Rule60/Run/{id:int}",
    defaults: new { controller = "Rule60", action = "Run" });

app.MapControllerRoute(
    name: "rule60-short",
    pattern: "Rule60",
    defaults: new { controller = "Rule60", action = "Index" });

app.MapControllerRoute(
    name: "rule61-run",
    pattern: "Rule61/Run/{id:int}",
    defaults: new { controller = "Rule61", action = "Run" });

app.MapControllerRoute(
    name: "rule61-short",
    pattern: "Rule61",
    defaults: new { controller = "Rule61", action = "Index" });

app.MapControllerRoute(
    name: "rule62-run",
    pattern: "Rule62/Run/{id:int}",
    defaults: new { controller = "Rule62", action = "Run" });

app.MapControllerRoute(
    name: "rule62-short",
    pattern: "Rule62",
    defaults: new { controller = "Rule62", action = "Index" });

app.MapControllerRoute(
    name: "rule63-run",
    pattern: "Rule63/Run/{id:int}",
    defaults: new { controller = "Rule63", action = "Run" });

app.MapControllerRoute(
    name: "rule63-short",
    pattern: "Rule63",
    defaults: new { controller = "Rule63", action = "Index" });

app.MapControllerRoute(
    name: "rule64-run",
    pattern: "Rule64/Run/{id:int}",
    defaults: new { controller = "Rule64", action = "Run" });

app.MapControllerRoute(
    name: "rule64-short",
    pattern: "Rule64",
    defaults: new { controller = "Rule64", action = "Index" });

app.MapControllerRoute(
    name: "rule65-run",
    pattern: "Rule65/Run/{id:int}",
    defaults: new { controller = "Rule65", action = "Run" });

app.MapControllerRoute(
    name: "rule65-short",
    pattern: "Rule65",
    defaults: new { controller = "Rule65", action = "Index" });

app.MapControllerRoute(
    name: "rule66-run",
    pattern: "Rule66/Run/{id:int}",
    defaults: new { controller = "Rule66", action = "Run" });

app.MapControllerRoute(
    name: "rule66-short",
    pattern: "Rule66",
    defaults: new { controller = "Rule66", action = "Index" });

app.MapControllerRoute(
    name: "rule67-run",
    pattern: "Rule67/Run/{id:int}",
    defaults: new { controller = "Rule67", action = "Run" });

app.MapControllerRoute(
    name: "rule67-short",
    pattern: "Rule67",
    defaults: new { controller = "Rule67", action = "Index" });

app.MapControllerRoute(
    name: "rule68-run",
    pattern: "Rule68/Run/{id:int}",
    defaults: new { controller = "Rule68", action = "Run" });

app.MapControllerRoute(
    name: "rule68-short",
    pattern: "Rule68",
    defaults: new { controller = "Rule68", action = "Index" });

app.MapControllerRoute(
    name: "rule58-run",
    pattern: "Rule58/Run/{id:int}",
    defaults: new { controller = "Rule58", action = "Run" });

app.MapControllerRoute(
    name: "rule58-short",
    pattern: "Rule58",
    defaults: new { controller = "Rule58", action = "Index" });

app.MapControllerRoute(
    name: "rule57-run",
    pattern: "Rule57/Run/{id:int}",
    defaults: new { controller = "Rule57", action = "Run" });

app.MapControllerRoute(
    name: "rule57-short",
    pattern: "Rule57",
    defaults: new { controller = "Rule57", action = "Index" });

app.MapControllerRoute(
    name: "messages-short",
    pattern: "Messages",
    defaults: new { controller = "Messages", action = "Index" });

app.MapControllerRoute(
    name: "directives-short",
    pattern: "Directives",
    defaults: new { controller = "Directives", action = "Index" });

app.MapControllerRoute(
    name: "saqa-short",
    pattern: "Saqa",
    defaults: new { controller = "Saqa", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
