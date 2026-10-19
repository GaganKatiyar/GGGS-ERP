namespace Gggs.Modules.Learning.Contracts;

/// <summary>A course someone completed, and the certificate it earned if it expires.</summary>
public sealed record LearningCompletion(Guid EmployeeId, Guid CourseId, string CourseCode, string CourseTitle, DateOnly CompletedOn, DateOnly? CertificateExpiresOn);

/// <summary>Completed learning, as evidence for reviews and career steps (ADR-12).</summary>
public interface ILearningRecords
{
    /// <summary>Completions for the employees, optionally only those completed in a period.</summary>
    Task<IReadOnlyList<LearningCompletion>> CompletionsAsync(IEnumerable<Guid> employeeIds, DateOnly? firstDay, DateOnly? lastDay, CancellationToken cancellationToken = default);
}
