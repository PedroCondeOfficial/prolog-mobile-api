using Microsoft.Extensions.Options;
using prolog_mobile_api.Clients;
using prolog_mobile_api.Config;
using prolog_mobile_api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOptions<PrologApiOptions>()
    .Bind(builder.Configuration.GetSection(PrologApiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<IPrologApiClient, PrologApiClient>((sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<PrologApiOptions>>().Value;
    // Trailing slash so relative paths append to the base URL instead of replacing its last segment.
    http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    http.DefaultRequestHeaders.Add(PrologApiClient.ApiKeyHeaderName, options.ApiKey);
})
// Standard pipeline: total timeout -> retry (exponential backoff + jitter, honours Retry-After)
// -> circuit breaker -> per-attempt timeout. Retries 429, 408, 5xx and transport errors; not other 4xx.
// Tunable via "PrologApi:Resilience" in appsettings.json.
.AddStandardResilienceHandler(builder.Configuration.GetSection($"{PrologApiOptions.SectionName}:Resilience"));

const string UiCorsPolicy = "Ui";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(UiCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddControllers();
builder.Services.AddSingleton<AggregatorService>();
builder.Services.AddScoped<PrologService>();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(UiCorsPolicy);

app.MapControllers();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
