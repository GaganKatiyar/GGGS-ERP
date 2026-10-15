using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Skills.Application;

namespace Gggs.Modules.Skills.Endpoints;

/// <summary>Skills under <c>/api/v1/skills</c>, and one's own under <c>/api/v1/me/skills</c> (spec §29).</summary>
internal static class SkillEndpoints
{
    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/skills").WithTags("Skills");

        group.MapGet("/", async (string? search, string? category, bool? includeInactive, SkillService skills, CancellationToken ct) =>
                TypedResults.Ok(await skills.CatalogAsync(search, category, includeInactive == true, ct)))
            .WithName("ListSkills").RequirePermission(Permissions.Skill.View);
        group.MapPost("/", async (SkillBody body, SkillService skills, CancellationToken ct) => TypedResults.Ok(await skills.SaveSkillAsync(null, body, ct)))
            .WithName("CreateSkill").RequirePermission(Permissions.Skill.Manage);
        group.MapPut("/{id:guid}", async (Guid id, SkillBody body, SkillService skills, CancellationToken ct) => TypedResults.Ok(await skills.SaveSkillAsync(id, body, ct)))
            .WithName("UpdateSkill").RequirePermission(Permissions.Skill.Manage);
        group.MapPost("/{id:guid}/activate", async (Guid id, SkillService skills, CancellationToken ct) =>
            {
                await skills.SetSkillActiveAsync(id, true, ct);
                return TypedResults.NoContent();
            })
            .WithName("ActivateSkill").RequirePermission(Permissions.Skill.Manage);
        group.MapPost("/{id:guid}/deactivate", async (Guid id, SkillService skills, CancellationToken ct) =>
            {
                await skills.SetSkillActiveAsync(id, false, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeactivateSkill").RequirePermission(Permissions.Skill.Manage);
        group.MapGet("/matrix", async (string? category, SkillService skills, CancellationToken ct) => TypedResults.Ok(await skills.MatrixAsync(category, ct)))
            .WithName("SkillMatrix").RequirePermission(Permissions.Skill.EmployeeSkillView);
        group.MapGet("/{id:guid}/people", async (Guid id, int? minLevel, SkillService skills, CancellationToken ct) => TypedResults.Ok(await skills.PeopleWithAsync(id, minLevel ?? 1, ct)))
            .WithName("PeopleWithSkill").RequirePermission(Permissions.Skill.EmployeeSkillView);

        group.MapGet("/employees/{employeeId:guid}", async (Guid employeeId, SkillService skills, CancellationToken ct) => TypedResults.Ok(await skills.ProfileAsync(employeeId, ct)))
            .WithName("EmployeeSkills").RequirePermission(Permissions.Skill.EmployeeSkillView);
        group.MapPut("/employees/{employeeId:guid}", async (Guid employeeId, EmployeeSkillBody body, SkillService skills, CancellationToken ct) =>
                TypedResults.Ok(await skills.SaveSkillAsync(employeeId, body, self: false, ct)))
            .WithName("AssessEmployeeSkill").RequirePermission(Permissions.Skill.EmployeeSkillManage);
        group.MapDelete("/employees/{employeeId:guid}/{skillId:guid}", async (Guid employeeId, Guid skillId, SkillService skills, CancellationToken ct) =>
                TypedResults.Ok(await skills.RemoveSkillAsync(employeeId, skillId, self: false, ct)))
            .WithName("RemoveEmployeeSkill").RequirePermission(Permissions.Skill.EmployeeSkillManage);
        group.MapPost("/employees/{employeeId:guid}/certifications", async (Guid employeeId, CertificationBody body, SkillService skills, CancellationToken ct) =>
                TypedResults.Ok(await skills.SaveCertificationAsync(employeeId, null, body, self: false, ct)))
            .WithName("AddEmployeeCertification").RequirePermission(Permissions.Skill.EmployeeSkillManage);
        group.MapPut("/employees/{employeeId:guid}/certifications/{certificationId:guid}", async (Guid employeeId, Guid certificationId, CertificationBody body, SkillService skills, CancellationToken ct) =>
                TypedResults.Ok(await skills.SaveCertificationAsync(employeeId, certificationId, body, self: false, ct)))
            .WithName("UpdateEmployeeCertification").RequirePermission(Permissions.Skill.EmployeeSkillManage);
        group.MapDelete("/employees/{employeeId:guid}/certifications/{certificationId:guid}", async (Guid employeeId, Guid certificationId, SkillService skills, CancellationToken ct) =>
                TypedResults.Ok(await skills.RemoveCertificationAsync(employeeId, certificationId, self: false, ct)))
            .WithName("RemoveEmployeeCertification").RequirePermission(Permissions.Skill.EmployeeSkillManage);

        RouteGroupBuilder me = endpoints.MapGroup("/api/v1/me/skills").WithTags("Skills");
        me.MapGet("/", async (SkillService skills, CancellationToken ct) => TypedResults.Ok(await skills.MyProfileAsync(ct)))
            .WithName("MySkills").RequirePermission(Permissions.Self.SkillManage);
        me.MapGet("/catalog", async (SkillService skills, CancellationToken ct) => TypedResults.Ok(await skills.CatalogAsync(null, null, includeInactive: false, ct)))
            .WithName("MySkillCatalog").RequirePermission(Permissions.Self.SkillManage);
        me.MapPut("/", async (EmployeeSkillBody body, SkillService skills, ICurrentUser user, CancellationToken ct) =>
                TypedResults.Ok(await skills.SaveSkillAsync(Me(user), body, self: true, ct)))
            .WithName("AssessMySkill").RequirePermission(Permissions.Self.SkillManage);
        me.MapDelete("/{skillId:guid}", async (Guid skillId, SkillService skills, ICurrentUser user, CancellationToken ct) =>
                TypedResults.Ok(await skills.RemoveSkillAsync(Me(user), skillId, self: true, ct)))
            .WithName("RemoveMySkill").RequirePermission(Permissions.Self.SkillManage);
        me.MapPost("/certifications", async (CertificationBody body, SkillService skills, ICurrentUser user, CancellationToken ct) =>
                TypedResults.Ok(await skills.SaveCertificationAsync(Me(user), null, body, self: true, ct)))
            .WithName("AddMyCertification").RequirePermission(Permissions.Self.SkillManage);
        me.MapDelete("/certifications/{certificationId:guid}", async (Guid certificationId, SkillService skills, ICurrentUser user, CancellationToken ct) =>
                TypedResults.Ok(await skills.RemoveCertificationAsync(Me(user), certificationId, self: true, ct)))
            .WithName("RemoveMyCertification").RequirePermission(Permissions.Self.SkillManage);

        return endpoints;
    }

    private static Guid Me(ICurrentUser user)
        => user.EmployeeId ?? throw new DomainRuleException("SKILL_NO_EMPLOYEE", "Your account is not linked to an employee record.");
}
