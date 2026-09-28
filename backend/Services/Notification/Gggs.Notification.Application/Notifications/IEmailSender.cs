namespace Gggs.Notification.Application.Notifications;

/// <summary>
/// Sends one rendered email.
/// <para>
/// Only ever called by <see cref="NotificationDispatchService"/>, which has already rendered the
/// subject and body and already decided this recipient should get one - this port does not decide
/// policy, it delivers bytes.
/// </para>
/// </summary>
public interface IEmailSender
{
    /// <summary>Returns a provider message id when the provider gives one, for troubleshooting a
    /// specific delivery later; null is fine when it does not.</summary>
    Task<string?> SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default);
}
