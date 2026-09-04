using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using MyCoinFlow.WinUI.Models;

namespace MyCoinFlow.WinUI.Services;

public interface IInvoicingEmailTransport
{
    Task SendAsync(
        InvoicingSmtpConfiguration configuration,
        string? password,
        InvoicingPreparedEmail message,
        CancellationToken cancellationToken = default);
}

public sealed class InvoicingSmtpEmailTransport : IInvoicingEmailTransport
{
    public async Task SendAsync(
        InvoicingSmtpConfiguration configuration,
        string? password,
        InvoicingPreparedEmail message,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.IsConfigured)
            throw new InvalidOperationException("SMTP ist unter Finanzen noch nicht vollständig eingerichtet.");
        if (!string.IsNullOrWhiteSpace(configuration.UserName) && string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Für den konfigurierten SMTP-Benutzer fehlt das lokal gespeicherte Kennwort.");

        using var mail = new MailMessage
        {
            From = new MailAddress(configuration.FromAddress),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message.Body,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        mail.To.Add(new MailAddress(message.RecipientAddress));
        mail.Headers.Add("X-MyCoinFlow-Message-Id", message.MessageId);
        foreach (var attachment in message.Attachments)
        {
            var stream = new MemoryStream(attachment.Content, writable: false);
            mail.Attachments.Add(new Attachment(
                stream,
                attachment.FileName,
                string.IsNullOrWhiteSpace(attachment.ContentType)
                    ? MediaTypeNames.Application.Octet
                    : attachment.ContentType));
        }

        using var client = new SmtpClient(configuration.Host, configuration.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = configuration.UseTls,
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrWhiteSpace(configuration.UserName))
            client.Credentials = new NetworkCredential(configuration.UserName, password);

        await client.SendMailAsync(mail, cancellationToken);
    }
}
