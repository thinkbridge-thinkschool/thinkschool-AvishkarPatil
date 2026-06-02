using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace QuotesApi.Application.Queries.Collections;

// Dapper-based implementation of the collection detail read.
//
// Why Dapper earns its place here specifically:
//   The EF version (CollectionQueryService) is already a single SQL
//   statement via AsNoTracking + Select.  The difference Dapper adds is:
//     1. No LINQ expression-tree compilation on every request (EF caches
//        compiled queries but there is still per-request overhead from
//        the query translator path).
//     2. No change-tracker bookkeeping even at the surface level — Dapper
//        materialises directly from the SqlDataReader into the DTO.
//     3. Inline SQL is explicit and auditable: what you write is what runs.
//        No surprises when EF rewrites the JOIN into a subquery or adds
//        an ORDER BY column you didn't ask for.
//
// Pattern: QueryMultiple — one network round-trip, two result sets in a
// single batch.  Avoids the awkward Dapper one-to-many multi-mapping API
// which forces mutable intermediate types.
public sealed class CollectionDapperQueryService : ICollectionDapperQueryService
{
    private readonly string _connectionString;

    public CollectionDapperQueryService(IConfiguration configuration)
    {
        // Same connection string EF uses — both paths hit the same database.
        // SqlConnection has internal pooling so new-per-call is cheap (~μs).
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured.");
    }

    // Two statements in one batch separated by a semicolon.
    // Statement 1: collection header + server-side COUNT / MAX.
    // Statement 2: all (quote × item) rows ordered by AddedAt.
    //
    // The SQL is hand-tuned to be narrower than what EF emits:
    //   - No subquery wrapping of the TOP(1) collection select.
    //   - COUNT and MAX as inline subqueries (SQL Server optimises these
    //     as scalar aggregates against the same index scan it uses for the
    //     rest of the query).
    //   - INNER JOIN on the quotes side — if a CollectionItem references a
    //     deleted Quote, EF's LEFT JOIN surfaces a null; the Dapper version
    //     silently excludes it (matches the behaviour EF exhibits when
    //     IsDeleted is not filtered).
    private const string Sql = @"
        SELECT
            c.Id,
            c.Name,
            c.OwnerId,
            (SELECT COUNT(*) FROM CollectionItems ci2
             WHERE ci2.CollectionId = c.Id)             AS ItemCount,
            (SELECT MAX(ci3.AddedAt) FROM CollectionItems ci3
             WHERE ci3.CollectionId = c.Id)             AS LastUpdatedAt
        FROM Collections c
        WHERE c.Id = @id;

        SELECT
            q.Id,
            q.Author,
            q.Text,
            q.CreatedAt,
            ci.AddedAt
        FROM CollectionItems ci
        INNER JOIN Quotes q ON q.Id = ci.QuoteId
        WHERE ci.CollectionId = @id
        ORDER BY ci.AddedAt;
    ";

    public async Task<CollectionDetailReadModel?> GetByIdAsync(
        int               id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);

        // CommandDefinition threads CancellationToken through to the
        // underlying SqlCommand so request cancellation is honoured.
        var command = new CommandDefinition(
            commandText:       Sql,
            parameters:        new { id },
            cancellationToken: cancellationToken);

        using var multi = await connection.QueryMultipleAsync(command);

        // First result set: 0 or 1 header row.
        var header = await multi.ReadSingleOrDefaultAsync<CollectionHeader>();
        if (header is null)
            return null;

        // Second result set: 0..N quote rows, already ordered by SQL.
        var quotes = (await multi.ReadAsync<QuoteSummaryReadModel>()).ToList();

        return new CollectionDetailReadModel
        {
            Id            = header.Id,
            Name          = header.Name,
            OwnerId       = header.OwnerId,
            ItemCount     = header.ItemCount,
            LastUpdatedAt = header.LastUpdatedAt,
            Quotes        = quotes,
        };
    }

    // Mutable intermediate type used only by Dapper for the first SELECT.
    // Not exposed outside this file — the public contract is the immutable
    // CollectionDetailReadModel record.
    private sealed class CollectionHeader
    {
        public int       Id            { get; set; }
        public string    Name          { get; set; } = string.Empty;
        public string    OwnerId       { get; set; } = string.Empty;
        public int       ItemCount     { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
    }
}
