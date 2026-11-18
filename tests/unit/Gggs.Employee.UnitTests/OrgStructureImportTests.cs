using Gggs.Modules.Organization.Application;
using Gggs.Modules.Organization.Application.StructureImport;
using Gggs.Modules.Organization.Domain;
using Shouldly;

namespace Gggs.Employee.UnitTests;

/// <summary>Reading an org-structure file, planning it against what exists, and role designations.</summary>
public sealed class OrgStructureImportTests
{
    private static readonly ExistingUnit[] Levels =
    [
        new(Guid.NewGuid(), "L1", "Associate", null, true),
        new(Guid.NewGuid(), "L2", "Senior associate", null, true),
        new(Guid.NewGuid(), "L3", "Lead", null, true),
    ];

    private static StructureSnapshot Empty() => new([], [], Levels, []);

    private const string Markdown = """
        # Department Role Mapping

        Total records: **6**

        | Department | FunctionName | FunctionCode | RoleName | RoleCode | RoleLevelCode |
        | --- | --- | --- | --- | --- | --- |
        | Technology Services | UX Practice | UX | Business Analyst | TS-BA | L1 |
        | Technology Services | UX Practice | UX | Business Analyst | TS-BA | L2 |
        | Technology Services | UX Practice | UX | Business Analyst | TS-BA | NULL |
        | Technology Services | UX Practice | UX | Business Analyst | TS-BA | NULL |
        | Quality | NULL | NULL | QA Lead | QUAL-QAL | L1 |
        | IT Support | NULL | NULL | NULL | NULL | NULL |
        """;

    [Fact]
    public void AMarkdownTable_IsRead_WithNullAsNone()
    {
        StructureParseResult parsed = StructureImportParser.Parse(Markdown);

        parsed.Errors.ShouldBeEmpty();
        parsed.Rows.Count.ShouldBe(6);
        parsed.Rows[0].ShouldBe(new StructureRow(parsed.Rows[0].Line, "Technology Services", null, "UX Practice", "UX", "Business Analyst", "TS-BA", "L1"));
        parsed.Rows[2].LevelCode.ShouldBeNull();
        parsed.Rows[4].FunctionCode.ShouldBeNull();
    }

    [Fact]
    public void ACsv_IsRead_WithQuotedCommas_AndAnOptionalDepartmentCode()
    {
        StructureParseResult parsed = StructureImportParser.Parse(
            "DepartmentCode,Department,FunctionName,FunctionCode,RoleName,RoleCode,RoleLevelCode\r\n" +
            "D-SNM,Sales & Marketing,Sales,SAL,\"Sales, Inside\",SNM-IS,L1\r\n");

        parsed.Errors.ShouldBeEmpty();
        parsed.Rows.Single().ShouldBe(new StructureRow(2, "Sales & Marketing", "D-SNM", "Sales", "SAL", "Sales, Inside", "SNM-IS", "L1"));
    }

    [Fact]
    public void AFileWithoutTheColumns_IsRefused()
    {
        StructureParseResult parsed = StructureImportParser.Parse("Name,Code\nA,B\n");

        parsed.Errors.Single().Code.ShouldBe("IMPORT_COLUMNS_MISSING");
    }

    [Fact]
    public void ThePlan_ReadsRepeatsOnce_SkipsEmptyDepartments_AndPutsARoleWithoutAFunctionOnItsDepartment()
    {
        StructurePlan plan = StructureImportPlanner.Plan(StructureImportParser.Parse(Markdown).Rows, Empty());

        plan.Errors.ShouldBeEmpty();
        plan.DuplicateRows.ShouldBe(1);
        plan.Departments.Select(unit => unit.Name).ShouldBe(["Quality", "Technology Services"]);
        plan.Departments.ShouldAllBe(unit => unit.Change == PlannedChange.Create && unit.Code.StartsWith("D-"));
        plan.Functions.Single().ShouldBe(new PlannedFunction("UX", "UX Practice", "D-TECSER", PlannedChange.Create, null, null));
        plan.Roles.Single(role => role.Code == "QUAL-QAL").DepartmentCode.ShouldBe("D-QUA");
        plan.NewDesignations.ShouldBe(6); // two roles × three active levels
        plan.Warnings.ShouldContain(issue => issue.Code == "IMPORT_DEPARTMENT_EMPTY" && issue.Message.StartsWith("IT Support"));
    }

    [Fact]
    public void BadCodes_UnknownLevels_AndClashes_AreErrors()
    {
        StructureRow[] rows =
        [
            new(2, "Quality", null, null, null, "QA Lead", "DS_DC_TS_QA_QA Lead", "L1"),
            new(3, "Finance", "FIN", "Finance", "FIN", "Analyst", "FIN-AN", "L1"),
            new(4, "HR", null, "General", "GH", "Partner", "HR-PP", "L9"),
        ];

        StructurePlan plan = StructureImportPlanner.Plan(rows, Empty());

        plan.CanApply.ShouldBeFalse();
        plan.Errors.ShouldContain(issue => issue.Line == 2 && issue.Code == "IMPORT_CODE_INVALID");
        plan.Errors.ShouldContain(issue => issue.Line == 3 && issue.Code == "IMPORT_CODE_CLASH");
        plan.Errors.ShouldContain(issue => issue.Line == 4 && issue.Code == "IMPORT_LEVEL_UNKNOWN");
    }

    [Fact]
    public void RunningTheSameFileAgain_ChangesNothing()
    {
        Guid tsId = Guid.NewGuid();
        Guid qualityId = Guid.NewGuid();
        var existing = new StructureSnapshot(
            [
                new(tsId, "D-TECSER", "Technology Services", null, true),
                new(qualityId, "D-QUA", "Quality", null, true),
                new(Guid.NewGuid(), "UX", "UX Practice", tsId, true),
            ],
            [
                new(Guid.NewGuid(), "TS-BA", "Business Analyst", tsId, true),
                new(Guid.NewGuid(), "QUAL-QAL", "QA Lead", qualityId, true),
            ],
            Levels,
            ["TS-BA-L1", "TS-BA-L2", "TS-BA-L3", "QUAL-QAL-L1", "QUAL-QAL-L2", "QUAL-QAL-L3"]);

        StructurePlan plan = StructureImportPlanner.Plan(StructureImportParser.Parse(Markdown).Rows, existing);

        plan.Errors.ShouldBeEmpty();
        plan.Departments.ShouldAllBe(unit => unit.Change == PlannedChange.Unchanged);
        plan.Functions.ShouldAllBe(unit => unit.Change == PlannedChange.Unchanged);
        plan.Roles.ShouldAllBe(unit => unit.Change == PlannedChange.Unchanged);
        plan.NewDesignations.ShouldBe(0);
    }

    [Fact]
    public void TheCompanyFile_PlansFourteenDepartments_ThirtyNineFunctions_AndFiftySixRoles()
    {
        string csv = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "DevSeed", "org-structure.csv"));

        StructurePlan plan = StructureImportPlanner.Plan(StructureImportParser.Parse(csv).Rows, Empty());

        plan.Errors.ShouldBeEmpty();
        plan.Departments.Count.ShouldBe(14);
        plan.Functions.Count.ShouldBe(39);
        plan.Roles.Count.ShouldBe(56);
        plan.NewDesignations.ShouldBe(168);
    }

    [Fact]
    public void ARole_GetsADesignationPerActiveLevel_ThatFollowsItsNameAndCode()
    {
        Role role = Role.Create("TS-BA", "Business Analyst");
        Level[] levels = [Level.Create("L1", "Associate", 1), Level.Create("L2", "Senior associate", 2)];
        var designations = new List<Designation>();

        RoleDesignations.Sync([role], levels, designations, _ => { }).ShouldBe(2);
        designations.Select(row => (row.Code, row.Name)).ShouldBe([("TS-BA-L1", "Business Analyst L1"), ("TS-BA-L2", "Business Analyst L2")]);

        role.Rename("Business Analyst (Tech)");
        role.ChangeCode("TS-BAT");
        RoleDesignations.Sync([role], levels, designations, _ => { }).ShouldBe(0);
        designations.Select(row => (row.Code, row.Name)).ShouldBe([("TS-BAT-L1", "Business Analyst (Tech) L1"), ("TS-BAT-L2", "Business Analyst (Tech) L2")]);

        // Retiring a level, or the role, switches its designations off.
        RoleDesignations.Sync([role], [levels[0]], designations, _ => { });
        designations.Select(row => row.IsActive).ShouldBe([true, false]);
        role.Deactivate();
        RoleDesignations.Sync([role], [levels[0]], designations, _ => { });
        designations.ShouldAllBe(row => !row.IsActive);
    }

    [Fact]
    public void AFreeStandingDesignation_WithTheRolesCode_IsTakenOver_NotDuplicated()
    {
        Role role = Role.Create("TS-BA", "Business Analyst");
        Level level = Level.Create("L1", "Associate", 1);
        var designations = new List<Designation> { Designation.Create("TS-BA-L1", "Junior BA", null) };

        RoleDesignations.Sync([role], [level], designations, _ => { }).ShouldBe(0);

        designations.Single().RoleId.ShouldBe(role.Id);
        designations.Single().Name.ShouldBe("Business Analyst L1");
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tools", "DevSeed", "org-structure.csv")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root (with tools/DevSeed/org-structure.csv) was not found.");
    }
}
