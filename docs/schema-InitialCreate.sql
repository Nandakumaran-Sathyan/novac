IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [LoanApplications] (
    [Id] int NOT NULL IDENTITY,
    [ApplicantName] nvarchar(100) NOT NULL,
    [MonthlyIncome] decimal(18,2) NULL,
    [ExistingMonthlyDebt] decimal(18,2) NULL,
    [LoanAmount] decimal(18,2) NOT NULL,
    [AnnualInterestRate] decimal(5,2) NOT NULL,
    [TenureMonths] int NOT NULL,
    [EmploymentType] nvarchar(20) NULL,
    [Status] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_LoanApplications] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_LoanApplications_AnnualInterestRate_Range] CHECK ([AnnualInterestRate] BETWEEN 0 AND 100),
    CONSTRAINT [CK_LoanApplications_ExistingMonthlyDebt_NonNegative] CHECK ([ExistingMonthlyDebt] IS NULL OR [ExistingMonthlyDebt] >= 0),
    CONSTRAINT [CK_LoanApplications_LoanAmount_Positive] CHECK ([LoanAmount] > 0),
    CONSTRAINT [CK_LoanApplications_MonthlyIncome_NonNegative] CHECK ([MonthlyIncome] IS NULL OR [MonthlyIncome] >= 0),
    CONSTRAINT [CK_LoanApplications_TenureMonths_Range] CHECK ([TenureMonths] BETWEEN 1 AND 600)
);

CREATE TABLE [RuleAssessments] (
    [Id] int NOT NULL IDENTITY,
    [ApplicationId] int NOT NULL,
    [Emi] decimal(18,2) NULL,
    [DebtToIncomeRatio] decimal(18,4) NULL,
    [FlagsJson] nvarchar(max) NOT NULL,
    [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_RuleAssessments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_RuleAssessments_FlagsJson_IsJson] CHECK (ISJSON([FlagsJson]) = 1),
    CONSTRAINT [FK_RuleAssessments_LoanApplications_ApplicationId] FOREIGN KEY ([ApplicationId]) REFERENCES [LoanApplications] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_LoanApplications_Status_CreatedAt] ON [LoanApplications] ([Status], [CreatedAt]);

CREATE INDEX [IX_RuleAssessments_ApplicationId_CreatedAt] ON [RuleAssessments] ([ApplicationId], [CreatedAt]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261025132345_InitialCreate', N'10.0.13');

COMMIT;
GO

