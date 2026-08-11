using System.Net;
using FindJob.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Extensions.Http;

// Prevent inotify file descriptor limit exhaustion in Linux/Docker containers
Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");

var builder = WebApplication.CreateBuilder(args);

// Turn off file change polling/watchers to prevent inotify instance crashes on Render
builder.Host.ConfigureAppConfiguration((hostingContext, config) =>
{
    foreach (var source in config.Sources)
    {
        if (source is FileConfigurationSource fileSource)
        {
            fileSource.ReloadOnChange = false;
        }
    }
});

// Bind to dynamic PORT environment variable on Render / cloud containers
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Configure Forwarded Headers for reverse proxies like Render
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add MVC services
builder.Services.AddControllersWithViews();

// Register application domain services
builder.Services.AddScoped<IResumeParserService, ResumeParserService>();
builder.Services.AddScoped<IBdjobsService, BdjobsService>();
builder.Services.AddScoped<IJobExtractorService, JobExtractorService>();
builder.Services.AddScoped<ISkillDictionaryService, SkillDictionaryService>();
builder.Services.AddScoped<IDeterministicScoringService, DeterministicScoringService>();
builder.Services.AddScoped<IOllamaService, OllamaService>();
builder.Services.AddScoped<IJobRankingService, JobRankingService>();

// Configure HTTP clients with Polly resilience policies
builder.Services.AddHttpClient("BdjobsClient", client =>
{
    var timeoutSec = builder.Configuration.GetValue<int>("Bdjobs:TimeoutSeconds", 20);
    client.Timeout = TimeSpan.FromSeconds(timeoutSec);
})
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(400 * Math.Pow(2, retryAttempt))));

builder.Services.AddHttpClient("UniversalWebClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 5
})
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(500 * Math.Pow(2, retryAttempt))));

builder.Services.AddHttpClient("OllamaClient", client =>
{
    var timeoutSec = builder.Configuration.GetValue<int>("Ollama:TimeoutSeconds", 180);
    client.Timeout = TimeSpan.FromSeconds(timeoutSec);
});

builder.Services.AddHttpClient("OllamaHealthClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Configure upload limits
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10MB
});

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
