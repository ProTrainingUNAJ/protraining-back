namespace Application.DTOs.Users;

public sealed record PfContactDto(string? PhoneNumber, string? TelegramUsername = null);
