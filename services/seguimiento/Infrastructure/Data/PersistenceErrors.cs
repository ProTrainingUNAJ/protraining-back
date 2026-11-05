using System.Data.Common;
using Application.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Data;

internal static class PersistenceErrors
{
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PersistenceConcurrencyException();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // KAN-425: el índice único (sesión, prueba, atleta, intento) es la última línea
            // de defensa contra cargas duplicadas; se traduce a 409 y no a un 503.
            throw new PersistenceConflictException();
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // The persistence boundary does not leak provider details or credentials.
            throw new PersistenceUnavailableException();
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException switch
        {
            // PostgreSQL (producción): SqlState 23505 = unique_violation.
            PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
            // SQLite (tests): el proveedor no expone un código tipado acá, pero el motor
            // siempre reporta el texto fijo "UNIQUE constraint failed: tabla.columna, ...".
            DbException provider => provider.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal),
            _ => false
        };

    private static bool IsUnavailable(Exception exception) =>
        exception is NpgsqlException or DbUpdateException or TimeoutException
        || (exception.InnerException is not null && IsUnavailable(exception.InnerException));
}
