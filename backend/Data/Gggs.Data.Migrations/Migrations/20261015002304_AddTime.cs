using System;
using Gggs.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gggs.Data.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "att");

            migrationBuilder.EnsureSchema(
                name: "lv");

            migrationBuilder.EnsureSchema(
                name: "shf");

            migrationBuilder.CreateTable(
                name: "AttendanceDay",
                schema: "att",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FirstIn = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastOut = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    WorkedMinutes = table.Column<int>(type: "int", nullable: false),
                    LateMinutes = table.Column<int>(type: "int", nullable: false),
                    EarlyLeaveMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    PunchCount = table.Column<int>(type: "int", nullable: false),
                    WorkedOnDayOff = table.Column<bool>(type: "bit", nullable: false),
                    IsRegularized = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDay", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceDevice",
                schema: "att",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastSeenDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDevice", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "AttendancePunch",
                schema: "att",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PunchedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePunch", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "HolidayCalendar",
                schema: "lv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    LocationIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolidayCalendar", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "LeaveType",
                schema: "lv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    AllowHalfDay = table.Column<bool>(type: "bit", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveType", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "RegularizationRequest",
                schema: "att",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CheckIn = table.Column<TimeOnly>(type: "time", nullable: true),
                    CheckOut = table.Column<TimeOnly>(type: "time", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegularizationRequest", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "Shift",
                schema: "shf",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    BreakMinutes = table.Column<int>(type: "int", nullable: false),
                    GraceMinutes = table.Column<int>(type: "int", nullable: false),
                    HalfDayMinutes = table.Column<int>(type: "int", nullable: false),
                    FullDayMinutes = table.Column<int>(type: "int", nullable: false),
                    WeeklyOffMask = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shift", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "ShiftSwapRequest",
                schema: "shf",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CounterpartEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftSwapRequest", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "Holiday",
                schema: "lv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsOptional = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holiday", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_Holiday_HolidayCalendar_CalendarId",
                        column: x => x.CalendarId,
                        principalSchema: "lv",
                        principalTable: "HolidayCalendar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveLedgerEntry",
                schema: "lv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Days = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PeriodKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveLedgerEntry", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_LeaveLedgerEntry_LeaveType_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalSchema: "lv",
                        principalTable: "LeaveType",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeavePolicy",
                schema: "lv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmploymentTypeCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AnnualEntitlement = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Accrual = table.Column<byte>(type: "tinyint", nullable: false),
                    CarryForwardLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AllowDuringProbation = table.Column<bool>(type: "bit", nullable: false),
                    MaxNegativeBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxConsecutiveDays = table.Column<int>(type: "int", nullable: true),
                    MinNoticeDays = table.Column<int>(type: "int", nullable: false),
                    CountSandwichedDays = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicy", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_LeavePolicy_LeaveType_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalSchema: "lv",
                        principalTable: "LeaveType",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveRequest",
                schema: "lv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartsSecondHalf = table.Column<bool>(type: "bit", nullable: false),
                    EndsFirstHalf = table.Column<bool>(type: "bit", nullable: false),
                    Days = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppliedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequest", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_LeaveRequest_LeaveType_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalSchema: "lv",
                        principalTable: "LeaveType",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RosterOverride",
                schema: "shf",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterOverride", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_RosterOverride_Shift_ShiftId",
                        column: x => x.ShiftId,
                        principalSchema: "shf",
                        principalTable: "Shift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftAssignment",
                schema: "shf",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftAssignment", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_ShiftAssignment_Shift_ShiftId",
                        column: x => x.ShiftId,
                        principalSchema: "shf",
                        principalTable: "Shift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDay_TenantId_Date_Status",
                schema: "att",
                table: "AttendanceDay",
                columns: new[] { "TenantId", "Date", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceDay_TenantId_Employee_Date",
                schema: "att",
                table: "AttendanceDay",
                columns: new[] { "TenantId", "EmployeeId", "Date" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceDay_TenantId_Id",
                schema: "att",
                table: "AttendanceDay",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceDevice_TenantId_Code",
                schema: "att",
                table: "AttendanceDevice",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_AttendanceDevice_TenantId_Id",
                schema: "att",
                table: "AttendanceDevice",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePunch_TenantId_Employee_At",
                schema: "att",
                table: "AttendancePunch",
                columns: new[] { "TenantId", "EmployeeId", "PunchedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_AttendancePunch_TenantId_Id",
                schema: "att",
                table: "AttendancePunch",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Holiday_CalendarId",
                schema: "lv",
                table: "Holiday",
                column: "CalendarId");

            migrationBuilder.CreateIndex(
                name: "UX_Holiday_TenantId_Calendar_Date",
                schema: "lv",
                table: "Holiday",
                columns: new[] { "TenantId", "CalendarId", "Date" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Holiday_TenantId_Id",
                schema: "lv",
                table: "Holiday",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_HolidayCalendar_TenantId_Code",
                schema: "lv",
                table: "HolidayCalendar",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_HolidayCalendar_TenantId_Id",
                schema: "lv",
                table: "HolidayCalendar",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveLedgerEntry_LeaveTypeId",
                schema: "lv",
                table: "LeaveLedgerEntry",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveLedgerEntry_TenantId_Employee_Type",
                schema: "lv",
                table: "LeaveLedgerEntry",
                columns: new[] { "TenantId", "EmployeeId", "LeaveTypeId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "UX_LeaveLedgerEntry_TenantId_Employee_Type_Period",
                schema: "lv",
                table: "LeaveLedgerEntry",
                columns: new[] { "TenantId", "EmployeeId", "LeaveTypeId", "PeriodKey" },
                unique: true,
                filter: "[PeriodKey] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_LeaveLedgerEntry_TenantId_Id",
                schema: "lv",
                table: "LeaveLedgerEntry",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicy_LeaveTypeId",
                schema: "lv",
                table: "LeavePolicy",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicy_TenantId_Type",
                schema: "lv",
                table: "LeavePolicy",
                columns: new[] { "TenantId", "LeaveTypeId", "EmploymentTypeCode" });

            migrationBuilder.CreateIndex(
                name: "UX_LeavePolicy_TenantId_Id",
                schema: "lv",
                table: "LeavePolicy",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequest_LeaveTypeId",
                schema: "lv",
                table: "LeaveRequest",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequest_TenantId_Employee_Start",
                schema: "lv",
                table: "LeaveRequest",
                columns: new[] { "TenantId", "EmployeeId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequest_TenantId_Status_Start",
                schema: "lv",
                table: "LeaveRequest",
                columns: new[] { "TenantId", "Status", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "UX_LeaveRequest_TenantId_Id",
                schema: "lv",
                table: "LeaveRequest",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_LeaveType_TenantId_Code",
                schema: "lv",
                table: "LeaveType",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_LeaveType_TenantId_Id",
                schema: "lv",
                table: "LeaveType",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_RegularizationRequest_TenantId_Employee_Date",
                schema: "att",
                table: "RegularizationRequest",
                columns: new[] { "TenantId", "EmployeeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_RegularizationRequest_TenantId_Status",
                schema: "att",
                table: "RegularizationRequest",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_RegularizationRequest_TenantId_Id",
                schema: "att",
                table: "RegularizationRequest",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterOverride_ShiftId",
                schema: "shf",
                table: "RosterOverride",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "UX_RosterOverride_TenantId_Employee_Date",
                schema: "shf",
                table: "RosterOverride",
                columns: new[] { "TenantId", "EmployeeId", "Date" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RosterOverride_TenantId_Id",
                schema: "shf",
                table: "RosterOverride",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UX_Shift_TenantId_Code",
                schema: "shf",
                table: "Shift",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Shift_TenantId_Default",
                schema: "shf",
                table: "Shift",
                columns: new[] { "TenantId", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Shift_TenantId_Id",
                schema: "shf",
                table: "Shift",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignment_ShiftId",
                schema: "shf",
                table: "ShiftAssignment",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignment_TenantId_Employee",
                schema: "shf",
                table: "ShiftAssignment",
                columns: new[] { "TenantId", "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "UX_ShiftAssignment_TenantId_Id",
                schema: "shf",
                table: "ShiftAssignment",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSwapRequest_TenantId_Requester",
                schema: "shf",
                table: "ShiftSwapRequest",
                columns: new[] { "TenantId", "RequesterEmployeeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "UX_ShiftSwapRequest_TenantId_Id",
                schema: "shf",
                table: "ShiftSwapRequest",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            // New tenant-owned tables (shf, lv, att): the policy is re-derived from the model.
            migrationBuilder.SyncTenantRowLevelSecurity(TargetModel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The policy names these tables, so it goes first; it is restored below without them.
            migrationBuilder.DisableTenantRowLevelSecurity();

            migrationBuilder.DropTable(
                name: "AttendanceDay",
                schema: "att");

            migrationBuilder.DropTable(
                name: "AttendanceDevice",
                schema: "att");

            migrationBuilder.DropTable(
                name: "AttendancePunch",
                schema: "att");

            migrationBuilder.DropTable(
                name: "Holiday",
                schema: "lv");

            migrationBuilder.DropTable(
                name: "LeaveLedgerEntry",
                schema: "lv");

            migrationBuilder.DropTable(
                name: "LeavePolicy",
                schema: "lv");

            migrationBuilder.DropTable(
                name: "LeaveRequest",
                schema: "lv");

            migrationBuilder.DropTable(
                name: "RegularizationRequest",
                schema: "att");

            migrationBuilder.DropTable(
                name: "RosterOverride",
                schema: "shf");

            migrationBuilder.DropTable(
                name: "ShiftAssignment",
                schema: "shf");

            migrationBuilder.DropTable(
                name: "ShiftSwapRequest",
                schema: "shf");

            migrationBuilder.DropTable(
                name: "HolidayCalendar",
                schema: "lv");

            migrationBuilder.DropTable(
                name: "LeaveType",
                schema: "lv");

            migrationBuilder.DropTable(
                name: "Shift",
                schema: "shf");

            // The policy as it stood before these tables existed.
            migrationBuilder.SyncTenantRowLevelSecurity(new AddPlatformServices().TargetModel);
        }
    }
}
