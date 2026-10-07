using MvChat.Web.Campaigns;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using MvChat.Web.Ai;
using MvChat.Web.Catalog;
using MvChat.Web.Contacts;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;
using MvChat.Web.WhatsApp;

var builder = WebApplication.CreateBuilder(args);

// Registro tecnico in App_Data/logs: sull'hosting condiviso è l'unico modo per sapere perché il programma si è fermato.
var fileLog = new FileLogProvider(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
builder.Logging.ClearProviders(); // niente output sullo schermo del server: così il file stdout di IIS contiene solo i crash
builder.Logging.AddProvider(fileLog);
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    fileLog.Write("GRAVE ", "Program", "Errore non gestito: il programma si chiude", e.ExceptionObject as Exception);
TaskScheduler.UnobservedTaskException += (_, e) => { fileLog.Write("ERRORE", "Program", "Lavoro in sottofondo non riuscito", e.Exception); e.SetObserved(); };
builder.Services.Configure<HostOptions>(o =>
{
    // Un errore in un lavoro automatico (campagne, assistente, pulizia) non deve spegnere tutto mvchat.
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
    // Quando Aruba chiede di chiudere, si chiude in fretta: così il riavvio non resta a metà.
    o.ShutdownTimeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton<AppConfigStore>();
builder.Services.AddSingleton<Db>();
builder.Services.AddScoped<Repos>();
builder.Services.AddScoped<ContactsRepo>();
builder.Services.AddScoped<CatalogRepo>();
builder.Services.AddHttpClient<CloudApi>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<WaRepo>();
builder.Services.AddScoped<WaService>();
builder.Services.AddScoped<WebhookHandler>();
builder.Services.AddHttpClient<AiClient>(c => c.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddScoped<ConversationRepo>();
builder.Services.AddScoped<AssistantService>();
builder.Services.AddSingleton<AiQueue>();
builder.Services.AddHostedService<AiWorker>();
builder.Services.AddScoped<CampaignRepo>();
builder.Services.AddScoped<ArchiveRepo>();
builder.Services.AddScoped<MvChat.Web.Privacy.RetentionService>();
builder.Services.AddScoped<MvChat.Web.Privacy.SubjectRequests>();
builder.Services.AddHostedService<MvChat.Web.Privacy.RetentionWorker>();
builder.Services.AddScoped<MvChat.Web.Billing.BillingService>();
builder.Services.AddScoped<MvChat.Web.Reports.ReportRepo>();
builder.Services.AddScoped<MvChat.Web.Reports.AlertRepo>();
builder.Services.AddScoped<QuickReplyRepo>();
builder.Services.AddScoped<CampaignSender>();
builder.Services.AddHostedService<CampaignWorker>();
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
    o.AddPolicy("ManageLists", p => p.RequireRole(Roles.SuperAdmin, Roles.OrgAdmin, Roles.Manager));
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
    o.Conventions.AuthorizeFolder("/Lists", "ManageLists");
    o.Conventions.AuthorizeFolder("/OptOuts", "ManageLists");
    o.Conventions.AuthorizeFolder("/Sedi", "ManageLists");
    o.Conventions.AuthorizeFolder("/Offerte", "ManageLists");
    o.Conventions.AuthorizeFolder("/Modelli", "ManageLists");
    o.Conventions.AuthorizeFolder("/WhatsApp", "ManageLists");
    o.Conventions.AuthorizeFolder("/Impostazioni", "SuperAdmin");
    o.Conventions.AuthorizeFolder("/Assistente", "ManageLists");
    o.Conventions.AuthorizeFolder("/Campagne", "ManageLists");
    o.Conventions.AuthorizeFolder("/RisposteRapide", "ManageLists");
    o.Conventions.AuthorizeFolder("/Gruppo", "ManageGyms");
    o.Conventions.AuthorizeFolder("/Attivita", "ManageLists");
    o.Conventions.AuthorizeFolder("/Report", "ManageLists");
    o.Conventions.AuthorizeFolder("/Privacy", "ManageLists");
}).AddMvcOptions(o =>
{
    // I campi obbligatori sono solo quelli marcati [Required], con messaggi in italiano.
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    o.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(_ => "Campo obbligatorio.");
    o.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((v, f) => $"Il valore «{v}» non è valido.");
    o.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(f => "Inserisci un numero.");
});
// Lettere accentate scritte così come sono nell'HTML, non come codici.
builder.Services.AddWebEncoders(o => o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
builder.Services.AddAntiforgery(o => { o.Cookie.Name = "mvchat.af"; o.HeaderName = "RequestVerificationToken"; });

var app = builder.Build();

var version = typeof(Program).Assembly.GetName().Version?.ToString(3);
app.Lifetime.ApplicationStarted.Register(() => app.Logger.LogInformation("mvchat avviato (versione {Version}, processo {Pid})", version, Environment.ProcessId));
app.Lifetime.ApplicationStopping.Register(() =>
{
    using var p = System.Diagnostics.Process.GetCurrentProcess();
    app.Logger.LogWarning("mvchat in chiusura: richiesta dal server (riavvio, aggiornamento o limite dell'hosting). Memoria in uso: {Mb} MB, acceso da {Hours:0.0} ore",
        p.PrivateMemorySize64 / 1048576, (DateTime.Now - p.StartTime).TotalHours);
});
app.Lifetime.ApplicationStopped.Register(() => app.Logger.LogInformation("mvchat chiuso"));

// Installazioni fatte prima del Passo 4: crea la parola d'ordine del webhook se manca.
{
    var store = app.Services.GetRequiredService<AppConfigStore>();
    if (store.Current.Installed && string.IsNullOrEmpty(store.Current.Meta.WebhookVerifyToken))
    {
        var c = store.Current;
        c.Meta.WebhookVerifyToken = AppConfigStore.NewToken();
        store.Save(c);
    }
    // Aggiornamenti del database: chi carica una nuova versione su Aruba non deve fare nulla,
    // gli script nuovi (Data/Schema) si applicano da soli al primo avvio.
    if (store.Current.Installed)
    {
        try
        {
            var done = await Migrator.ApplyAsync(store.Current.ConnectionString);
            if (done.Count > 0) app.Logger.LogInformation("Database aggiornato: {Scripts}", string.Join(", ", done));
        }
        catch (Exception ex)
        {
            Migrator.LastError = ex.Message;
            app.Logger.LogError(ex, "Aggiornamento del database non riuscito");
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection(); // su Aruba con il certificato attivo: tutto passa in HTTPS
}

// Protezioni del browser: niente pagine di mvchat dentro siti altrui, niente script o moduli verso altri indirizzi.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "same-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; "
        + "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
    await next();
});
// Le pagine d'errore servono alle persone: webhook e indirizzi tecnici rispondono col solo codice.
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/webhooks") && !ctx.Request.Path.StartsWithSegments("/jobs"),
    b => b.UseStatusCodePagesWithReExecute("/Error/{0}"));
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

// Controllo dello stato per un servizio di monitoraggio: risponde 503 se il database non risponde o non è aggiornato.
app.MapGet("/health", async (AppConfigStore cfg, Db db) =>
{
    var version = typeof(Program).Assembly.GetName().Version?.ToString(3);
    if (!cfg.Current.Installed) return Results.Json(new { status = "ok", installed = false, version });
    try
    {
        var (applied, expected) = await Migrator.StatusAsync(db);
        var ok = Migrator.LastError is null && applied >= expected;
        return Results.Json(new { status = ok ? "ok" : "schema", installed = true, version, database = "ok", schema = $"{applied}/{expected}", error = Migrator.LastError },
            statusCode: ok ? 200 : 503);
    }
    catch
    {
        return Results.Json(new { status = "database", installed = true, version, database = "non raggiungibile" }, statusCode: 503);
    }
});

// Indirizzo richiamato ogni pochi minuti dall'operazione pianificata di Aruba.
// Dal Passo 6 farà partire gli invii in coda; per ora registra solo che è stato chiamato.
app.MapGet("/jobs/tick", async (string? token, AppConfigStore cfg, Db db, ConversationRepo convs, AiQueue queue, CampaignSender sender,
    MvChat.Web.Privacy.RetentionService retention, WebhookHandler hook, WaRepo waRepo, WaService wa, ILogger<Program> log) =>
{
    var c = cfg.Current;
    if (!c.Installed || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(c.JobToken)
        || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(token), System.Text.Encoding.UTF8.GetBytes(c.JobToken)))
        return Results.NotFound();
    var started = DateTime.UtcNow;
    var notes = new List<string>();
    // Ogni lavoro per conto suo: se uno dà errore, gli altri partono lo stesso.
    async Task Step(string name, Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) { log.LogError(ex, "Operazione pianificata: {Step} non riuscito", name); notes.Add($"{name}: errore ({ex.Message})"); }
    }
    // Risposte AI rimaste in sospeso (per esempio dopo un riavvio dell'hosting).
    var pending = new List<long>();
    await Step("risposte AI", async () => { pending = await convs.PendingAsync(60); foreach (var id in pending) queue.Enqueue(id); });
    // Avvisi di Meta non elaborati per un problema momentaneo.
    var retried = 0;
    await Step("avvisi Meta", async () => retried = await hook.RetryFailedAsync());
    // Campagne: un giro di invio (l'operazione pianificata di Aruba ha un tempo massimo, quindi si resta sotto i 30 secondi in tutto).
    var run = new MvChat.Web.Campaigns.RunSummary(0, 0, 0, new());
    await Step("campagne", async () => run = await sender.RunAsync(TimeSpan.FromSeconds(20)));
    // Qualità e limite di invio dei numeri Meta: aggiornati ogni 6 ore (al massimo 3 numeri per giro).
    await Step("numeri WhatsApp", async () => { foreach (var n in (await waRepo.MetaNumbersAsync(6)).Take(3)) await wa.CheckNumberAsync(n); });
    // Pulizia giornaliera dei dati vecchi (salta se già fatta nelle ultime 20 ore; se il tempo non basta, continua al giro dopo).
    var left = TimeSpan.FromSeconds(28) - (DateTime.UtcNow - started);
    if (left > TimeSpan.FromSeconds(3)) await Step("pulizia dati", () => retention.RunAsync(budget: left));
    var detail = string.Join(" · ", new[] {
        pending.Count > 0 ? $"riprese {pending.Count} risposte" : null,
        retried > 0 ? $"rielaborati {retried} avvisi Meta" : null,
        run.Sent + run.Skipped + run.Errors > 0 ? $"campagne: inviati {run.Sent}, saltati {run.Skipped}, errori {run.Errors}" : null }
        .Concat(notes).Where(x => x is not null));
    // Il registro tiene un giro "vuoto" ogni 15 minuti (serve al pannello per sapere che l'operazione pianificata funziona), tutti quelli con del lavoro.
    var lastLogged = await db.ScalarAsync<DateTime?>("SELECT MAX(At) FROM AuditLog WHERE Action='jobs.tick'");
    if (detail != "" || lastLogged is null || lastLogged < DateTime.UtcNow.AddMinutes(-15))
        await db.ExecuteAsync("INSERT INTO AuditLog (Action, Detail) VALUES ('jobs.tick', @d)", new { d = detail == "" ? null : (detail.Length > 990 ? detail[..990] : detail) });
    return Results.Json(new { ok = true, at = DateTime.UtcNow, resumed = pending.Count, retried, sent = run.Sent, skipped = run.Skipped, errors = run.Errors, notes = run.Notes.Concat(notes) });
});

// Logo caricato da un gruppo: è un'immagine pubblica, servita dal database con il tipo verificato al caricamento.
app.MapGet("/logo/{id:int}", async (int id, HttpContext ctx, AppConfigStore cfg, Repos repos) =>
{
    if (!cfg.Current.Installed) return Results.NotFound();
    var logo = await repos.LogoAsync(id);
    if (logo is null) return Results.NotFound();
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers.CacheControl = "public, max-age=604800";
    return Results.File(logo.Value.Data, logo.Value.Type);
});

app.MapGet("/logo/a/{id:int}", async (int id, HttpContext ctx, AppConfigStore cfg, Repos repos) =>
{
    if (!cfg.Current.Installed) return Results.NotFound();
    var logo = await repos.ActivityLogoAsync(id);
    if (logo is null) return Results.NotFound();
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers.CacheControl = "public, max-age=604800";
    return Results.File(logo.Value.Data, logo.Value.Type);
});

// Webhook WhatsApp: Meta chiama questo indirizzo per consegnare messaggi e aggiornamenti di stato.
app.MapGet("/webhooks/whatsapp", (HttpRequest req, WebhookHandler h) =>
{
    var challenge = h.Verify(req.Query["hub.mode"], req.Query["hub.verify_token"], req.Query["hub.challenge"]);
    return challenge is null ? Results.StatusCode(403) : Results.Text(challenge);
});
app.MapPost("/webhooks/whatsapp", async (HttpRequest req, WebhookHandler h) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var body = ms.ToArray();
    if (!h.SignatureOk(body, req.Headers["X-Hub-Signature-256"])) return Results.StatusCode(401);
    await h.ProcessAsync(System.Text.Encoding.UTF8.GetString(body));
    return Results.Ok();
});

app.Run();
