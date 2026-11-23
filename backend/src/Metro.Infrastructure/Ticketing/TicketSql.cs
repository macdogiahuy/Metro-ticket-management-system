using Npgsql;
using Metro.Application.Abstractions.Ticketing;

namespace Metro.Infrastructure.Ticketing;

internal static class TicketSql
{
    internal static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? tx, string sql, params (string Name, object Value)[] parameters)
    {
        var cmd = new NpgsqlCommand(sql, connection, tx);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
    internal static TicketException Error(PostgresException exception) => exception.SqlState switch
    {
        "28000" => new("GUARDIAN_REQUIRED", 403),
        "P0002" => new("ORDER_NOT_FOUND", 404),
        "23505" or "23P01" => new("TICKET_CONFLICT", 409),
        "22023" or "23502" or "23503" or "23514" or "P0001" => new("ISSUANCE_RULE_REJECTED", 409),
        "40001" or "40P01" or "55P03" => new("ISSUANCE_RETRY_REQUIRED", 503),
        _ => new("ISSUANCE_UNAVAILABLE", 503),
    };
}
