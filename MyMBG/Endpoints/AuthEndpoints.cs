using System.Security.Cryptography;
using Npgsql;

namespace MyMBG.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async (RegisterRequest request, NpgsqlDataSource dataSource) =>
        {
            if (string.IsNullOrWhiteSpace(request.Nama) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { message = "Nama, email, dan password wajib diisi." });
            }

            await using var connection = await dataSource.OpenConnectionAsync();
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            await using (var checkCommand = new NpgsqlCommand("SELECT 1 FROM users WHERE email = @email LIMIT 1", connection))
            {
                checkCommand.Parameters.AddWithValue("email", normalizedEmail);
                var exists = await checkCommand.ExecuteScalarAsync();
                if (exists is not null)
                {
                    return Results.Conflict(new { message = "Email sudah terdaftar." });
                }
            }

            var userId = Guid.NewGuid();
            var passwordHash = PasswordHasher.HashPassword(request.Password.Trim());

            await using (var insertCommand = new NpgsqlCommand(
                             """
                             INSERT INTO users (id, nama, email, password_hash)
                             VALUES (@id, @nama, @email, @passwordHash)
                             """, connection))
            {
                insertCommand.Parameters.AddWithValue("id", userId);
                insertCommand.Parameters.AddWithValue("nama", request.Nama.Trim());
                insertCommand.Parameters.AddWithValue("email", normalizedEmail);
                insertCommand.Parameters.AddWithValue("passwordHash", passwordHash);
                await insertCommand.ExecuteNonQueryAsync();
            }

            var token = TokenGenerator.CreateToken();
            return Results.Ok(new AuthResponse(
                token,
                new AuthUser(userId.ToString(), request.Nama.Trim(), normalizedEmail, "Staff")));
        });

        group.MapPost("/login", async (LoginRequest request, NpgsqlDataSource dataSource) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { message = "Email dan password wajib diisi." });
            }

            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT id, nama, email, password_hash, role
                FROM users
                WHERE email = @email
                LIMIT 1
                """, connection);
            command.Parameters.AddWithValue("email", request.Email.Trim().ToLowerInvariant());

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return Results.Unauthorized();
            }

            var userId = reader.GetGuid(0);
            var nama = reader.GetString(1);
            var email = reader.GetString(2);
            var passwordHash = reader.GetString(3);
            var role = reader.GetString(4);

            if (!PasswordHasher.VerifyPassword(request.Password, passwordHash))
            {
                return Results.Unauthorized();
            }

            var token = TokenGenerator.CreateToken();
            return Results.Ok(new AuthResponse(
                token,
                new AuthUser(userId.ToString(), nama, email, role)));
        });

        return group;
    }

    private static class TokenGenerator
    {
        public static string CreateToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }

    private static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;

        public static string HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        public static bool VerifyPassword(string password, string encodedHash)
        {
            var parts = encodedHash.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
            {
                return false;
            }

            byte[] salt;
            byte[] expectedHash;
            try
            {
                salt = Convert.FromBase64String(parts[1]);
                expectedHash = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
    }

    private sealed record RegisterRequest(string Nama, string Email, string Password);

    private sealed record LoginRequest(string Email, string Password);

    private sealed record AuthUser(string Id, string Nama, string Email, string Role);

    private sealed record AuthResponse(string Token, AuthUser User);
}
