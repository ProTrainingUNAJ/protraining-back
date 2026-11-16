using Application.Interfaces.Services;

namespace Identidad.Tests.Infrastructure;

public sealed record SentEmail(string ToEmail, string ResetToken, string ResetLink);
public sealed record SentConfirmationEmail(string ToEmail, Guid UserId, string ConfirmationToken, string ConfirmationLink);

/// <summary>
/// Fake de IEmailSender para capturar los correos emitidos durante las pruebas de integración (#KAN-540, KAN-601).
/// </summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<SentEmail> _sentEmails = [];
    private readonly List<SentConfirmationEmail> _sentConfirmationEmails = [];
    public bool ShouldFail { get; set; }

    public IReadOnlyList<SentEmail> SentEmails
    {
        get
        {
            lock (_sentEmails)
            {
                return [.. _sentEmails];
            }
        }
    }

    public IReadOnlyList<SentConfirmationEmail> SentConfirmationEmails
    {
        get
        {
            lock (_sentConfirmationEmails)
            {
                return [.. _sentConfirmationEmails];
            }
        }
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("Falla simulada en la conexión con el servidor SMTP.");
        }

        var encodedEmail = Uri.EscapeDataString(toEmail);
        var encodedToken = Uri.EscapeDataString(resetToken);
        var resetLink = $"http://localhost:5173/reset-password?email={encodedEmail}&token={encodedToken}";

        lock (_sentEmails)
        {
            _sentEmails.Add(new SentEmail(toEmail, resetToken, resetLink));
        }

        return Task.CompletedTask;
    }

    public Task SendConfirmationEmailAsync(string toEmail, Guid userId, string confirmationToken, CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("Falla simulada en la conexión con el servidor SMTP.");
        }

        var encodedToken = Uri.EscapeDataString(confirmationToken);
        var confirmLink = $"http://localhost:5173/confirmar-email?userId={userId}&token={encodedToken}";

        lock (_sentConfirmationEmails)
        {
            _sentConfirmationEmails.Add(new SentConfirmationEmail(toEmail, userId, confirmationToken, confirmLink));
        }

        return Task.CompletedTask;
    }

    public void Clear()
    {
        lock (_sentEmails)
        {
            _sentEmails.Clear();
        }
        lock (_sentConfirmationEmails)
        {
            _sentConfirmationEmails.Clear();
        }
        ShouldFail = false;
    }
}
