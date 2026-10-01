using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gggs.Payroll.Application.Attendance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Gggs.Payroll.Infrastructure.Attendance;

/// <summary>Wire shape Time answers with - a client-side copy of
/// <c>Gggs.Time.Api.Contracts.AttendanceDayResponse</c>, trimmed to what proration needs.</summary>
internal sealed record AttendanceDayWireResponse(DateOnly AttendanceDate, RemoteAttendanceDayStatus Status);

/// <inheritdoc cref="IAttendanceDaysClient"/>
public sealed class HttpAttendanceDaysClient(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    ILogger<HttpAttendanceDaysClient> logger) : IAttendanceDaysClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<IReadOnlyList<RemoteAttendanceDay>> ListAsync(
        Guid employeeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        string? token = ReadBearerToken();

        if (token is null)
        {
            logger.LogWarning("The request has no bearer token to forward; no attendance days were resolved.");
            return [];
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/attendance/days?employeeId={employeeId:D}&fromDate={fromDate:yyyy-MM-dd}&toDate={toDate:yyyy-MM-dd}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Including when Time refuses for lack of permission: the acting user's own token is
            // what was forwarded, so a denial here is their own permission gap, not an outage. Either
            // way, no attendance data means proration cannot run - the caller treats an empty list as
            // "no days on record," which keeps every employee fully paid rather than guessing.
            logger.LogWarning(
                "Time refused an attendance lookup for employee {EmployeeId} with {StatusCode}.",
                employeeId, (int)response.StatusCode);
            return [];
        }

        List<AttendanceDayWireResponse>? days = await response.Content.ReadFromJsonAsync<List<AttendanceDayWireResponse>>(
            SerializerOptions, cancellationToken);

        return days?.Select(x => new RemoteAttendanceDay(x.AttendanceDate, x.Status)).ToList() ?? [];
    }

    private string? ReadBearerToken()
    {
        string? header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();

        return !string.IsNullOrWhiteSpace(header)
            && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header["Bearer ".Length..].Trim()
                : null;
    }
}
