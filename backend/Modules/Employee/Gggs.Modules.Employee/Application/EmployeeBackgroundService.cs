using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Data;
using Gggs.Modules.Employee.Domain;
using Gggs.Modules.Employee.Infrastructure;
using Microsoft.EntityFrameworkCore;
using EmployeeEntity = Gggs.Modules.Employee.Domain.Employee;

namespace Gggs.Modules.Employee.Application;

public sealed record EducationInput(string Degree, string? FieldOfStudy, string Institution, int? StartYear, int? EndYear, string? Grade);

public sealed record EducationItem(Guid Id, string Degree, string? FieldOfStudy, string Institution, int? StartYear, int? EndYear, string? Grade);

public sealed record WorkExperienceInput(string Company, string Title, string? Location, DateOnly FromDate, DateOnly? ToDate, string? Summary);

public sealed record WorkExperienceItem(Guid Id, string Company, string Title, string? Location, DateOnly FromDate, DateOnly? ToDate, string? Summary);

/// <summary>
/// The employee's background: education and the jobs held before joining. The employee keeps
/// their own; HR reads and corrects them for the people in their scope.
/// </summary>
public sealed class EmployeeBackgroundService(AppDbContext dbContext, IUnitOfWork unitOfWork, EmployeeScope scope, TimeProvider timeProvider)
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<EducationItem>> EducationAsync(Guid employeeId, string? permission, CancellationToken cancellationToken = default)
    {
        await EnsureReachableAsync(employeeId, permission, cancellationToken);
        return await dbContext.EmployeeEducation.AsNoTracking()
            .Where(education => education.EmployeeId == employeeId)
            .OrderByDescending(education => education.EndYear ?? int.MaxValue)
            .ThenByDescending(education => education.StartYear)
            .Select(education => new EducationItem(education.Id, education.Degree, education.FieldOfStudy, education.Institution, education.StartYear, education.EndYear, education.Grade))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> AddEducationAsync(Guid employeeId, string? permission, EducationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await EnsureReachableAsync(employeeId, permission, cancellationToken);

        EmployeeEducation education = EmployeeEducation.Create(employeeId, input.Degree, input.FieldOfStudy, input.Institution, input.StartYear, input.EndYear, input.Grade);
        dbContext.EmployeeEducation.Add(education);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return education.Id;
    }

    public async Task UpdateEducationAsync(Guid employeeId, Guid educationId, string? permission, EducationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await EnsureReachableAsync(employeeId, permission, cancellationToken);

        EmployeeEducation education = await dbContext.EmployeeEducation
            .FirstOrDefaultAsync(candidate => candidate.Id == educationId && candidate.EmployeeId == employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeEducation), educationId);
        education.Update(input.Degree, input.FieldOfStudy, input.Institution, input.StartYear, input.EndYear, input.Grade);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveEducationAsync(Guid employeeId, Guid educationId, string? permission, CancellationToken cancellationToken = default)
    {
        await EnsureReachableAsync(employeeId, permission, cancellationToken);

        EmployeeEducation education = await dbContext.EmployeeEducation
            .FirstOrDefaultAsync(candidate => candidate.Id == educationId && candidate.EmployeeId == employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeEducation), educationId);
        dbContext.EmployeeEducation.Remove(education);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkExperienceItem>> WorkExperienceAsync(Guid employeeId, string? permission, CancellationToken cancellationToken = default)
    {
        await EnsureReachableAsync(employeeId, permission, cancellationToken);
        return await dbContext.EmployeeWorkExperience.AsNoTracking()
            .Where(experience => experience.EmployeeId == employeeId)
            .OrderByDescending(experience => experience.FromDate)
            .Select(experience => new WorkExperienceItem(experience.Id, experience.Company, experience.Title, experience.Location, experience.FromDate, experience.ToDate, experience.Summary))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> AddWorkExperienceAsync(Guid employeeId, string? permission, WorkExperienceInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await EnsureReachableAsync(employeeId, permission, cancellationToken);

        EmployeeWorkExperience experience = EmployeeWorkExperience.Create(employeeId, input.Company, input.Title, input.Location, input.FromDate, input.ToDate, input.Summary, Today);
        dbContext.EmployeeWorkExperience.Add(experience);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return experience.Id;
    }

    public async Task UpdateWorkExperienceAsync(Guid employeeId, Guid experienceId, string? permission, WorkExperienceInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await EnsureReachableAsync(employeeId, permission, cancellationToken);

        EmployeeWorkExperience experience = await dbContext.EmployeeWorkExperience
            .FirstOrDefaultAsync(candidate => candidate.Id == experienceId && candidate.EmployeeId == employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeWorkExperience), experienceId);
        experience.Update(input.Company, input.Title, input.Location, input.FromDate, input.ToDate, input.Summary, Today);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveWorkExperienceAsync(Guid employeeId, Guid experienceId, string? permission, CancellationToken cancellationToken = default)
    {
        await EnsureReachableAsync(employeeId, permission, cancellationToken);

        EmployeeWorkExperience experience = await dbContext.EmployeeWorkExperience
            .FirstOrDefaultAsync(candidate => candidate.Id == experienceId && candidate.EmployeeId == employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeWorkExperience), experienceId);
        dbContext.EmployeeWorkExperience.Remove(experience);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// HR calls name the permission the caller's data scope is read for; self-service passes none,
    /// because the endpoint has already taken the id from the caller's own session.
    /// </summary>
    private async Task EnsureReachableAsync(Guid employeeId, string? permission, CancellationToken cancellationToken)
    {
        IQueryable<EmployeeEntity> employees = permission is null
            ? dbContext.Employees.AsNoTracking()
            : await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), permission, cancellationToken);
        if (!await employees.AnyAsync(employee => employee.Id == employeeId, cancellationToken))
        {
            throw new NotFoundException("Employee", employeeId);
        }
    }
}
