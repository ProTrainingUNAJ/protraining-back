using System.Data.Common;
using Application.Exceptions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Estructura.Tests;

public class PersistenceErrorsTests
{
    private sealed class FakeDbException(string message) : DbException(message);

    [Fact]
    public async Task ExecuteAsync_WhenOperationSucceeds_ReturnsResult()
    {
        var result = await PersistenceErrors.ExecuteAsync(() => Task.FromResult(42));
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDbUpdateConcurrencyExceptionThrown_ThrowsPersistenceConcurrencyException()
    {
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() =>
            PersistenceErrors.ExecuteAsync<int>(() => throw new DbUpdateConcurrencyException("Concurrency conflict", [])));
    }

    [Fact]
    public async Task ExecuteAsync_WhenPostgresUniqueViolationInsideDbUpdateException_ThrowsPersistenceConflictException()
    {
        var postgresException = new PostgresException("duplicate key value", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);
        var dbUpdateException = new DbUpdateException("Duplicate key", postgresException);

        await Assert.ThrowsAsync<PersistenceConflictException>(() =>
            PersistenceErrors.ExecuteAsync<int>(() => throw dbUpdateException));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDirectPostgresUniqueViolationThrown_ThrowsPersistenceConflictException()
    {
        var postgresException = new PostgresException("duplicate key value", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

        await Assert.ThrowsAsync<PersistenceConflictException>(() =>
            PersistenceErrors.ExecuteAsync<int>(() => throw postgresException));
    }

    [Fact]
    public async Task ExecuteAsync_WhenSqliteUniqueConstraintFailedInsideDbUpdateException_ThrowsPersistenceConflictException()
    {
        var sqliteException = new FakeDbException("SQLite Error 19: 'UNIQUE constraint failed: athletes.dni'.");
        var dbUpdateException = new DbUpdateException("Unique constraint", sqliteException);

        await Assert.ThrowsAsync<PersistenceConflictException>(() =>
            PersistenceErrors.ExecuteAsync<int>(() => throw dbUpdateException));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDirectSqliteUniqueConstraintFailed_ThrowsPersistenceConflictException()
    {
        var sqliteException = new FakeDbException("SQLite Error 19: 'UNIQUE constraint failed: athletes.dni'.");

        await Assert.ThrowsAsync<PersistenceConflictException>(() =>
            PersistenceErrors.ExecuteAsync<int>(() => throw sqliteException));
    }

    [Fact]
    public async Task ExecuteAsync_WhenNpgsqlExceptionThrown_ThrowsPersistenceUnavailableException()
    {
        var npgsqlException = new NpgsqlException("Connection timeout");

        await Assert.ThrowsAsync<PersistenceUnavailableException>(() =>
            PersistenceErrors.ExecuteAsync<int>(() => throw npgsqlException));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTimeoutExceptionThrown_ThrowsPersistenceUnavailableException()
    {
        await Assert.ThrowsAsync<PersistenceUnavailableException>(() =>
            PersistenceErrors.ExecuteAsync<int>(() => throw new TimeoutException("Database unreachable")));
    }

    [Fact]
    public async Task ExecuteAsync_WhenGenericDbUpdateExceptionThrown_ThrowsPersistenceUnavailableException()
    {
        var dbUpdateException = new DbUpdateException("General database error", new FakeDbException("Connection terminated"));

        await Assert.ThrowsAsync<PersistenceUnavailableException>(() =>
            PersistenceErrors.ExecuteAsync<int>(() => throw dbUpdateException));
    }

    [Fact]
    public async Task ExecuteAsync_ActionOverload_WhenOperationSucceeds_CompletesSuccessfully()
    {
        var executed = false;
        await PersistenceErrors.ExecuteAsync(() =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        Assert.True(executed);
    }

    [Fact]
    public async Task ExceptionMiddleware_WhenPersistenceConflictExceptionThrown_Returns409()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<API.Middleware.ExceptionMiddleware>.Instance;
        var middleware = new API.Middleware.ExceptionMiddleware(_ => throw new PersistenceConflictException(), logger);

        await middleware.InvokeAsync(context);

        Assert.Equal(409, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExceptionMiddleware_WhenPersistenceUnavailableExceptionThrown_Returns503()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<API.Middleware.ExceptionMiddleware>.Instance;
        var middleware = new API.Middleware.ExceptionMiddleware(_ => throw new PersistenceUnavailableException(), logger);

        await middleware.InvokeAsync(context);

        Assert.Equal(503, context.Response.StatusCode);
    }
}

