using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Time.Api.Contracts;
using Gggs.Time.Application.Holidays;
using Gggs.Time.Domain.Holidays;
using Gggs.Time.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Time.Api.Endpoints;

public static class HolidayEndpoints
{
    public static IEndpointRouteBuilder MapHolidayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/holidays/calendars").WithTags("Holidays");

        group.MapGet("/", async (TimeDbContext database, CancellationToken cancellationToken) =>
        {
            List<HolidayCalendarResponse> items = await database.HolidayCalendars.AsNoTracking()
                .OrderByDescending(x => x.Year)
                .Select(x => new HolidayCalendarResponse(x.Id, x.Name, x.Year, x.CountryCode, x.StateCode))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("ListHolidayCalendars").RequirePermission(Permissions.Leave.RequestView);

        group.MapPost("/", async (CreateHolidayCalendarBody body, HolidayCalendarService service, CancellationToken cancellationToken) =>
        {
            HolidayCalendar entity = await service.CreateAsync(body.Name, body.Year, body.CountryCode, body.StateCode, cancellationToken);
            return TypedResults.Created(
                $"/api/v1/holidays/calendars/{entity.Id}",
                new HolidayCalendarResponse(entity.Id, entity.Name, entity.Year, entity.CountryCode, entity.StateCode));
        }).WithName("CreateHolidayCalendar").RequirePermission(Permissions.Leave.HolidayManage);

        group.MapGet("/{id:guid}/holidays", async (Guid id, TimeDbContext database, CancellationToken cancellationToken) =>
        {
            List<HolidayResponse> items = await database.Holidays.AsNoTracking()
                .Where(x => x.HolidayCalendarId == id)
                .OrderBy(x => x.HolidayDate)
                .Select(x => new HolidayResponse(x.Id, x.HolidayCalendarId, x.HolidayDate, x.Name, x.IsOptional, x.IsRestricted))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        }).WithName("ListHolidays").RequirePermission(Permissions.Leave.RequestView);

        group.MapPost("/{id:guid}/holidays", async (
            Guid id, AddHolidayBody body, HolidayCalendarService service, CancellationToken cancellationToken) =>
        {
            Holiday holiday = await service.AddHolidayAsync(
                id, body.HolidayDate, body.Name, body.IsOptional, body.IsRestricted, cancellationToken);

            return TypedResults.Created(
                $"/api/v1/holidays/calendars/{id}/holidays/{holiday.Id}",
                new HolidayResponse(holiday.Id, holiday.HolidayCalendarId, holiday.HolidayDate, holiday.Name, holiday.IsOptional, holiday.IsRestricted));
        }).WithName("AddHoliday").RequirePermission(Permissions.Leave.HolidayManage);

        return endpoints;
    }
}
