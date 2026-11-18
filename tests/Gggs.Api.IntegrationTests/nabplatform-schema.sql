IF OBJECT_ID(N'[platform].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'platform') IS NULL EXEC(N'CREATE SCHEMA [platform];');
    CREATE TABLE [platform].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF SCHEMA_ID(N'platform') IS NULL EXEC(N'CREATE SCHEMA [platform];');

IF SCHEMA_ID(N'hr') IS NULL EXEC(N'CREATE SCHEMA [hr];');

CREATE TABLE [hr].[JobHealth] (
    [Id] int NOT NULL,
    [MeasuredDate] datetime2(3) NOT NULL,
    [ServersJson] nvarchar(max) NOT NULL,
    [RecurringJson] nvarchar(max) NOT NULL,
    [FailedJson] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_JobHealth] PRIMARY KEY ([Id])
);

CREATE TABLE [platform].[Plan] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(32) NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [Edition] tinyint NOT NULL,
    [MaxEmployees] int NULL,
    [IsPubliclyAvailable] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedBy] uniqueidentifier NOT NULL,
    [CreatedDate] datetime2(3) NOT NULL,
    [ModifiedBy] uniqueidentifier NULL,
    [ModifiedDate] datetime2(3) NULL,
    [FeatureCodes] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Plan] PRIMARY KEY ([Id])
);

CREATE TABLE [platform].[PlatformAuditEntry] (
    [Id] uniqueidentifier NOT NULL,
    [OccurredDate] datetime2(3) NOT NULL,
    [StaffId] uniqueidentifier NOT NULL,
    [StaffName] nvarchar(200) NOT NULL,
    [TenantId] uniqueidentifier NULL,
    [TenantCode] nvarchar(32) NULL,
    [Action] nvarchar(64) NOT NULL,
    [Summary] nvarchar(500) NOT NULL,
    [Reason] nvarchar(500) NULL,
    CONSTRAINT [PK_PlatformAuditEntry] PRIMARY KEY ([Id])
);
DECLARE @description AS sql_variant;
SET @description = N'Append-only. What platform staff did in the console.';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'platform', 'TABLE', N'PlatformAuditEntry';

CREATE TABLE [platform].[PlatformRole] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [NormalizedName] nvarchar(100) NOT NULL,
    [Description] nvarchar(500) NULL,
    [IsSystem] bit NOT NULL,
    [TemplateCode] nvarchar(50) NULL,
    [CreatedBy] uniqueidentifier NOT NULL,
    [CreatedDate] datetime2(3) NOT NULL,
    [ModifiedBy] uniqueidentifier NULL,
    [ModifiedDate] datetime2(3) NULL,
    [RowVersion] rowversion NULL,
    [PermissionCodes] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_PlatformRole] PRIMARY KEY ([Id])
);

CREATE TABLE [platform].[PlatformRoleAssignment] (
    [Id] uniqueidentifier NOT NULL,
    [PlatformStaffId] uniqueidentifier NOT NULL,
    [PlatformRoleId] uniqueidentifier NOT NULL,
    [CreatedBy] uniqueidentifier NOT NULL,
    [CreatedDate] datetime2(3) NOT NULL,
    [ModifiedBy] uniqueidentifier NULL,
    [ModifiedDate] datetime2(3) NULL,
    CONSTRAINT [PK_PlatformRoleAssignment] PRIMARY KEY ([Id])
);

CREATE TABLE [platform].[PlatformStaff] (
    [Id] uniqueidentifier NOT NULL,
    [Email] nvarchar(256) NOT NULL,
    [NormalizedEmail] nvarchar(256) NOT NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [Status] tinyint NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(64) NOT NULL,
    [AccessFailedCount] int NOT NULL,
    [LockoutEndDate] datetime2(3) NULL,
    [PasswordChangedDate] datetime2(3) NULL,
    [LastLoginDate] datetime2(3) NULL,
    [PermissionVersion] int NOT NULL,
    [InvitationTokenHash] nvarchar(max) NULL,
    [InvitationTokenExpiresDate] datetime2(3) NULL,
    [CreatedBy] uniqueidentifier NOT NULL,
    [CreatedDate] datetime2(3) NOT NULL,
    [ModifiedBy] uniqueidentifier NULL,
    [ModifiedDate] datetime2(3) NULL,
    [RowVersion] rowversion NULL,
    [PasswordHistory] nvarchar(max) NULL,
    CONSTRAINT [PK_PlatformStaff] PRIMARY KEY ([Id])
);

CREATE TABLE [platform].[ProvisioningRequest] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NULL,
    [Kind] tinyint NOT NULL,
    [Email] nvarchar(256) NULL,
    [DisplayName] nvarchar(200) NULL,
    [JobId] nvarchar(100) NULL,
    [Status] tinyint NOT NULL,
    [Result] nvarchar(500) NULL,
    [RequestedDate] datetime2(3) NOT NULL,
    [RequestedBy] nvarchar(200) NOT NULL,
    [CompletedDate] datetime2(3) NULL,
    CONSTRAINT [PK_ProvisioningRequest] PRIMARY KEY ([Id])
);
DECLARE @description1 AS sql_variant;
SET @description1 = N'Asked for by platform staff, carried out by the HR worker, which sets Status, Result and CompletedDate.';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', N'platform', 'TABLE', N'ProvisioningRequest';

CREATE TABLE [platform].[Tenant] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(32) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [LegalName] nvarchar(300) NULL,
    [Status] tinyint NOT NULL,
    [IsolationTier] tinyint NOT NULL,
    [DataRegion] nvarchar(32) NOT NULL,
    [CountryCode] nchar(2) NOT NULL,
    [DefaultTimeZone] nvarchar(64) NOT NULL,
    [DefaultCurrency] nchar(3) NOT NULL,
    [DefaultLocale] nvarchar(10) NOT NULL,
    [ActivatedDate] datetime2(3) NULL,
    [SuspendedDate] datetime2(3) NULL,
    [SuspensionReason] nvarchar(500) NULL,
    [PurgeEligibleDate] datetime2(3) NULL,
    [CreatedBy] uniqueidentifier NOT NULL,
    [CreatedDate] datetime2(3) NOT NULL,
    [ModifiedBy] uniqueidentifier NULL,
    [ModifiedDate] datetime2(3) NULL,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_Tenant] PRIMARY KEY ([Id])
);

CREATE TABLE [hr].[TenantAdministrator] (
    [TenantId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Email] nvarchar(256) NOT NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [LastLoginDate] datetime2(3) NULL,
    [InvitationExpiresDate] datetime2(3) NULL,
    [MeasuredDate] datetime2(3) NOT NULL,
    CONSTRAINT [PK_TenantAdministrator] PRIMARY KEY ([TenantId], [UserId])
);

CREATE TABLE [hr].[TenantSeedState] (
    [TenantId] uniqueidentifier NOT NULL,
    [SeededDate] datetime2(3) NOT NULL,
    CONSTRAINT [PK_TenantSeedState] PRIMARY KEY ([TenantId])
);

CREATE TABLE [hr].[TenantUsage] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [MeasuredDate] datetime2(3) NOT NULL,
    [Employees] int NOT NULL,
    [Users] int NOT NULL,
    [SignedInToday] int NOT NULL,
    [LastSignIn] datetime2(3) NULL,
    [EmailsSentToday] int NOT NULL,
    [EmailsFailedToday] int NOT NULL,
    [EmailsLoggedToday] int NOT NULL,
    [EmailChannel] nvarchar(200) NULL,
    [SignIn] nvarchar(200) NULL,
    CONSTRAINT [PK_TenantUsage] PRIMARY KEY ([Id])
);

CREATE TABLE [platform].[DedicatedDatabase] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [Server] nvarchar(256) NOT NULL,
    [DatabaseName] nvarchar(128) NOT NULL,
    [Authentication] tinyint NOT NULL,
    [UserName] nvarchar(128) NULL,
    [PasswordCiphertext] nvarchar(2000) NULL,
    [Encryption] tinyint NOT NULL,
    [PasswordChangedDate] datetime2(3) NULL,
    [PasswordChangedBy] nvarchar(200) NULL,
    [UpdatedDate] datetime2(3) NOT NULL,
    [UpdatedBy] nvarchar(200) NOT NULL,
    [LastTestedDate] datetime2(3) NULL,
    [LastTestSucceeded] bit NULL,
    [LastTestResult] nvarchar(500) NULL,
    CONSTRAINT [PK_DedicatedDatabase] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DedicatedDatabase_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [platform].[Tenant] ([Id]) ON DELETE CASCADE
);
DECLARE @description2 AS sql_variant;
SET @description2 = N'A dedicated database''s connection details, kept by the platform console. The password is ciphertext only.';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', N'platform', 'TABLE', N'DedicatedDatabase';

CREATE TABLE [platform].[Subscription] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [PlanId] uniqueidentifier NOT NULL,
    [Status] tinyint NOT NULL,
    [StartDate] date NOT NULL,
    [EndDate] date NULL,
    [TrialEndsOn] date NULL,
    [SeatLimit] int NULL,
    [BillingReference] nvarchar(100) NULL,
    [RenewsOn] date NULL,
    [CreatedBy] uniqueidentifier NOT NULL,
    [CreatedDate] datetime2(3) NOT NULL,
    [ModifiedBy] uniqueidentifier NULL,
    [ModifiedDate] datetime2(3) NULL,
    [RowVersion] rowversion NULL,
    [FeatureOverrides] nvarchar(max) NULL,
    CONSTRAINT [PK_Subscription] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Subscription_Plan_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [platform].[Plan] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Subscription_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [platform].[Tenant] ([Id]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [UX_DedicatedDatabase_TenantId] ON [platform].[DedicatedDatabase] ([TenantId]);

CREATE UNIQUE INDEX [UX_Plan_Code] ON [platform].[Plan] ([Code]);

CREATE INDEX [IX_PlatformAuditEntry_OccurredDate] ON [platform].[PlatformAuditEntry] ([OccurredDate]);

CREATE INDEX [IX_PlatformAuditEntry_TenantId_OccurredDate] ON [platform].[PlatformAuditEntry] ([TenantId], [OccurredDate]);

CREATE UNIQUE INDEX [UX_PlatformRole_NormalizedName] ON [platform].[PlatformRole] ([NormalizedName]);

CREATE INDEX [IX_PlatformRoleAssignment_PlatformRoleId] ON [platform].[PlatformRoleAssignment] ([PlatformRoleId]);

CREATE INDEX [IX_PlatformRoleAssignment_PlatformStaffId] ON [platform].[PlatformRoleAssignment] ([PlatformStaffId]);

CREATE UNIQUE INDEX [UX_PlatformRoleAssignment_Assignment] ON [platform].[PlatformRoleAssignment] ([PlatformStaffId], [PlatformRoleId]);

CREATE UNIQUE INDEX [UX_PlatformStaff_NormalizedEmail] ON [platform].[PlatformStaff] ([NormalizedEmail]);

CREATE INDEX [IX_ProvisioningRequest_Status] ON [platform].[ProvisioningRequest] ([Status], [RequestedDate]);

CREATE INDEX [IX_ProvisioningRequest_TenantId] ON [platform].[ProvisioningRequest] ([TenantId]);

CREATE INDEX [IX_Subscription_PlanId] ON [platform].[Subscription] ([PlanId]);

CREATE UNIQUE INDEX [UX_Subscription_TenantId_Live] ON [platform].[Subscription] ([TenantId]) WHERE [Status] IN (1, 2, 3);

CREATE INDEX [IX_Tenant_Status] ON [platform].[Tenant] ([Status]);

CREATE UNIQUE INDEX [UX_Tenant_Code] ON [platform].[Tenant] ([Code]);

CREATE UNIQUE INDEX [UX_TenantUsage_TenantId] ON [hr].[TenantUsage] ([TenantId]);

INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261118075747_InitialCreate', N'10.0.12');

COMMIT;
GO

