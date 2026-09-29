using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Time.Domain.Attendance;

namespace Gggs.Time.Application.Attendance;

public sealed class ShiftService(IShiftRepository shifts, IUnitOfWork unitOfWork)
{
    public async Task<Shift> CreateAsync(
        string code,
        string name,
        TimeOnly startTime,
        TimeOnly endTime,
        int breakMinutes,
        bool isNightShift,
        int graceInMinutes,
        int graceOutMinutes,
        int fullDayMinMinutes,
        int halfDayMinMinutes,
        CancellationToken cancellationToken = default)
    {
        Shift? existing = await shifts.GetByCodeAsync(code, cancellationToken);

        if (existing is not null)
        {
            throw new DomainRuleException("SHIFT_CODE_IN_USE", $"A shift with code '{code}' already exists.");
        }

        Shift shift = Shift.Create(
            code, name, startTime, endTime, breakMinutes, isNightShift, graceInMinutes, graceOutMinutes,
            fullDayMinMinutes, halfDayMinMinutes);

        shifts.Add(shift);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return shift;
    }
}

public sealed class ShiftRosterService(IShiftRepository shifts, IShiftRosterRepository rosters, IUnitOfWork unitOfWork)
{
    public async Task<ShiftRoster> AssignAsync(
        Guid employeeId, DateOnly rosterDate, Guid shiftId, CancellationToken cancellationToken = default)
    {
        _ = await shifts.GetAsync(shiftId, cancellationToken)
            ?? throw new NotFoundException(nameof(Shift), shiftId);

        ShiftRoster? existing = await rosters.GetForDateAsync(employeeId, rosterDate, cancellationToken);

        if (existing is not null)
        {
            throw new DomainRuleException(
                "SHIFT_ROSTER_ALREADY_ASSIGNED", $"This employee already has a roster entry for {rosterDate:yyyy-MM-dd}.");
        }

        ShiftRoster roster = ShiftRoster.CreateWorkingDay(employeeId, rosterDate, shiftId, RosterSource.Manual);
        rosters.Add(roster);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return roster;
    }

    public async Task<ShiftRoster> AssignWeeklyOffAsync(Guid employeeId, DateOnly rosterDate, CancellationToken cancellationToken = default)
    {
        ShiftRoster? existing = await rosters.GetForDateAsync(employeeId, rosterDate, cancellationToken);

        if (existing is not null)
        {
            throw new DomainRuleException(
                "SHIFT_ROSTER_ALREADY_ASSIGNED", $"This employee already has a roster entry for {rosterDate:yyyy-MM-dd}.");
        }

        ShiftRoster roster = ShiftRoster.CreateWeeklyOff(employeeId, rosterDate, RosterSource.Manual);
        rosters.Add(roster);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return roster;
    }
}
