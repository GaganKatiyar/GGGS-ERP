using Gggs.BuildingBlocks.Messaging;

namespace Gggs.Modules.Learning.Contracts;

/// <summary>A course someone completed, and the certificate it earned if it expires.</summary>
public sealed record LearningCompletion(Guid EmployeeId, Guid CourseId, string CourseCode, string CourseTitle, DateOnly CompletedOn, DateOnly? CertificateExpiresOn);

/// <summary>Someone completed a course.</summary>
[MessageType("gggs.learning.course-completed")]
public sealed record CourseCompletedIntegrationEvent(Guid EnrollmentId, Guid EmployeeId, Guid CourseId, string CourseTitle, DateOnly CompletedOn) : IntegrationEvent;

/// <summary>Completed learning, as evidence for reviews and career steps (ADR-12).</summary>
public interface ILearningRecords
{
    /// <summary>Completions for the employees, optionally only those completed in a period.</summary>
    Task<IReadOnlyList<LearningCompletion>> CompletionsAsync(IEnumerable<Guid> employeeIds, DateOnly? firstDay, DateOnly? lastDay, CancellationToken cancellationToken = default);
}
