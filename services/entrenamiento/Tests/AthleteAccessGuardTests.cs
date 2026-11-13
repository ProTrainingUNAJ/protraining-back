using Application.DTOs.Memberships;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.Services;
using Moq;
using Xunit;

namespace Tests;

public class AthleteAccessGuardTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IStructureClient> _structureClient = new();

    private AthleteAccessGuard CreateGuard(string role)
    {
        _currentUser.Setup(u => u.IsInRole(It.IsAny<string>()))
            .Returns<string>(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

        return new AthleteAccessGuard(_currentUser.Object, _structureClient.Object);
    }

    [Fact]
    public async Task EnsureCanReadAsync_RolPf_AtletaPropio_NoLanzaExcepcion()
    {
        var athleteId = Guid.NewGuid();
        _structureClient
            .Setup(s => s.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athleteId, "Franco"));

        var guard = CreateGuard("PreparadorFisico");

        await guard.EnsureCanReadAsync(athleteId);
    }

    [Fact]
    public async Task EnsureCanReadAsync_RolPf_AtletaInexistenteOAjeno_LanzaAthleteNotFoundException()
    {
        var athleteId = Guid.NewGuid();
        _structureClient
            .Setup(s => s.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StructureAthleteDto?)null);

        var guard = CreateGuard("PreparadorFisico");

        await Assert.ThrowsAsync<AthleteNotFoundException>(() =>
            guard.EnsureCanReadAsync(athleteId));
    }

    [Fact]
    public async Task EnsureCanReadAsync_RolAtleta_SinPerfil_LanzaNotFoundException()
    {
        var athleteId = Guid.NewGuid();
        _structureClient
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteMeDto?)null);

        var guard = CreateGuard("Atleta");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            guard.EnsureCanReadAsync(athleteId));
    }

    [Fact]
    public async Task EnsureCanReadAsync_RolAtleta_MismoAtleta_NoLanzaExcepcion()
    {
        var athleteId = Guid.NewGuid();
        _structureClient
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto { AthleteId = athleteId });

        var guard = CreateGuard("Atleta");

        await guard.EnsureCanReadAsync(athleteId);
    }

    [Fact]
    public async Task EnsureCanReadAsync_RolAtleta_OtroAtleta_LanzaAthleteNotFoundException()
    {
        var athleteId = Guid.NewGuid();
        _structureClient
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto { AthleteId = Guid.NewGuid() });

        var guard = CreateGuard("Atleta");

        await Assert.ThrowsAsync<AthleteNotFoundException>(() =>
            guard.EnsureCanReadAsync(athleteId));
    }
}
