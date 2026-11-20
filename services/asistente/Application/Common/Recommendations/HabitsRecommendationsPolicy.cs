using Application.Interfaces.Services;

namespace Application.Common.Recommendations;

/// <summary>
/// Combina la preferencia del PF (KAN-493) con la autorización del tutor (KAN-496).
/// Las recomendaciones solo están habilitadas si ambas condiciones aplicables están favorables.
/// Un atleta mayor de edad no requiere autorización de tutor: se respeta la regla vigente de KAN-496.
/// Cualquier consulta fallida se representa con null y se traduce en bloqueo (fail-closed).
/// </summary>
public static class HabitsRecommendationsPolicy
{
    public static bool IsEnabled(bool? pfEnabled, StructureAthleteHabitsPermissionDto? permission)
    {
        if (pfEnabled is not true || permission is null)
            return false;

        return !permission.IsMinor || permission.AllowsHabitsRecommendations;
    }
}
