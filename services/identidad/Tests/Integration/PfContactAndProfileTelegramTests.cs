using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.DTOs.Auth;
using Application.DTOs.Users;
using Identidad.Tests.Infrastructure;
using Xunit;

namespace Identidad.Tests.Integration;

public sealed class PfContactAndProfileTelegramTests : IAsyncDisposable
{
    private readonly IdentidadTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public PfContactAndProfileTelegramTests()
    {
        _factory = new IdentidadTestApplicationFactory();
        _client = _factory.CreateClient();
    }

    private async Task<LoginResponseDto> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, password, "device-tg-test"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(dto);
        return dto;
    }

    [Fact]
    public async Task Given_PfAndAthlete_When_PfUpdatesTelegramAndAthleteFetchesPfContact_Then_RespectsChannelsAndTenant()
    {
        var pfA = await _factory.SeedUserAsync($"pf.a.{Guid.NewGuid():N}@protraining.com", "PF A", "PreparadorFisico", "PasswordSegura123!");
        var pfB = await _factory.SeedUserAsync($"pf.b.{Guid.NewGuid():N}@protraining.com", "PF B", "PreparadorFisico", "PasswordSegura123!");
        var athleteA = await _factory.SeedUserAsync($"ath.a.{Guid.NewGuid():N}@protraining.com", "Atleta A", "Atleta", "PasswordSegura123!", pfId: pfA.Id);

        var pfBLogin = await LoginAsync(pfB.Email!, "PasswordSegura123!");
        var pfBClient = _factory.CreateClient();
        pfBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfBLogin.AccessToken);
        await pfBClient.PutAsJsonAsync("/api/v1/users/me/profile", new UpdateProfileRequestDto("PF B", "+54 9 11 9999-8888", "pf_b_telegram"));

        var athleteLogin = await LoginAsync(athleteA.Email!, "PasswordSegura123!");
        var athleteClient = _factory.CreateClient();
        athleteClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", athleteLogin.AccessToken);

        // Sin canales en PF A -> 404 (no filtra datos de PF B)
        var emptyContact = await athleteClient.GetAsync("/api/v1/users/me/pf-contact");
        Assert.Equal(HttpStatusCode.NotFound, emptyContact.StatusCode);

        var pfALogin = await LoginAsync(pfA.Email!, "PasswordSegura123!");
        var pfAClient = _factory.CreateClient();
        pfAClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfALogin.AccessToken);

        // Username invalido -> 400
        var invalidRes = await pfAClient.PutAsJsonAsync("/api/v1/users/me/profile", new UpdateProfileRequestDto("PF A", null, "ab"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidRes.StatusCode);

        // Solo Telegram -> 200 en pf-contact
        var onlyTgRes = await pfAClient.PutAsJsonAsync("/api/v1/users/me/profile", new UpdateProfileRequestDto("PF A", null, "@profe_a"));
        Assert.Equal(HttpStatusCode.OK, onlyTgRes.StatusCode);
        var profileDto = await onlyTgRes.Content.ReadFromJsonAsync<ProfileResponseDto>();
        Assert.NotNull(profileDto);
        Assert.Equal("profe_a", profileDto.TelegramUsername);

        var tgOnlyContact = await athleteClient.GetAsync("/api/v1/users/me/pf-contact");
        Assert.Equal(HttpStatusCode.OK, tgOnlyContact.StatusCode);
        var tgOnlyDto = await tgOnlyContact.Content.ReadFromJsonAsync<PfContactDto>();
        Assert.NotNull(tgOnlyDto);
        Assert.Null(tgOnlyDto.PhoneNumber);
        Assert.Equal("profe_a", tgOnlyDto.TelegramUsername);

        // Ambos canales -> 200 con ambos
        var bothRes = await pfAClient.PutAsJsonAsync("/api/v1/users/me/profile", new UpdateProfileRequestDto("PF A", "+54 9 11 2345-6789", "@profe_a"));
        Assert.Equal(HttpStatusCode.OK, bothRes.StatusCode);

        var bothContact = await athleteClient.GetAsync("/api/v1/users/me/pf-contact");
        Assert.Equal(HttpStatusCode.OK, bothContact.StatusCode);
        var bothDto = await bothContact.Content.ReadFromJsonAsync<PfContactDto>();
        Assert.NotNull(bothDto);
        Assert.Equal("5491123456789", bothDto.PhoneNumber);
        Assert.Equal("profe_a", bothDto.TelegramUsername);

        // Limpiar Telegram con "" y limpiar telefono con "" -> vuelve a 404
        var clearRes = await pfAClient.PutAsJsonAsync("/api/v1/users/me/profile", new UpdateProfileRequestDto("PF A", "", ""));
        Assert.Equal(HttpStatusCode.OK, clearRes.StatusCode);
        var clearedProfile = await clearRes.Content.ReadFromJsonAsync<ProfileResponseDto>();
        Assert.NotNull(clearedProfile);
        Assert.Null(clearedProfile.TelegramUsername);
        Assert.Null(clearedProfile.PhoneNumber);

        var afterClear = await athleteClient.GetAsync("/api/v1/users/me/pf-contact");
        Assert.Equal(HttpStatusCode.NotFound, afterClear.StatusCode);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync();
    }
}
