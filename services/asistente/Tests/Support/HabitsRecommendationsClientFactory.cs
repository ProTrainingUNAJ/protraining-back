using Application.Interfaces.Services;
using Moq;

namespace Asistente.Tests;

/// <summary>
/// Arma los clientes de KAN-493 usados por las pruebas del Asistente.
/// Los tests que no ejercitan la preferencia la dejan habilitada para conservar su comportamiento original.
/// </summary>
internal static class HabitsRecommendationsClientFactory
{
    public static void SetupHabitsPermission(
        this Mock<IStructureAthleteClient> mock,
        Guid athleteId,
        bool isMinor = false,
        bool allows = true) =>
        mock.Setup(s => s.GetHabitsPermissionAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteHabitsPermissionDto(isMinor, allows));

    public static void SetupMyHabitsPermission(
        this Mock<IStructureAthleteClient> mock,
        bool isMinor = false,
        bool allows = true) =>
        mock.Setup(s => s.GetMyHabitsPermissionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteHabitsPermissionDto(isMinor, allows));

    public static IIdentityHabitsRecommendationsClient Enabled() =>
        Create(enabled: true, athleteEnabled: true);

    public static IIdentityHabitsRecommendationsClient Create(bool? enabled, bool? athleteEnabled = null) =>
        CreateMock(enabled, athleteEnabled ?? enabled).Object;

    public static Mock<IIdentityHabitsRecommendationsClient> CreateMock(bool? enabled, bool? athleteEnabled = null)
    {
        var mock = new Mock<IIdentityHabitsRecommendationsClient>();
        mock.Setup(c => c.GetMyHabitsRecommendationsEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(enabled);
        mock.Setup(c => c.GetAthletePfHabitsRecommendationsEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(athleteEnabled ?? enabled);
        return mock;
    }
}
