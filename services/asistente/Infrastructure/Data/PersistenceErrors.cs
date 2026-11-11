using System.Data.Common;
using System.Runtime.CompilerServices;
using Application.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

[assembly: InternalsVisibleTo("Asistente.Tests")]

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
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            throw new PersistenceConflictException();
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // The persistence boundary does not leak provider details or credentials.
            throw new PersistenceUnavailableException();
        }
    }

    public static async Task ExecuteAsync(Func<Task> operation)
    {
        await ExecuteAsync(async () =>
        {
            await operation();
            return true;
        });
    }

    private static bool IsUniqueViolation(Exception exception) =>
        exception switch
        {
            // PostgreSQL (producción): SqlState 23505 = unique_violation.
            PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
            // SQLite (tests): el proveedor no expone un código tipado acá, pero el motor
            // siempre reporta el texto fijo "UNIQUE constraint failed: tabla.columna, ...".
            DbException provider when provider.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) => true,
            DbUpdateException dbUpdate when dbUpdate.InnerException is not null => IsUniqueViolation(dbUpdate.InnerException),
            _ => exception.InnerException is not null && IsUniqueViolation(exception.InnerException)
        };

    private static bool IsUnavailable(Exception exception) =>
        exception is NpgsqlException or DbUpdateException or TimeoutException
        || (exception.InnerException is not null && IsUnavailable(exception.InnerException));
}
