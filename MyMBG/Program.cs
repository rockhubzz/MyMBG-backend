using MyMBG.Data;
using MyMBG.Endpoints;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection in configuration.");

builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
builder.Services.AddSingleton<EntityMetadataProvider>();
builder.Services.AddScoped<GenericCrudRepository>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("WebApp", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",
                "http://127.0.0.1:3000")
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

app.MapGet("/db/ping", async (NpgsqlDataSource dataSource) =>
{
    await using var connection = await dataSource.OpenConnectionAsync();
    await using var command = new NpgsqlCommand("SELECT current_database()", connection);
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

app.Run();
