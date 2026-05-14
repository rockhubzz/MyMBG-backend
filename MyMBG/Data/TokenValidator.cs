using Microsoft.AspNetCore.Http;
using Npgsql;

namespace MyMBG.Data;

public class TokenValidator
{
    private readonly NpgsqlDataSource _dataSource;

    public TokenValidator(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>
    /// Validates Bearer token from the Authorization header.
    /// Returns the user ID if valid, null otherwise.
    /// </summary>
    public async Task<string?> ValidateTokenAsync(HttpRequest request)
    {
        var authHeader = request.Headers.Authorization.ToString();
        
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT user_id FROM tokens WHERE token = @token AND expires_at > NOW()
                """, connection);
            command.Parameters.AddWithValue("token", token);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a token table if it doesn't exist.
    /// Call this during application startup.
    /// </summary>
    public async Task EnsureTokensTableAsync()
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS tokens (
                    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                    token TEXT NOT NULL UNIQUE,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    expires_at TIMESTAMP NOT NULL,
                    created_by VARCHAR(255)
                )
                """, connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating tokens table: {ex.Message}");
        }
    }
}
