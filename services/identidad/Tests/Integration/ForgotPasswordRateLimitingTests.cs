using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Domain.Entities;
using Identidad.Tests.Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Identidad.Tests.Integration;

public sealed class ForgotPasswordRateLimitingTests : IAsyncLifetime
{
    private sealed class ControllableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private readonly ControllableTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 22, 14, 0, 0, TimeSpan.Zero));
    private IdentidadTestApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new IdentidadTestApplicationFactory(_timeProvider);
        _client = _factory.CreateClient();
        _factory.EmailSender.Clear();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task SeedUserAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (!await roleManager.RoleExistsAsync("Atleta"))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>("Atleta"));
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            FullName = "Atleta RateLimit",
            IsActive = true,
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded);
        await userManager.AddToRoleAsync(user, "Atleta");
    }

    [Fact]
    public async Task Given_SameIpAndEmail_When_Exceeding3RequestsIn15Minutes_Then_4thReturns429AndAfterWindowIsAllowedAgain()
    {
        const string email = "limite.email@protraining.app";
        await SeedUserAsync(email, "PasswordSegura1");

        // 1ra, 2da y 3ra solicitud admitidas (HTTP 202)
        for (var i = 0; i < 3; i++)
        {
            var res = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(email));
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        }
        Assert.Equal(3, _factory.EmailSender.SentEmails.Count);

        // 4ta solicitud dentro de los 15 minutos -> HTTP 429 sin enviar correo
        var fourth = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(email));
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        Assert.True(fourth.Headers.RetryAfter is not null);
        var problem = await fourth.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(429, problem.Status);
        Assert.DoesNotContain(email, problem.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, _factory.EmailSender.SentEmails.Count);

        // Avanzar reloj 15 minutos -> vuelve a permitir
        _timeProvider.Advance(TimeSpan.FromMinutes(15));
        var afterWindow = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(email));
        Assert.Equal(HttpStatusCode.Accepted, afterWindow.StatusCode);
        Assert.Equal(4, _factory.EmailSender.SentEmails.Count);
    }

    [Fact]
    public async Task Given_EmailWithCaseAndSpaces_When_RequestsExceedLimit_Then_SharesSameNormalizedBucket()
    {
        const string canonical = "usuario@correo.com";
        await SeedUserAsync(canonical, "PasswordSegura1");

        var r1 = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto("Usuario@Correo.com"));
        var r2 = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto("  usuario@correo.com  "));
        var r3 = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto("USUARIO@CORREO.COM"));
        var r4 = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(canonical));

        Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, r2.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, r3.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, r4.StatusCode);
        Assert.Equal(3, _factory.EmailSender.SentEmails.Count);
    }

    [Fact]
    public async Task Given_DifferentEmailsFromSameIp_When_IpExceeds3Requests_Then_4thReturns429AndSpoofedXForwardedForDoesNotBypass()
    {
        for (var i = 1; i <= 3; i++)
        {
            var res = await _client.PostAsJsonAsync(
                "/api/v1/auth/forgot-password",
                new ForgotPasswordRequestDto($"inexistente{i}@protraining.app"));
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        }

        // 4ta solicitud con otro correo y cabecera X-Forwarded-For falsa -> sigue bloqueado por IP real
        using var spoofedReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/forgot-password")
        {
            Content = JsonContent.Create(new ForgotPasswordRequestDto("inexistente4@protraining.app"))
        };
        spoofedReq.Headers.Add("X-Forwarded-For", "198.51.100.77");

        var blocked = await _client.SendAsync(spoofedReq);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Empty(_factory.EmailSender.SentEmails);
    }

    [Fact]
    public async Task Given_DistinctIpsForSameEmail_When_ExceedingEmailLimit_Then_4thReturns429AndLoginStillWorks()
    {
        const string email = "multi.ip@protraining.app";
        const string password = "PasswordSegura1";
        await SeedUserAsync(email, password);

        var limiter = _factory.Services.GetRequiredService<IForgotPasswordRateLimiter>();
        Assert.True(limiter.TryAcquire("10.0.0.1", email).IsAllowed);
        Assert.True(limiter.TryAcquire("10.0.0.2", email).IsAllowed);
        Assert.True(limiter.TryAcquire("10.0.0.3", email).IsAllowed);
        var fourth = limiter.TryAcquire("10.0.0.4", email);
        Assert.False(fourth.IsAllowed);

        // Login tradicional continúa funcionando normalmente
        var loginRes = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, password, "dev-1"));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
    }

    [Fact]
    public async Task Given_ConcurrentRequests_When_Sending10Simultaneously_Then_AtMost3AreAllowed()
    {
        const string email = "concurrente@protraining.app";
        await SeedUserAsync(email, "PasswordSegura1");

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(email)));
        var responses = await Task.WhenAll(tasks);

        var acceptedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Accepted);
        var limitedCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);

        Assert.Equal(3, acceptedCount);
        Assert.Equal(7, limitedCount);
        Assert.Equal(3, _factory.EmailSender.SentEmails.Count);
    }

    [Fact]
    public async Task Given_TrustedGatewayProxy_When_DifferentClientIpsRequestDifferentEmails_Then_CountersAreNotShared()
    {
        for (var i = 1; i <= 4; i++)
        {
            var email = $"proxy-user-{i}@protraining.app";
            await SeedUserAsync(email, "PasswordSegura1");

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/forgot-password")
            {
                Content = JsonContent.Create(new ForgotPasswordRequestDto(email))
            };
            req.Headers.Add("X-Remote-Ip", "172.18.0.5");
            req.Headers.Add("X-Forwarded-For", $"198.51.100.{10 + i}");

            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        }

        Assert.Equal(4, _factory.EmailSender.SentEmails.Count);
    }
}
