using Gggs.BuildingBlocks.Application;
using Gggs.Data;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Notification.Domain;
using Gggs.Modules.Notification.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Notification.Application;

/// <summary>One kind of notification as the preferences page shows it.</summary>
public sealed record NotificationPreferenceItem(
    string Code,
    string Name,
    string Module,
    bool TemplateSendsEmail,
    bool TemplateSendsInApp,
    bool EmailEnabled,
    bool InAppEnabled,
    bool IsMandatory,
    bool TemplateSendsSms,
    bool SmsEnabled);

public sealed record NotificationPreferenceChoice(string Code, bool EmailEnabled, bool InAppEnabled, bool SmsEnabled = true);

/// <summary>
/// The signed-in person's notification choices. The list is the tenant's active templates, so it
/// shows exactly the notifications Nuts and Bolts can send them, in the tenant's own names.
/// </summary>
public sealed class NotificationPreferences(AppDbContext dbContext, ICurrentUser currentUser, ITenantMessageSender sender)
{
    public async Task<IReadOnlyList<NotificationPreferenceItem>> MineAsync(CancellationToken cancellationToken = default)
    {
        Guid userId = currentUser.UserId;
        bool sendsSms = await sender.SendsSmsAsync(cancellationToken);
        List<NotificationTemplate> templates = await dbContext.NotificationTemplates.AsNoTracking()
            .Where(template => template.IsActive && (template.SendEmail || template.SendInApp || (sendsSms && template.SendSms)))
            .ToListAsync(cancellationToken);
        Dictionary<string, NotificationPreference> chosen = await dbContext.NotificationPreferences.AsNoTracking()
            .Where(preference => preference.UserId == userId)
            .ToDictionaryAsync(preference => preference.EventCode, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return
        [
            .. templates
                .Select(template =>
                {
                    bool mandatory = NotificationTemplateCatalog.Mandatory.Contains(template.Code);
                    NotificationPreference? choice = mandatory ? null : chosen.GetValueOrDefault(template.Code);
                    return new NotificationPreferenceItem(
                        template.Code,
                        template.Name,
                        template.Code.Split('.')[0],
                        template.SendEmail,
                        template.SendInApp,
                        choice?.EmailEnabled ?? true,
                        choice?.InAppEnabled ?? true,
                        mandatory,
                        sendsSms && template.SendSms,
                        choice?.SmsEnabled ?? true);
                })
                .OrderBy(item => item.Module, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    /// <summary>Saves the choices sent; codes that are mandatory or unknown are ignored rather than refused.</summary>
    public async Task SaveAsync(IReadOnlyList<NotificationPreferenceChoice> choices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choices);
        Guid userId = currentUser.UserId;
        Guid? employeeId = currentUser.EmployeeId;

        HashSet<string> known = [.. await dbContext.NotificationTemplates.AsNoTracking().Select(template => template.Code).ToListAsync(cancellationToken)];
        Dictionary<string, NotificationPreference> existing = await dbContext.NotificationPreferences
            .Where(preference => preference.UserId == userId)
            .ToDictionaryAsync(preference => preference.EventCode, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (NotificationPreferenceChoice choice in choices)
        {
            string code = choice.Code.Trim().ToLowerInvariant();
            if (!known.Contains(code) || NotificationTemplateCatalog.Mandatory.Contains(code))
            {
                continue;
            }

            if (existing.TryGetValue(code, out NotificationPreference? preference))
            {
                preference.Set(employeeId, choice.EmailEnabled, choice.InAppEnabled, choice.SmsEnabled);
            }
            else
            {
                NotificationPreference created = NotificationPreference.Create(userId, employeeId, code, choice.EmailEnabled, choice.InAppEnabled, choice.SmsEnabled);
                dbContext.NotificationPreferences.Add(created);
                existing[code] = created;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
