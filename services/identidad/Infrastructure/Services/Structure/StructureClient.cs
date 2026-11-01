using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Structure;

public sealed class StructureClient(HttpClient httpClient, ILogger<StructureClient> logger) : IStructureClient
{
    public async Task<CategoryInvitationDto> ValidateInvitationTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvitationNotFoundException();
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync($"api/v1/invitations/{Uri.EscapeDataString(token.Trim())}", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error de red al comunicarse con el servicio de Estructura.");
            throw new StructureUnavailableException("No se pudo contactar a Estructura para validar la invitación.", ex);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var dto = await response.Content.ReadFromJsonAsync<CategoryInvitationDto>(cancellationToken: cancellationToken);
                    if (dto is null)
                    {
                        throw new StructureUnavailableException("Estructura respondió OK pero el cuerpo de la invitación vino vacío.");
                    }
                    return dto;

                case HttpStatusCode.Gone: // 410
                    logger.LogWarning("Token de invitación expirado en Estructura: {Token}", token);
                    throw new InvitationExpiredException();

                case HttpStatusCode.NotFound: // 404
                case HttpStatusCode.BadRequest: // 400
                    logger.LogWarning("Token de invitación no encontrado o inválido en Estructura: {Token}", token);
                    throw new InvitationNotFoundException();

                default:
                    logger.LogError("Estructura respondió con código {Status} al validar la invitación.", (int)response.StatusCode);
                    throw new StructureUnavailableException();
            }
        }
    }
}