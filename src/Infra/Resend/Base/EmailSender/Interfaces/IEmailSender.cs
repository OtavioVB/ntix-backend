namespace Infra.Resend.Base.EmailSender.Interfaces;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string html, CancellationToken cancellationToken = default);
}
