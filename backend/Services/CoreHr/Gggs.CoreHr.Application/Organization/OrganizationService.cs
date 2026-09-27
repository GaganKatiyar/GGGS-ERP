using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Domain.Organization;
using Microsoft.Extensions.Logging;

namespace Gggs.CoreHr.Application.Organization;

/// <summary>
/// Creating and changing the organization structures that are not a tree: legal entities, business
/// units, branches, locations, designations and grades.
/// <para>
/// Every "does this code already exist" check here runs before the aggregate is constructed, not
/// after, for the same reason it does everywhere else in this codebase: constructing a row and then
/// discovering it collides is a wasted round trip and a database error dressed up as a business
/// rule.
/// </para>
/// </summary>
public sealed class OrganizationService(
    IOrganizationRepository organization,
    IUnitOfWork unitOfWork,
    ILogger<OrganizationService> logger)
{
    public async Task<LegalEntity> CreateLegalEntityAsync(
        string code,
        string name,
        string countryCode,
        string currency,
        string? registrationNumber,
        CancellationToken cancellationToken = default)
    {
        await EnsureCodeIsFreeAsync(
            code,
            organization.LegalEntityCodeExistsAsync,
            nameof(LegalEntity),
            cancellationToken);

        LegalEntity entity = LegalEntity.Create(code, name, countryCode, currency, registrationNumber);
        organization.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Legal entity {LegalEntityId} ({Code}) created.", entity.Id, entity.Code);
        return entity;
    }

    public async Task<LegalEntity> UpdateLegalEntityAsync(
        Guid id,
        string name,
        Address address,
        string? compliancePackCode,
        CancellationToken cancellationToken = default)
    {
        LegalEntity entity = await organization.GetLegalEntityAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(LegalEntity), id);

        entity.Rename(name);
        entity.SetAddress(address);
        entity.SetCompliancePack(compliancePackCode);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<BusinessUnit> CreateBusinessUnitAsync(
        string code,
        string name,
        CancellationToken cancellationToken = default)
    {
        await EnsureCodeIsFreeAsync(code, organization.BusinessUnitCodeExistsAsync, nameof(BusinessUnit), cancellationToken);

        BusinessUnit entity = BusinessUnit.Create(code, name);
        organization.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<BusinessUnit> UpdateBusinessUnitAsync(
        Guid id,
        string name,
        Guid? headEmployeeId,
        CancellationToken cancellationToken = default)
    {
        BusinessUnit entity = await organization.GetBusinessUnitAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(BusinessUnit), id);

        entity.Rename(name);
        entity.SetHead(headEmployeeId);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<Branch> CreateBranchAsync(
        string code,
        string name,
        Guid legalEntityId,
        string countryCode,
        string timeZone,
        CancellationToken cancellationToken = default)
    {
        await EnsureCodeIsFreeAsync(code, organization.BranchCodeExistsAsync, nameof(Branch), cancellationToken);

        _ = await organization.GetLegalEntityAsync(legalEntityId, cancellationToken)
            ?? throw new NotFoundException(nameof(LegalEntity), legalEntityId);

        Branch entity = Branch.Create(code, name, legalEntityId, countryCode, timeZone);
        organization.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<Branch> UpdateBranchAsync(
        Guid id,
        string name,
        string timeZone,
        Address address,
        string? stateCode,
        CancellationToken cancellationToken = default)
    {
        Branch entity = await organization.GetBranchAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Branch), id);

        entity.Rename(name);
        entity.SetTimeZone(timeZone);
        entity.SetAddress(address, stateCode);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <summary>
    /// Switches geo-fencing on or off for a branch.
    /// <para>
    /// A named, separate operation rather than a field on the ordinary update, because turning on a
    /// surveillance feature is a decision worth its own audit trail entry and its own permission
    /// check, not a side effect of editing an address.
    /// </para>
    /// </summary>
    public async Task<Branch> SetBranchGeoFenceAsync(
        Guid id,
        double? latitude,
        double? longitude,
        int? radiusMeters,
        CancellationToken cancellationToken = default)
    {
        Branch entity = await organization.GetBranchAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Branch), id);

        entity.SetGeoFence(latitude, longitude, radiusMeters);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<Location> CreateLocationAsync(
        string code,
        string name,
        Guid branchId,
        LocationType locationType,
        CancellationToken cancellationToken = default)
    {
        await EnsureCodeIsFreeAsync(code, organization.LocationCodeExistsAsync, nameof(Location), cancellationToken);

        _ = await organization.GetBranchAsync(branchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Branch), branchId);

        Location entity = Location.Create(code, name, branchId, locationType);
        organization.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<Designation> CreateDesignationAsync(
        string code,
        string title,
        string? jobFamily,
        int? jobLevel,
        CancellationToken cancellationToken = default)
    {
        await EnsureCodeIsFreeAsync(code, organization.DesignationCodeExistsAsync, nameof(Designation), cancellationToken);

        Designation entity = Designation.Create(code, title, jobFamily, jobLevel);
        organization.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<Designation> UpdateDesignationAsync(
        Guid id,
        string title,
        string? jobFamily,
        int? jobLevel,
        CancellationToken cancellationToken = default)
    {
        Designation entity = await organization.GetDesignationAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Designation), id);

        entity.Update(title, jobFamily, jobLevel);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<Grade> CreateGradeAsync(string code, string name, int rank, CancellationToken cancellationToken = default)
    {
        await EnsureCodeIsFreeAsync(code, organization.GradeCodeExistsAsync, nameof(Grade), cancellationToken);

        Grade entity = Grade.Create(code, name, rank);
        organization.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<Grade> UpdateGradeAsync(
        Guid id,
        string name,
        decimal? salaryBandMinimum,
        decimal? salaryBandMaximum,
        string? currency,
        CancellationToken cancellationToken = default)
    {
        Grade entity = await organization.GetGradeAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Grade), id);

        entity.Rename(name);
        entity.SetSalaryBand(salaryBandMinimum, salaryBandMaximum, currency);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <summary>
    /// Checked once, in one place, so every "create X" method above reads the same way. The code is
    /// normalized here with the same rule the aggregate itself uses, so a check against
    /// <c>"eng "</c> and a row stored as <c>"ENG"</c> do not talk past each other.
    /// </summary>
    private static async Task EnsureCodeIsFreeAsync(
        string code,
        Func<string, CancellationToken, Task<bool>> existsAsync,
        string entityTypeName,
        CancellationToken cancellationToken)
    {
        string normalized = OrganizationCode.Normalize(code);

        if (await existsAsync(normalized, cancellationToken))
        {
            throw new DomainRuleException(
                "ORGANIZATION_CODE_IN_USE",
                $"A {entityTypeName} with code '{normalized}' already exists.");
        }
    }
}
