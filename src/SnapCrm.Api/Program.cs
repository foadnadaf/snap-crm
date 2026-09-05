using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;
using SnapCrm.Api.Jobs;
using SnapCrm.Api.Services.Agent;
using SnapCrm.Api.Services.Approvals;
using SnapCrm.Api.Services.Campaigns;
using SnapCrm.Api.Services.Consent;
using SnapCrm.Api.Services.Email;
using SnapCrm.Api.Services.Segmentation;
using SnapCrm.Api.Services.Sync;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// --- CRM's own database (isolated) ---
builder.Services.AddDbContext<SnapCrmDbContext>(o =>
    o.UseSqlServer(cfg.GetConnectionString("CrmDb")));

// --- Services ---
builder.Services.AddScoped<ICustomerSyncSource, FoodDbSyncSource>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<SegmentationService>();
builder.Services.AddScoped<ConsentService>();
builder.Services.AddScoped<CampaignService>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddScoped<PlannerService>();
builder.Services.AddScoped<CrmJobs>();
builder.Services.AddSingleton<UnsubscribeTokens>();
builder.Services.AddHttpClient<IEmailProvider, BrevoEmailProvider>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- Hangfire (background jobs) stored in the CRM DB, isolated ---
builder.Services.AddHangfire(h => h.UseSqlServerStorage(cfg.GetConnectionString("CrmDb"),
    new SqlServerStorageOptions { SchemaName = "hangfire" }));
builder.Services.AddHangfireServer();

var app = builder.Build();

// --- Create/upgrade the CRM DB on startup (only the CRM DB; never production) ---
// Phase 1 uses EnsureCreated for a zero-setup first run. Once you add EF migrations
// (`dotnet ef migrations add Init`), switch this to db.Database.Migrate().
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SnapCrmDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment() || cfg.GetValue("Hangfire:DashboardEnabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.UseHangfireDashboard("/jobs"); // protect behind reverse-proxy auth in production

// --- Health + kill-switch status (quick operational visibility) ---
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    sendingEnabled = cfg.GetValue("Crm:SendingEnabled", false),
    requireApproval = cfg.GetValue("Crm:RequireApprovalForNewCampaigns", true)
}));

// --- Recurring jobs ---
var syncEnabled = cfg.GetValue("Sync:Enabled", true);
if (syncEnabled)
{
    var mins = Math.Max(5, cfg.GetValue("Sync:IntervalMinutes", 30));
    RecurringJob.AddOrUpdate<CrmJobs>("crm-sync", j => j.SyncAsync(), $"*/{mins} * * * *");
}
// Planner proposes campaigns once a day (08:15) -> lands in approval queue.
RecurringJob.AddOrUpdate<CrmJobs>("crm-planner", j => j.PlanAsync(), "15 8 * * *");
// Dispatcher sends approved campaigns in small batches, every 10 minutes.
RecurringJob.AddOrUpdate<CrmJobs>("crm-dispatch", j => j.DispatchAsync(), "*/10 * * * *");

app.Run();
