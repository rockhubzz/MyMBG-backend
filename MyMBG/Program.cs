using MyMBG.Data;
using MyMBG.Endpoints;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);

// Listen on all network interfaces
builder.WebHost.UseUrls(
    "http://0.0.0.0:5292"
);

builder.Services.AddOpenApi();

var connectionString =
    Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Missing ConnectionStrings:DefaultConnection in configuration."
    );

builder.Services.AddSingleton(
    _ => new NpgsqlDataSourceBuilder(connectionString).Build()
);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString)
);

builder.Services.AddSingleton<EntityMetadataProvider>();
builder.Services.AddScoped<GenericCrudRepository>();
builder.Services.AddScoped<TokenValidator>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString)
);

builder.Services.AddCors(options =>
{
    options.AddPolicy("WebApp", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });

    options.AddPolicy("AllowVercel",
        policy =>
        {
            policy
                .WithOrigins(
                    "https://my-mbg.vercel.app/"
                )
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("WebApp");

await DatabaseBootstrapper.EnsureUsersTableAsync(app.Services);
await DatabaseBootstrapper.SeedDemoUsersAsync(app.Services);

// Ensure tokens table exists
var tokenValidator = app.Services.CreateScope().ServiceProvider.GetRequiredService<TokenValidator>();
await tokenValidator.EnsureTokensTableAsync();

app.MapGet("/db/ping", async (NpgsqlDataSource dataSource) =>
{
    await using var connection = await dataSource.OpenConnectionAsync();

    await using var command =
        new NpgsqlCommand("SELECT current_database()", connection);

    var databaseName = await command.ExecuteScalarAsync();

    return Results.Ok(new
    {
        connected = true,
        database = databaseName?.ToString()
    });
});

app.MapAuthEndpoints();
app.MapCrudEndpoints();
app.MapProduksiEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); // use your actual DbContext name
    db.Database.Migrate();
}

app.Run();