namespace Application.Exceptions;

public sealed class CheckInNotFoundException()
    : Exception("El atleta todavía no hizo el check-in de hoy.");
