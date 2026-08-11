using FindJob.Services;
using Microsoft.AspNetCore.Http.Features;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// Add MVC services
builder.Services.AddControllersWithViews();

// Register application domain services
builder.Services.AddScoped<IResumeParserService, ResumeParserService>();
builder.Services.AddScoped<IBdjobsService, BdjobsService>();
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

builder.Services.AddHttpClient("OllamaClient", client =>
{
    var timeoutSec = builder.Configuration.GetValue<int>("Ollama:TimeoutSeconds", 180);
    client.Timeout = TimeSpan.FromSeconds(timeoutSec);
});

builder.Services.AddHttpClient("OllamaHealthClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(4);
});

// Configure upload limits
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10MB
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
