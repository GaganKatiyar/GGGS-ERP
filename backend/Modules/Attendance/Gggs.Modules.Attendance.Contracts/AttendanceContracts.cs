namespace Gggs.Modules.Attendance.Contracts;

/// <summary>Judged attendance for payroll (ADR-12).</summary>
public interface IAttendanceLedger
{
    /// <summary>
    /// Days absent per person in the window: an absent day counts 1, a half day 0.5. Leave, weekly
    /// offs and holidays are not absences. People with no absence are left out.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> AbsenceDaysAsync(IEnumerable<Guid> employeeIds, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default);
}
