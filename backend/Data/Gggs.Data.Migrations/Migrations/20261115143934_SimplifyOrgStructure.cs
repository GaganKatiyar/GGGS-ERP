using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <summary>
    /// A simpler organization: a person joins a department (or one of its functions) with a role, a
    /// designation and a level. Positions, grades and profit centers go; cost centers become the
    /// company name an office works under, with a default per company.
    /// <para>
    /// Data is carried over before anything is dropped: every grade becomes the level of the same
    /// rank (a level is created where none has that rank), so salary bands, notice rules, offers,
    /// promotions, career paths and people keep their seniority; the titles people hold become
    /// designations; and everyone works under their company's default cost center.
    /// </para>
    /// </summary>
    public partial class SimplifyOrgStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The tenant policy names the tables dropped below; it is rebuilt at the end.
            migrationBuilder.DisableTenantRowLevelSecurity();

            // ----- Grades become levels -------------------------------------------------------
            migrationBuilder.Sql("""
                -- A level for every grade rank that has none.
                INSERT INTO org.JobLevel (Id, Rank, TenantId, IsActive, IsDeleted, CreatedBy, CreatedDate, Code, Name)
                SELECT NEWID(), g.Rank, g.TenantId, g.IsActive, 0, g.CreatedBy, SYSUTCDATETIME(),
                       CASE WHEN EXISTS (SELECT 1 FROM org.JobLevel l WHERE l.TenantId = g.TenantId AND l.Code = g.Code AND l.IsDeleted = 0)
                            THEN LEFT(g.Code, 28) + '-GR' ELSE g.Code END,
                       g.Name
                FROM org.Grade g
                WHERE g.IsDeleted = 0
                  AND NOT EXISTS (SELECT 1 FROM org.JobLevel l WHERE l.TenantId = g.TenantId AND l.Rank = g.Rank AND l.IsDeleted = 0);

                -- A level standing in for an active grade is active too: people and bands point at it.
                UPDATE l SET l.IsActive = 1
                FROM org.JobLevel l
                JOIN org.Grade g ON g.TenantId = l.TenantId AND g.Rank = l.Rank AND g.IsDeleted = 0 AND g.IsActive = 1
                WHERE l.IsDeleted = 0 AND l.IsActive = 0;

                -- Levels still called "Level N" take the grade's more telling name.
                UPDATE l SET l.Name = g.Name
                FROM org.JobLevel l
                JOIN org.Grade g ON g.TenantId = l.TenantId AND g.Rank = l.Rank AND g.IsDeleted = 0
                WHERE l.Name = CONCAT('Level ', l.Rank);

                DECLARE @map TABLE (GradeId uniqueidentifier PRIMARY KEY, LevelId uniqueidentifier NOT NULL);
                INSERT INTO @map (GradeId, LevelId)
                SELECT g.Id, x.Id
                FROM org.Grade g
                CROSS APPLY (SELECT TOP 1 l.Id FROM org.JobLevel l
                             WHERE l.TenantId = g.TenantId AND l.Rank = g.Rank AND l.IsDeleted = 0
                             ORDER BY l.CreatedDate) x;

                -- Grade ids become level ids in place; the columns are renamed below.
                UPDATE t SET t.GradeId = m.LevelId FROM emp.Employee t JOIN @map m ON m.GradeId = t.GradeId;
                UPDATE t SET t.GradeId = m.LevelId FROM emp.EmploymentRecord t JOIN @map m ON m.GradeId = t.GradeId;
                UPDATE t SET t.GradeId = m.LevelId FROM comp.SalaryBand t JOIN @map m ON m.GradeId = t.GradeId;
                UPDATE t SET t.NewGradeId = m.LevelId FROM comp.CompensationRevision t JOIN @map m ON m.GradeId = t.NewGradeId;
                UPDATE t SET t.ProposedGradeId = m.LevelId FROM car.Promotion t JOIN @map m ON m.GradeId = t.ProposedGradeId;
                UPDATE t SET t.GradeId = m.LevelId FROM rec.Offer t JOIN @map m ON m.GradeId = t.GradeId;
                UPDATE t SET t.GradeId = m.LevelId FROM sep.NoticeRule t JOIN @map m ON m.GradeId = t.GradeId;

                -- Career path steps keep their grade as JSON: swap each id, then the property name.
                DECLARE @grade nvarchar(36), @level nvarchar(36);
                DECLARE steps CURSOR LOCAL FAST_FORWARD FOR
                    SELECT LOWER(CONVERT(nvarchar(36), GradeId)), LOWER(CONVERT(nvarchar(36), LevelId)) FROM @map;
                OPEN steps;
                FETCH NEXT FROM steps INTO @grade, @level;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    UPDATE car.Path SET StepsJson = REPLACE(StepsJson, @grade, @level) WHERE StepsJson LIKE '%' + @grade + '%';
                    FETCH NEXT FROM steps INTO @grade, @level;
                END;
                CLOSE steps;
                DEALLOCATE steps;
                UPDATE car.Path SET StepsJson = REPLACE(StepsJson, '"gradeId":', '"jobLevelId":') WHERE StepsJson LIKE '%"gradeId":%';

                -- Position links have no successor: they become empty designation links.
                UPDATE emp.Employee SET PositionId = NULL WHERE PositionId IS NOT NULL;
                UPDATE emp.EmploymentRecord SET PositionId = NULL WHERE PositionId IS NOT NULL;
                UPDATE car.Promotion SET ProposedPositionId = NULL WHERE ProposedPositionId IS NOT NULL;
                """);

            // ----- Drop positions, grades and profit centers --------------------------------
            migrationBuilder.DropForeignKey(
                name: "FK_Department_ProfitCenter_ProfitCenterId",
                schema: "org",
                table: "Department");

            migrationBuilder.DropTable(
                name: "Position",
                schema: "org");

            migrationBuilder.DropTable(
                name: "ProfitCenter",
                schema: "org");

            migrationBuilder.DropTable(
                name: "Grade",
                schema: "org");

            migrationBuilder.DropIndex(
                name: "IX_Employee_TenantId_PositionId",
                schema: "emp",
                table: "Employee");

            migrationBuilder.DropIndex(
                name: "IX_Department_ProfitCenterId",
                schema: "org",
                table: "Department");

            migrationBuilder.DropColumn(
                name: "PositionId",
                schema: "car",
                table: "SuccessionPlan");

            migrationBuilder.DropColumn(
                name: "PositionId",
                schema: "rec",
                table: "Requisition");

            migrationBuilder.DropColumn(
                name: "PositionId",
                schema: "wfp",
                table: "ManpowerRequest");

            migrationBuilder.DropColumn(
                name: "ProfitCenterId",
                schema: "org",
                table: "Department");

            // ----- Renames (values already converted above) -------------------------------
            migrationBuilder.RenameColumn(name: "GradeId", schema: "comp", table: "SalaryBand", newName: "JobLevelId");
            migrationBuilder.RenameColumn(name: "NewGradeId", schema: "comp", table: "CompensationRevision", newName: "NewJobLevelId");
            migrationBuilder.RenameColumn(name: "ProposedGradeId", schema: "car", table: "Promotion", newName: "ProposedJobLevelId");
            migrationBuilder.RenameColumn(name: "ProposedPositionId", schema: "car", table: "Promotion", newName: "ProposedDesignationId");
            migrationBuilder.RenameColumn(name: "GradeId", schema: "rec", table: "Offer", newName: "JobLevelId");
            migrationBuilder.RenameColumn(name: "GradeId", schema: "sep", table: "NoticeRule", newName: "JobLevelId");
            migrationBuilder.RenameColumn(name: "GradeId", schema: "emp", table: "EmploymentRecord", newName: "JobLevelId");
            migrationBuilder.RenameColumn(name: "PositionId", schema: "emp", table: "EmploymentRecord", newName: "DesignationId");
            migrationBuilder.RenameColumn(name: "GradeId", schema: "emp", table: "Employee", newName: "JobLevelId");
            migrationBuilder.RenameColumn(name: "PositionId", schema: "emp", table: "Employee", newName: "DesignationId");

            // ----- New columns ----------------------------------------------------------
            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                schema: "org",
                table: "Location",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                schema: "emp",
                table: "EmploymentRecord",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                schema: "emp",
                table: "Employee",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressCountryCode",
                schema: "org",
                table: "CostCenter",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                schema: "org",
                table: "CostCenter",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                schema: "org",
                table: "CostCenter",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "org",
                table: "CostCenter",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gstin",
                schema: "org",
                table: "CostCenter",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                schema: "org",
                table: "CostCenter",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalEntityId",
                schema: "org",
                table: "CostCenter",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pan",
                schema: "org",
                table: "CostCenter",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                schema: "org",
                table: "CostCenter",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateCode",
                schema: "org",
                table: "CostCenter",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Designation",
                schema: "org",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobLevelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Designation", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_Designation_JobLevel_JobLevelId",
                        column: x => x.JobLevelId,
                        principalSchema: "org",
                        principalTable: "JobLevel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // ----- Titles people hold become designations; everyone gets a cost center -----
            migrationBuilder.Sql("""
                INSERT INTO org.Designation (Id, JobLevelId, TenantId, IsActive, IsDeleted, CreatedBy, CreatedDate, Code, Name)
                SELECT NEWID(), NULL, d.TenantId, 1, 0, '00000000-0000-0000-0000-000000000000', SYSUTCDATETIME(),
                       CONCAT('D', RIGHT(CONCAT('000', ROW_NUMBER() OVER (PARTITION BY d.TenantId ORDER BY d.Name)), 3)), d.Name
                FROM (SELECT DISTINCT TenantId, LEFT(LTRIM(RTRIM(Designation)), 200) AS Name
                      FROM emp.Employee
                      WHERE IsDeleted = 0 AND Designation IS NOT NULL AND LTRIM(RTRIM(Designation)) <> '') d;

                -- Each designation's usual level: the one most of its holders are at.
                UPDATE ds SET ds.JobLevelId = (
                    SELECT TOP 1 e.JobLevelId FROM emp.Employee e
                    WHERE e.TenantId = ds.TenantId AND LTRIM(RTRIM(e.Designation)) = ds.Name AND e.JobLevelId IS NOT NULL
                    GROUP BY e.JobLevelId ORDER BY COUNT(*) DESC)
                FROM org.Designation ds;

                UPDATE e SET e.DesignationId = ds.Id
                FROM emp.Employee e JOIN org.Designation ds ON ds.TenantId = e.TenantId AND ds.Name = LTRIM(RTRIM(e.Designation));

                UPDATE r SET r.DesignationId = ds.Id
                FROM emp.EmploymentRecord r JOIN org.Designation ds ON ds.TenantId = r.TenantId AND ds.Name = LTRIM(RTRIM(r.Designation));

                -- Existing cost centers belong to the company when the tenant has just one.
                UPDATE cc SET cc.LegalEntityId = le.Id
                FROM org.CostCenter cc
                CROSS APPLY (SELECT TOP 1 x.Id FROM org.LegalEntity x WHERE x.TenantId = cc.TenantId AND x.IsDeleted = 0 ORDER BY x.CreatedDate) le
                WHERE cc.LegalEntityId IS NULL
                  AND (SELECT COUNT(*) FROM org.LegalEntity x WHERE x.TenantId = cc.TenantId AND x.IsDeleted = 0) = 1;

                -- A cost center already carrying a company's code becomes that company's default ...
                UPDATE cc SET cc.IsDefault = 1, cc.LegalEntityId = le.Id
                FROM org.CostCenter cc
                JOIN org.LegalEntity le ON le.TenantId = cc.TenantId AND le.Code = cc.Code AND le.IsDeleted = 0
                WHERE cc.IsDeleted = 0;

                -- ... and every other company gets one named after it.
                INSERT INTO org.CostCenter (Id, OwnerEmployeeId, TenantId, IsActive, IsDeleted, CreatedBy, CreatedDate, Code, Name,
                                            LegalEntityId, IsDefault, Gstin, Pan, AddressLine1, AddressLine2, City, StateCode, AddressCountryCode, PostalCode)
                SELECT NEWID(), NULL, le.TenantId, 1, 0, le.CreatedBy, SYSUTCDATETIME(), le.Code, le.Name,
                       le.Id, 1, le.Gstin, le.Pan, le.AddressLine1, le.AddressLine2, le.City, le.StateCode, le.AddressCountryCode, le.PostalCode
                FROM org.LegalEntity le
                WHERE le.IsDeleted = 0
                  AND NOT EXISTS (SELECT 1 FROM org.CostCenter cc WHERE cc.LegalEntityId = le.Id AND cc.IsDefault = 1 AND cc.IsDeleted = 0);

                -- People work under their company's default until an office gets its own.
                UPDATE e SET e.CostCenterId = (
                    SELECT TOP 1 cc.Id FROM org.CostCenter cc
                    WHERE cc.TenantId = e.TenantId AND cc.IsDefault = 1 AND cc.IsDeleted = 0
                      AND (e.LegalEntityId IS NULL OR cc.LegalEntityId = e.LegalEntityId)
                    ORDER BY cc.CreatedDate)
                FROM emp.Employee e;

                UPDATE r SET r.CostCenterId = (
                    SELECT TOP 1 cc.Id FROM org.CostCenter cc
                    WHERE cc.TenantId = r.TenantId AND cc.IsDefault = 1 AND cc.IsDeleted = 0
                      AND (r.LegalEntityId IS NULL OR cc.LegalEntityId = r.LegalEntityId)
                    ORDER BY cc.CreatedDate)
                FROM emp.EmploymentRecord r;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Location_CostCenterId",
                schema: "org",
                table: "Location",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenter_LegalEntityId",
                schema: "org",
                table: "CostCenter",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Designation_JobLevelId",
                schema: "org",
                table: "Designation",
                column: "JobLevelId");

            migrationBuilder.CreateIndex(
                name: "UX_Designation_TenantId_Code",
                schema: "org",
                table: "Designation",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Designation_TenantId_Id",
                schema: "org",
                table: "Designation",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.AddForeignKey(
                name: "FK_CostCenter_LegalEntity_LegalEntityId",
                schema: "org",
                table: "CostCenter",
                column: "LegalEntityId",
                principalSchema: "org",
                principalTable: "LegalEntity",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Location_CostCenter_CostCenterId",
                schema: "org",
                table: "Location",
                column: "CostCenterId",
                principalSchema: "org",
                principalTable: "CostCenter",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropForeignKey(
                name: "FK_CostCenter_LegalEntity_LegalEntityId",
                schema: "org",
                table: "CostCenter");

            migrationBuilder.DropForeignKey(
                name: "FK_Location_CostCenter_CostCenterId",
                schema: "org",
                table: "Location");

            migrationBuilder.DropTable(
                name: "Designation",
                schema: "org");

            migrationBuilder.DropIndex(
                name: "IX_Location_CostCenterId",
                schema: "org",
                table: "Location");

            migrationBuilder.DropIndex(
                name: "IX_CostCenter_LegalEntityId",
                schema: "org",
                table: "CostCenter");

            migrationBuilder.DropColumn(name: "CostCenterId", schema: "org", table: "Location");
            migrationBuilder.DropColumn(name: "CostCenterId", schema: "emp", table: "EmploymentRecord");
            migrationBuilder.DropColumn(name: "CostCenterId", schema: "emp", table: "Employee");
            migrationBuilder.DropColumn(name: "AddressCountryCode", schema: "org", table: "CostCenter");
            migrationBuilder.DropColumn(name: "AddressLine1", schema: "org", table: "CostCenter");
            migrationBuilder.DropColumn(name: "AddressLine2", schema: "org", table: "CostCenter");
            migrationBuilder.DropColumn(name: "City", schema: "org", table: "CostCenter");
            migrationBuilder.DropColumn(name: "Gstin", schema: "org", table: "CostCenter");
            migrationBuilder.DropColumn(name: "IsDefault", schema: "org", table: "CostCenter");
            migrationBuilder.DropColumn(name: "LegalEntityId", schema: "org", table: "CostCenter");
            migrationBuilder.DropColumn(name: "Pan", schema: "org", table: "CostCenter");
            migrationBuilder.DropColumn(name: "PostalCode", schema: "org", table: "CostCenter");
            migrationBuilder.DropColumn(name: "StateCode", schema: "org", table: "CostCenter");

            // Designation links had no position equivalent; levels stay in the grade columns.
            migrationBuilder.Sql("""
                UPDATE emp.Employee SET DesignationId = NULL;
                UPDATE emp.EmploymentRecord SET DesignationId = NULL;
                UPDATE car.Promotion SET ProposedDesignationId = NULL;
                UPDATE car.Path SET StepsJson = REPLACE(StepsJson, '"jobLevelId":', '"gradeId":') WHERE StepsJson LIKE '%"jobLevelId":%';
                """);

            migrationBuilder.RenameColumn(name: "JobLevelId", schema: "comp", table: "SalaryBand", newName: "GradeId");
            migrationBuilder.RenameColumn(name: "NewJobLevelId", schema: "comp", table: "CompensationRevision", newName: "NewGradeId");
            migrationBuilder.RenameColumn(name: "ProposedJobLevelId", schema: "car", table: "Promotion", newName: "ProposedGradeId");
            migrationBuilder.RenameColumn(name: "ProposedDesignationId", schema: "car", table: "Promotion", newName: "ProposedPositionId");
            migrationBuilder.RenameColumn(name: "JobLevelId", schema: "rec", table: "Offer", newName: "GradeId");
            migrationBuilder.RenameColumn(name: "JobLevelId", schema: "sep", table: "NoticeRule", newName: "GradeId");
            migrationBuilder.RenameColumn(name: "JobLevelId", schema: "emp", table: "EmploymentRecord", newName: "GradeId");
            migrationBuilder.RenameColumn(name: "DesignationId", schema: "emp", table: "EmploymentRecord", newName: "PositionId");
            migrationBuilder.RenameColumn(name: "JobLevelId", schema: "emp", table: "Employee", newName: "GradeId");
            migrationBuilder.RenameColumn(name: "DesignationId", schema: "emp", table: "Employee", newName: "PositionId");

            migrationBuilder.AddColumn<Guid>(
                name: "PositionId",
                schema: "car",
                table: "SuccessionPlan",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PositionId",
                schema: "rec",
                table: "Requisition",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PositionId",
                schema: "wfp",
                table: "ManpowerRequest",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProfitCenterId",
                schema: "org",
                table: "Department",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Grade",
                schema: "org",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grade", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "ProfitCenter",
                schema: "org",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OwnerEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfitCenter", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "Position",
                schema: "org",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    GradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Headcount = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsBudgeted = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReportsToPositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Position", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_Position_Department_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "org",
                        principalTable: "Department",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Position_Grade_GradeId",
                        column: x => x.GradeId,
                        principalSchema: "org",
                        principalTable: "Grade",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Position_Job_JobId",
                        column: x => x.JobId,
                        principalSchema: "org",
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Position_Location_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "org",
                        principalTable: "Location",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Position_Position_ReportsToPositionId",
                        column: x => x.ReportsToPositionId,
                        principalSchema: "org",
                        principalTable: "Position",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employee_TenantId_PositionId",
                schema: "emp",
                table: "Employee",
                columns: new[] { "TenantId", "PositionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Department_ProfitCenterId",
                schema: "org",
                table: "Department",
                column: "ProfitCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Grade_TenantId_Rank",
                schema: "org",
                table: "Grade",
                columns: new[] { "TenantId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "UX_Grade_TenantId_Code",
                schema: "org",
                table: "Grade",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Grade_TenantId_Id",
                schema: "org",
                table: "Grade",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(name: "IX_Position_DepartmentId", schema: "org", table: "Position", column: "DepartmentId");
            migrationBuilder.CreateIndex(name: "IX_Position_GradeId", schema: "org", table: "Position", column: "GradeId");
            migrationBuilder.CreateIndex(name: "IX_Position_JobId", schema: "org", table: "Position", column: "JobId");
            migrationBuilder.CreateIndex(name: "IX_Position_LocationId", schema: "org", table: "Position", column: "LocationId");
            migrationBuilder.CreateIndex(name: "IX_Position_ReportsToPositionId", schema: "org", table: "Position", column: "ReportsToPositionId");
            migrationBuilder.CreateIndex(name: "IX_Position_TenantId_DepartmentId", schema: "org", table: "Position", columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "UX_Position_TenantId_Code",
                schema: "org",
                table: "Position",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Position_TenantId_Id",
                schema: "org",
                table: "Position",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_ProfitCenter_TenantId_Code",
                schema: "org",
                table: "ProfitCenter",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_ProfitCenter_TenantId_Id",
                schema: "org",
                table: "ProfitCenter",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.AddForeignKey(
                name: "FK_Department_ProfitCenter_ProfitCenterId",
                schema: "org",
                table: "Department",
                column: "ProfitCenterId",
                principalSchema: "org",
                principalTable: "ProfitCenter",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.SyncTenantRowLevelSecurity(new RemoveDivisionsAddFunctions().TargetModel);
        }
    }
}
