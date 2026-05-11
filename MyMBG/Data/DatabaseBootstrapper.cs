using Npgsql;

namespace MyMBG.Data;

public static class DatabaseBootstrapper
{
    public static async Task EnsureUsersTableAsync(IServiceProvider services)
    {
        var dataSource = services.GetRequiredService<NpgsqlDataSource>();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS users (
                id UUID PRIMARY KEY,
                nama VARCHAR(100) NOT NULL,
                email VARCHAR(255) NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                role VARCHAR(20) NOT NULL DEFAULT 'Staff',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task SeedDemoUsersAsync(IServiceProvider services)
    {
        var dataSource = services.GetRequiredService<NpgsqlDataSource>();
        await using var connection = await dataSource.OpenConnectionAsync();

        var demoUsers = new[]
        {
            new { Nama = "Admin MBG", Email = "admin@dapur-mbg.id" },
            new { Nama = "Kepala Dapur MBG", Email = "kepala@dapur-mbg.id" },
            new { Nama = "Staff MBG", Email = "staff1@dapur-mbg.id" },
        };

        foreach (var demo in demoUsers)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO users (id, nama, email, password_hash)
                VALUES (@id, @nama, @email, @passwordHash)
                ON CONFLICT (email) DO UPDATE
                SET
                    nama = EXCLUDED.nama,
                    password_hash = EXCLUDED.password_hash
                """, connection);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("nama", demo.Nama);
            command.Parameters.AddWithValue("email", demo.Email);
            command.Parameters.AddWithValue("passwordHash", HashPassword("Password123!"));
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string HashPassword(string password)
    {
        const int saltSize = 16;
        const int hashSize = 32;
        const int iterations = 100_000;
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(saltSize);
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            hashSize);
        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
}
