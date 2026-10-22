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
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // The persistence boundary does not leak provider details or credentials.
            throw new PersistenceUnavailableException();
        }
    }

    private static bool IsUnavailable(Exception exception) =>
        exception is NpgsqlException or DbUpdateException or TimeoutException
        || (exception.InnerException is not null && IsUnavailable(exception.InnerException));
}
