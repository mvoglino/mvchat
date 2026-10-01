using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AppConfigStore>();
builder.Services.AddSingleton<Db>();
builder.Services.AddScoped<Repos>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddScoped<LoginService>();

// Le chiavi che proteggono i cookie di accesso restano in App_Data:
// così un riavvio dell'hosting non fa uscire tutti gli utenti.
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
builder.Services.AddDataProtection()
    .SetApplicationName("mvchat")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Login";
        o.LogoutPath = "/Logout";
        o.AccessDeniedPath = "/Error/403";
        o.Cookie.Name = "mvchat.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
        o.Events.OnValidatePrincipal = LoginService.ValidateAsync;
    });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("SuperAdmin", p => p.RequireRole(Roles.SuperAdmin));
    o.AddPolicy("ManageGyms", p => p.RequireRole(Roles.SuperAdmin, Roles.OrgAdmin));
    o.AddPolicy("ManageUsers", p => p.RequireRole(Roles.SuperAdmin, Roles.OrgAdmin, Roles.Manager));
});

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AllowAnonymousToPage("/Login");
    o.Conventions.AllowAnonymousToPage("/Install");
    o.Conventions.AllowAnonymousToPage("/Error");
    o.Conventions.AuthorizeFolder("/Orgs", "SuperAdmin");
    o.Conventions.AuthorizeFolder("/Gyms", "ManageGyms");
    o.Conventions.AuthorizeFolder("/Users", "ManageUsers");
}).AddMvcOptions(o =>
{
    // I campi obbligatori sono solo quelli marcati [Required], con messaggi in italiano.
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    o.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(_ => "Campo obbligatorio.");
    o.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((v, f) => $"Il valore «{v}» non è valido.");
    o.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(f => "Inserisci un numero.");
});
builder.Services.AddAntiforgery(o => o.Cookie.Name = "mvchat.af");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/Error/{0}");
app.UseStaticFiles();

// Finché l'installazione guidata non è completata, ogni pagina porta all'installazione.
app.Use(async (ctx, next) =>
{
    var cfg = ctx.RequestServices.GetRequiredService<AppConfigStore>().Current;
    var path = ctx.Request.Path.Value ?? "";
    var open = path.StartsWith("/Install", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/css") || path.StartsWith("/js") || path.StartsWith("/img")
               || path.StartsWith("/health") || path.StartsWith("/Error");
    if (!cfg.Installed && !open) { ctx.Response.Redirect("/Install"); return; }
    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Chi ha una password provvisoria deve sceglierne una propria prima di usare il pannello.
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    if (ctx.User.HasClaim("pwd", "change")
        && !path.StartsWith("/Account/Password", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/Logout", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/css") && !path.StartsWith("/Error"))
    { ctx.Response.Redirect("/Account/Password"); return; }
    await next();
});

app.MapRazorPages();

app.MapGet("/health", (AppConfigStore cfg) => Results.Json(new
{
    status = "ok",
    installed = cfg.Current.Installed,
    version = typeof(Program).Assembly.GetName().Version?.ToString(3)
}));

// Indirizzo richiamato ogni pochi minuti dall'operazione pianificata di Aruba.
// Dal Passo 6 farà partire gli invii in coda; per ora registra solo che è stato chiamato.
app.MapGet("/jobs/tick", async (string? token, AppConfigStore cfg, Db db) =>
{
    var c = cfg.Current;
    if (!c.Installed || string.IsNullOrEmpty(token) || token != c.JobToken) return Results.NotFound();
    await db.ExecuteAsync("INSERT INTO AuditLog (Action, Detail) VALUES ('jobs.tick', NULL)");
    return Results.Json(new { ok = true, at = DateTime.UtcNow });
});

app.Run();
