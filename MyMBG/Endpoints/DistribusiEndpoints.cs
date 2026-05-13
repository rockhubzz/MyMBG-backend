using Microsoft.AspNetCore.Http;
using Npgsql;

namespace MyMBG.Endpoints;

public static class DistribusiEndpoints
{
    public static RouteGroupBuilder MapDistribusiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/distribusi");

        /// <summary>
        /// Mark distribusi as complete with biaya_distribusi (distribution cost)
        /// This updates the distribusi record and triggers creation of keuangan entry
        /// </summary>
        group.MapPost("/{id}/complete", async (string id, DistribusiCompleteRequest body, NpgsqlDataSource dataSource) =>
        {
            if (!Guid.TryParse(id, out var distribusiId))
            {
                return Results.BadRequest(new { message = "ID format tidak valid." });
            }

            if (body.BiayaDistribusi < 0)
            {
                return Results.BadRequest(new { message = "Biaya distribusi tidak boleh negatif." });
            }

            await using var conn = await dataSource.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                // Check if distribusi exists
                await using (var cmdCheck = new NpgsqlCommand(
                    "SELECT id FROM public.distribusi WHERE id = @id",
                    conn,
                    tx))
                {
                    cmdCheck.Parameters.AddWithValue("id", distribusiId);
                    var result = await cmdCheck.ExecuteScalarAsync();
                    if (result is null or DBNull)
                    {
                        await tx.RollbackAsync();
                        return Results.NotFound(new { message = "Data distribusi tidak ditemukan." });
                    }
                }

                // Update distribusi with status and biaya
                await using (var cmdUpdate = new NpgsqlCommand(
                    """
                    UPDATE public.distribusi
                    SET status = CAST('Selesai' AS status_produksi),
                        biaya_distribusi = @biaya
                    WHERE id = @id
                    """,
                    conn,
                    tx))
                {
                    cmdUpdate.Parameters.AddWithValue("id", distribusiId);
                    cmdUpdate.Parameters.AddWithValue("biaya", body.BiayaDistribusi);
                    await cmdUpdate.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                return Results.Ok(new
                {
                    id = distribusiId.ToString(),
                    message = "Distribusi berhasil ditandai selesai dan biaya distribusi telah dicatat."
                });
            }
            catch (PostgresException ex)
            {
                await tx.RollbackAsync();
                return Results.Json(
                    new { message = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Results.Json(
                    new { message = ex.Message },
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        return group;
    }
}

public sealed record DistribusiCompleteRequest(decimal BiayaDistribusi);
