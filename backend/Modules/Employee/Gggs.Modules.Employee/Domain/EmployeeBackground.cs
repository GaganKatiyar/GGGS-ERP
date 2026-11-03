using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Employee.Domain;

/// <summary>A qualification the employee holds: a degree, diploma or certificate course.</summary>
public sealed class EmployeeEducation : TenantAggregateRoot
{
    private EmployeeEducation()
    {
        Degree = null!;
        Institution = null!;
    }

    public Guid EmployeeId { get; private set; }

    public string Degree { get; private set; }

    public string? FieldOfStudy { get; private set; }

    public string Institution { get; private set; }

    public int? StartYear { get; private set; }

    public int? EndYear { get; private set; }

    /// <summary>As the institution states it: "8.4 CGPA", "First class", "78%".</summary>
    public string? Grade { get; private set; }

    public static EmployeeEducation Create(Guid employeeId, string degree, string? fieldOfStudy, string institution, int? startYear, int? endYear, string? grade)
    {
        EmployeeEducation education = new() { EmployeeId = Guard.NotEmpty(employeeId) };
        education.Update(degree, fieldOfStudy, institution, startYear, endYear, grade);
        return education;
    }

    public void Update(string degree, string? fieldOfStudy, string institution, int? startYear, int? endYear, string? grade)
    {
        Guard.Against(startYear is < 1950 or > 2100 || endYear is < 1950 or > 2100, "EDUCATION_YEAR_INVALID", "Years must be between 1950 and 2100.");
        Guard.Against(startYear is { } start && endYear is { } end && end < start, "EDUCATION_YEARS_REVERSED", "The year finished cannot be before the year started.");

        Degree = Guard.MaxLength(Guard.NotNullOrWhiteSpace(degree).Trim(), 120);
        FieldOfStudy = Text(fieldOfStudy, 120);
        Institution = Guard.MaxLength(Guard.NotNullOrWhiteSpace(institution).Trim(), 200);
        StartYear = startYear;
        EndYear = endYear;
        Grade = Text(grade, 32);
    }

    private static string? Text(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), max);
}

/// <summary>A job the employee held before joining: what the profile shows as prior experience.</summary>
public sealed class EmployeeWorkExperience : TenantAggregateRoot
{
    private EmployeeWorkExperience()
    {
        Company = null!;
        Title = null!;
    }

    public Guid EmployeeId { get; private set; }

    public string Company { get; private set; }

    public string Title { get; private set; }

    public string? Location { get; private set; }

    public DateOnly FromDate { get; private set; }

    /// <summary>Empty while the role is still held, as for someone serving notice elsewhere when hired.</summary>
    public DateOnly? ToDate { get; private set; }

    public string? Summary { get; private set; }

    public static EmployeeWorkExperience Create(Guid employeeId, string company, string title, string? location, DateOnly fromDate, DateOnly? toDate, string? summary, DateOnly today)
    {
        EmployeeWorkExperience experience = new() { EmployeeId = Guard.NotEmpty(employeeId) };
        experience.Update(company, title, location, fromDate, toDate, summary, today);
        return experience;
    }

    public void Update(string company, string title, string? location, DateOnly fromDate, DateOnly? toDate, string? summary, DateOnly today)
    {
        Guard.Against(fromDate > today, "EXPERIENCE_IN_FUTURE", "Experience cannot start in the future.");
        Guard.Against(toDate is { } to && to < fromDate, "EXPERIENCE_DATES_REVERSED", "The end date cannot be before the start date.");

        Company = Guard.MaxLength(Guard.NotNullOrWhiteSpace(company).Trim(), 200);
        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 120);
        Location = string.IsNullOrWhiteSpace(location) ? null : Guard.MaxLength(location.Trim(), 120);
        FromDate = fromDate;
        ToDate = toDate;
        Summary = string.IsNullOrWhiteSpace(summary) ? null : Guard.MaxLength(summary.Trim(), 1000);
    }
}
