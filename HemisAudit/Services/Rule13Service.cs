using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule13Service : IRule13Service
    {
        private const int SqlCommandTimeoutSeconds = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;
        private const int BrowserPreviewRowLimit = 10;
        private const string ScopeCode = "ALL";
        private const string ScopeTitle = "Full Population";
        private const string ScopeDescription = "100% of qualifying CESM qualifications where _006 <> 'ZZZZZZ'.";
        private static readonly string[] PartOrder = [ScopeCode];
        private static readonly string[] DefaultPgTypes = [];
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule13Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, "master", driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT name FROM sys.databases WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name;";
                await using var reader = await cmd.ExecuteReaderAsync();

                var items = new List<string>();
                while (await reader.ReadAsync())
                    items.Add(reader.GetString(0));

                return new DatabaseListResult { Success = true, Databases = items };
            }
            catch (Exception ex)
            {
                return new DatabaseListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule13TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME;";
                await using var reader = await cmd.ExecuteReaderAsync();

                var tables = new List<string>();
                while (await reader.ReadAsync())
                {
                    var tableName = NormalizeObjectName(reader.GetString(0));
                    if (!string.IsNullOrWhiteSpace(tableName) &&
                        !tables.Contains(tableName, StringComparer.OrdinalIgnoreCase))
                    {
                        tables.Add(tableName);
                    }
                }

                return new Rule13TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = FindFirst(tables, ["dbo_CESM", "CESM"], ["cesm"]),
                    AutoQualTable = FindFirst(tables, ["dbo_QUAL", "QUAL"], ["qual"]),
                    AutoCregTable = FindFirst(tables, ["dbo_STUD", "STUD"], ["stud"]),
                    AutoCrseTable = FindFirst(tables, ["dbo_STUD", "STUD"], ["stud"]),
                    AutoPqmTable = tables.FirstOrDefault(t => t.Equals("PQM", StringComparison.OrdinalIgnoreCase) || t.Contains("PQM", StringComparison.OrdinalIgnoreCase))
                };
            }
            catch (Exception ex)
            {
                return new Rule13TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule13VerifyResult> VerifyTablesAsync(Rule13VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.QualTable, request.CregTable, request.CrseTable);

                var summary = await AnalyseAsync(new Rule13ValidationRequest
                {
                    Server = request.Server,
                    Database = request.Database,
                    Driver = request.Driver,
                    StudTable = request.StudTable,
                    QualTable = request.QualTable,
                    CregTable = request.CregTable,
                    CrseTable = request.CrseTable,
                    PgTypesText = request.PgTypesText
                }, includeAllReviewRows: false);

                return new Rule13VerifyResult
                {
                    Success = true,
                    FoundationStudentCount = summary.FoundationStudentCount,
                    ValidatedRowCount = summary.TotalValidated
                };
            }
            catch (Exception ex)
            {
                return new Rule13VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule13ValidationSummary> RunValidationAsync(Rule13ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.QualTable, request.CregTable, request.CrseTable);

                var summary = await AnalyseAsync(request, includeAllReviewRows: true);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName);
                    }
                    catch (Exception ex)
                    {
                        summary.Success = false;
                        summary.Error = $"Validation completed, but the saved run could not be written to the system database: {ex.Message}";
                        return summary;
                    }
                }

                ApplyBrowserPreview(summary);

                return summary;
            }
            catch (Exception ex)
            {
                return new Rule13ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule13ValidationSummary> GetExportSummaryAsync(Rule13ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.QualTable, request.CregTable, request.CrseTable);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule13WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID,
    vr.ClientID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS StudTable,
    ISNULL(vr.DeceasedTable, '') AS QualTable,
    ISNULL(vr.StudColumn, '') AS CregTable,
    ISNULL(vr.DeceasedColumn, '') AS CrseTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 13
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var deserializedSummary = DeserializeSummary(reader.IsDBNull(11) ? null : reader.GetString(11));
            if (deserializedSummary != null && includeSummary)
            {
                ApplyBrowserPreview(deserializedSummary);
            }
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule13WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim(),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim(),
                StudTable = NormalizeObjectName(reader.IsDBNull(4) ? "dbo_CESM" : reader.GetString(4), "dbo_CESM"),
                QualTable = NormalizeObjectName(reader.IsDBNull(5) ? "dbo_QUAL" : reader.GetString(5), "dbo_QUAL"),
                CregTable = NormalizeObjectName(reader.IsDBNull(6) ? "dbo_STUD" : reader.GetString(6), "dbo_STUD"),
                CrseTable = NormalizeObjectName(reader.IsDBNull(7) ? "dbo_STUD" : reader.GetString(7), "dbo_STUD"),
                CurrentStatus = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim(),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary = summary
            };

            if (string.IsNullOrWhiteSpace(workspace.CrseTable))
                workspace.CrseTable = workspace.CregTable;

            if (summary != null)
                workspace.CurrentStatus = summary.Status;

            if (deserializedSummary != null)
            {
                workspace.PgTypesText = string.IsNullOrWhiteSpace(deserializedSummary.PgTypesText)
                    ? string.Join(", ", deserializedSummary.PgTypes)
                    : deserializedSummary.PgTypesText;
                workspace.GoverningPartCodes = NormalizeGoverningPartCodes(deserializedSummary.GoverningPartCodes);
                workspace.PqmTable    = deserializedSummary.PqmTable    ?? "";
                workspace.CesmIdCol   = deserializedSummary.CesmIdCol   ?? "_001";
                workspace.CesmCodeCol = deserializedSummary.CesmCodeCol ?? "_006";
                workspace.QualIdCol   = deserializedSummary.QualIdCol   ?? "_001";
                workspace.QualNameCol = deserializedSummary.QualNameCol ?? "_003";
                workspace.StudIdCol   = deserializedSummary.StudIdCol   ?? "_001";
                workspace.PqmNameCol  = deserializedSummary.PqmNameCol  ?? "Authorised_Qualification_Name";
                workspace.PqmCode1Col = deserializedSummary.PqmCode1Col ?? "CESM_Code";
                workspace.PqmCode2Col = deserializedSummary.PqmCode2Col ?? "CESM_Code2";
            }

            await reader.CloseAsync();

            workspace.Driver = "ODBC Driver 17 for SQL Server";
            workspace.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var currentRoleSignoff = signoffs.FirstOrDefault(s =>
                HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff = currentRoleSignoff != null;
            workspace.CurrentUserSignoffComment = currentRoleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            return workspace;
        }

        public async Task<Rule13RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var sourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            if (includeFullResults)
            {
                summary = await ExpandSavedSummaryIfNeededAsync(summary, sourceServer);
                summary.DisplayedCount = summary.ReviewRows.Count;
                summary.IsPreviewOnly = false;
                summary.PreviewLimit = 0;
            }
            else
            {
                ApplyBrowserPreview(summary);
            }

            var clientId = reader.GetInt32(1);
            var review = new Rule13RunReviewViewModel
            {
                RunId = reader.GetInt32(0),
                ClientId = clientId,
                IsCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer = sourceServer,
                Summary = summary
            };

            await reader.CloseAsync();

            review.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";
            review.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            review.HasDataAnalystSignoff = review.Signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return review;
        }

        public async Task<Rule13WorkspaceSaveResult> SaveWorkspaceAsync(Rule13ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                ValidateRequest(request);

                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule13WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run the validation first so the workspace can be saved."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule13WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "The saved workspace could not be found for this engagement."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, request.ClientId);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash = await GetValidationRecordHashAsync(connection, request.RunId.Value);
                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer = @HemisServer,
    AuditDatabase = @AuditDatabase,
    StudTable = @StudTable,
    DeceasedTable = @QualTable,
    StudColumn = @CregTable,
    DeceasedColumn = @CrseTable,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID
  AND ClientID = @ClientID
  AND RuleNumber = 13;";
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID", request.ClientId);
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@StudTable", request.StudTable);
                command.Parameters.AddWithValue("@QualTable", request.QualTable);
                command.Parameters.AddWithValue("@CregTable", request.CregTable);
                command.Parameters.AddWithValue("@CrseTable", request.CrseTable);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)(reviewerName ?? reviewerEmail) ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule13|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.StudTable}|{request.QualTable}|{request.CregTable}|{request.CrseTable}|{request.PgTypesText}|{string.Join(",", NormalizeGoverningPartCodes(request.GoverningPartCodes))}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule13WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0
                        ? "Workspace saved. Existing signoffs were removed and the run must be reviewed again."
                        : "Workspace saved and marked for review again.",
                    SignoffsCleared = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule13WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule13WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule13WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash = await GetValidationRecordHashAsync(connection, runId);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = NULL,
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID;";
                command.Parameters.AddWithValue("@RunID", runId);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)(reviewerName ?? reviewerEmail) ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule13|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule13WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0
                        ? "Editing has begun. Existing signoffs were removed."
                        : "Editing has begun. Save the workspace when you are ready.",
                    SignoffsCleared = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule13WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("The reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("The selected Rule 13 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off a Rule 13 run.");

            if (!string.Equals(signoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
                !await HasSignoffRoleAsync(connection, runId, "DataAnalyst"))
            {
                throw new InvalidOperationException("The assigned data analyst must sign off before this review can be completed.");
            }

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
IF EXISTS (
    SELECT 1
    FROM dbo.ReviewSignoffs
    WHERE RunID = @RunID
      AND ReviewerID = @ReviewerID
)
BEGIN
    UPDATE dbo.ReviewSignoffs
    SET SignoffRole = @SignoffRole,
        ReviewType = 'Final',
        Comment = @Comment,
        SignedOffAt = GETDATE()
    WHERE RunID = @RunID
      AND ReviewerID = @ReviewerID;
END
ELSE
BEGIN
    INSERT INTO dbo.ReviewSignoffs (ClientID, RunID, ReviewerID, SignoffRole, ReviewType, Comment, SignedOffAt)
    VALUES (@ClientID, @RunID, @ReviewerID, @SignoffRole, 'Final', @Comment, GETDATE());
END";
            command.Parameters.AddWithValue("@ClientID", clientId.Value);
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId.Value);
            command.Parameters.AddWithValue("@SignoffRole", signoffRole!);
            command.Parameters.AddWithValue("@Comment", string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            await command.ExecuteNonQueryAsync();

            await UpdateRunStatusFromSignoffsAsync(connection, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("The reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("The selected Rule 13 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule13ValidationRequest request)
        {
            ValidateRequest(request);

            var cesmTable   = Sanitise(request.StudTable);
            var qualTable   = Sanitise(request.QualTable);
            var studTable   = Sanitise(request.CregTable);
            var cesmIdCol   = Sanitise(string.IsNullOrWhiteSpace(request.CesmIdCol)   ? "_001" : request.CesmIdCol);
            var cesmCodeCol = Sanitise(string.IsNullOrWhiteSpace(request.CesmCodeCol) ? "_006" : request.CesmCodeCol);
            var qualIdCol   = Sanitise(string.IsNullOrWhiteSpace(request.QualIdCol)   ? "_001" : request.QualIdCol);
            var qualNameCol = Sanitise(string.IsNullOrWhiteSpace(request.QualNameCol) ? "_003" : request.QualNameCol);

            var sql = $@"-- ============================================================================
-- HEMIS RULE 13: CESM QUALIFICATION POPULATION VALIDATION
-- 100% POPULATION WITH PROPER EXTRACTION
-- ============================================================================

DROP TABLE IF EXISTS #Rule13_CESM_Extracted_Population;
DROP TABLE IF EXISTS #Rule13_Validation;

-- ============================================================================
-- STEP 1: EXTRACT 100% CESM POPULATION
-- ============================================================================

SELECT DISTINCT
    ROW_NUMBER() OVER (ORDER BY CESM.[{cesmIdCol}], CESM.[{cesmCodeCol}]) AS [Extract_Number],
    CAST(CESM.[{cesmIdCol}] AS NVARCHAR(255)) AS [Qualification_Code],
    CAST(CESM.[{cesmCodeCol}] AS NVARCHAR(255)) AS [Major_Field_CESM],
    QUAL.[{qualNameCol}] AS [Qualification_Name_Designator],
    QUAL.[_004] AS [Qualification_Approval_Status],
    QUAL.[_005] AS [Qualification_Type_Descriptor],
    QUAL.[_084] AS [Legacy_Indicator],
    QUAL.[_085] AS [NQF_Exit_Level],
    QUAL.[_086] AS [Minimum_Total_Credits]
INTO #Rule13_CESM_Extracted_Population
FROM [{cesmTable}] CESM
LEFT JOIN [{qualTable}] QUAL
    ON CAST(QUAL.[{qualIdCol}] AS NVARCHAR(255)) = CAST(CESM.[{cesmIdCol}] AS NVARCHAR(255))
WHERE CESM.[{cesmIdCol}] IS NOT NULL
  AND LTRIM(RTRIM(ISNULL(CAST(CESM.[{cesmCodeCol}] AS NVARCHAR(255)), ''))) NOT IN ('', 'ZZZZZZ');

-- ============================================================================
-- STEP 2: VALIDATE CESM POPULATION AGAINST STUDENT LINKAGE
-- ============================================================================

SELECT
    P.[Extract_Number],
    P.[Qualification_Code],
    P.[Major_Field_CESM],
    P.[Qualification_Name_Designator],
    P.[Qualification_Approval_Status],
    P.[Qualification_Type_Descriptor],
    P.[Legacy_Indicator],
    P.[NQF_Exit_Level],
    P.[Minimum_Total_Credits],
    COUNT(DISTINCT CAST(STUD.[_007] AS NVARCHAR(255))) AS [Linked_Student_Count],
    CASE WHEN COUNT(DISTINCT CAST(STUD.[_007] AS NVARCHAR(255))) >= 1 THEN 'PASS' ELSE 'FAIL' END AS [Validation_Result],
    CASE
        WHEN COUNT(DISTINCT CAST(STUD.[_007] AS NVARCHAR(255))) >= 1
            THEN 'CESM qualification has at least one linked student record.'
        ELSE 'CESM qualification has no linked student record in [{studTable}].'
    END AS [Validation_Reason]
INTO #Rule13_Validation
FROM #Rule13_CESM_Extracted_Population P
LEFT JOIN [{studTable}] STUD
    ON CAST(STUD.[{cesmIdCol}] AS NVARCHAR(255)) = CAST(P.[Qualification_Code] AS NVARCHAR(255))
GROUP BY
    P.[Extract_Number], P.[Qualification_Code], P.[Major_Field_CESM],
    P.[Qualification_Name_Designator], P.[Qualification_Approval_Status],
    P.[Qualification_Type_Descriptor], P.[Legacy_Indicator], P.[NQF_Exit_Level], P.[Minimum_Total_Credits];

-- ============================================================================
-- STEP 3: FULL EXTRACTED POPULATION
-- ============================================================================

SELECT * FROM #Rule13_CESM_Extracted_Population ORDER BY [Extract_Number];

-- ============================================================================
-- STEP 4: FULL VALIDATION RESULT
-- ============================================================================

SELECT * FROM #Rule13_Validation ORDER BY [Extract_Number];

-- ============================================================================
-- STEP 5: EXCEPTIONS ONLY
-- ============================================================================

SELECT * FROM #Rule13_Validation WHERE [Validation_Result] = 'FAIL' ORDER BY [Extract_Number];

-- ============================================================================
-- STEP 6: SUMMARY
-- ============================================================================

SELECT
    COUNT(*)                                                                              AS [Total_CESM_Qualifications],
    SUM(CASE WHEN [Validation_Result] = 'PASS' THEN 1 ELSE 0 END)                       AS [PASS_Count],
    SUM(CASE WHEN [Validation_Result] = 'FAIL' THEN 1 ELSE 0 END)                       AS [FAIL_Count],
    CAST(SUM(CASE WHEN [Validation_Result] = 'FAIL' THEN 1 ELSE 0 END) * 100.0
         / NULLIF(COUNT(*), 0) AS DECIMAL(5,2))                                          AS [Exception_Rate_Pct]
FROM #Rule13_Validation;

DROP TABLE IF EXISTS #Rule13_CESM_Extracted_Population;
DROP TABLE IF EXISTS #Rule13_Validation;
-- ============================================================================
-- END RULE 13
-- ============================================================================";

            return Task.FromResult(sql.Trim());
        }

        // ============================================================================
        // PQM helper record and matching methods
        // ============================================================================

        private record PqmRow(string? Code1, string? Code2, string? Name);

        private static string Digits(string? v) =>
            v == null ? "" : string.Concat(v.Where(char.IsDigit));

        private static string TrimLeadingZeros(string s) =>
            s.TrimStart('0') is { Length: > 0 } t ? t : s;

        private static string NormName(string? v)
        {
            if (v == null) return "";
            return System.Text.RegularExpressions.Regex.Replace(v.Trim().ToUpperInvariant(), @"\s+", " ");
        }

        private static bool HasSameLeadingDigits(string left, string right, int digits) =>
            left.Length >= digits && right.Length >= digits &&
            string.Equals(left[..digits], right[..digits], StringComparison.Ordinal);

        private static int CesmReviewPriority(string reason) => reason switch
        {
            "first 4 digits matched" => 0,
            "first 4 digits matched after removing leading zeros" => 1,
            "first 3 digits matched" => 2,
            "first 3 digits matched after removing leading zeros" => 3,
            _ => 99
        };

        private record CesmReviewMatch(string Reason, string? PqmCode, string? PqmName);

        private static CesmReviewMatch? GetCesmReviewMatch(string? cesmCode, List<PqmRow> pqm)
        {
            if (string.IsNullOrWhiteSpace(cesmCode)) return null;
            var rawCode = Digits(cesmCode);
            if (rawCode.Length < 3) return null;
            var trimmedCode = TrimLeadingZeros(rawCode);
            CesmReviewMatch? best = null;
            foreach (var p in pqm)
            {
                foreach (var pqmCode in new[] { p.Code1, p.Code2 }.Where(c => !string.IsNullOrWhiteSpace(c)))
                {
                    var pqmRaw = Digits(pqmCode);
                    if (string.IsNullOrEmpty(pqmRaw)) continue;
                    var pqmTrimmed = TrimLeadingZeros(pqmRaw);
                    string? reason = null;
                    if (HasSameLeadingDigits(rawCode, pqmRaw, 4)) reason = "first 4 digits matched";
                    else if (HasSameLeadingDigits(trimmedCode, pqmTrimmed, 4)) reason = "first 4 digits matched after removing leading zeros";
                    else if (HasSameLeadingDigits(rawCode, pqmRaw, 3)) reason = "first 3 digits matched";
                    else if (HasSameLeadingDigits(trimmedCode, pqmTrimmed, 3)) reason = "first 3 digits matched after removing leading zeros";
                    if (reason != null)
                    {
                        if (best == null || CesmReviewPriority(reason) < CesmReviewPriority(best.Reason))
                            best = new CesmReviewMatch(reason, pqmCode?.Trim(), p.Name);
                        if (best.Reason == "first 4 digits matched") return best;
                    }
                }
            }
            return best;
        }

        private static bool ExactCodeMatch(string hDigits, string? pqmCode)
        {
            var p = Digits(pqmCode);
            return !string.IsNullOrEmpty(hDigits) && !string.IsNullOrEmpty(p) &&
                   string.Equals(hDigits, p, StringComparison.Ordinal);
        }

        private static bool CodeMatches(string hDigits, string? pqmCode, int n = 4)
        {
            var p = Digits(pqmCode);
            if (string.IsNullOrEmpty(hDigits) || string.IsNullOrEmpty(p)) return false;
            var use = Math.Min(n, Math.Min(hDigits.Length, p.Length));
            return use >= 2 && string.Equals(hDigits[..use], p[..use], StringComparison.Ordinal);
        }

        private static string? ResolveMatchedPqmCode(string hemisDigits, PqmRow pqm)
        {
            if (CodeMatches(hemisDigits, pqm.Code1)) return pqm.Code1?.Trim();
            if (CodeMatches(hemisDigits, pqm.Code2)) return pqm.Code2?.Trim();
            return pqm.Code1?.Trim() ?? pqm.Code2?.Trim();
        }

        private static void ApplyPqmMatch(Rule13ReviewRowViewModel row, string? cesmCode, string? qualName, List<PqmRow> pqm)
        {
            var hDigits = Digits(cesmCode);
            var hNorm = NormName(qualName);

            var codeRows = pqm.Where(p => CodeMatches(hDigits, p.Code1) || CodeMatches(hDigits, p.Code2)).ToList();
            var combined = codeRows.Where(p => string.Equals(NormName(p.Name), hNorm, StringComparison.Ordinal)).ToList();

            if (combined.Count > 0)
            {
                var best = combined[0];
                var resolvedPqmCode = ResolveMatchedPqmCode(hDigits, best);
                bool isExact = ExactCodeMatch(hDigits, best.Code1) || ExactCodeMatch(hDigits, best.Code2);
                row.PqmCode = resolvedPqmCode ?? "";
                row.PqmName = best.Name ?? "";
                row.PqmCodeMatch = true;
                row.PqmNameMatch = true;
                row.PqmNeedsReview = !isExact;
                row.PqmResult = "PASS";
                row.ValidationResult = "PASS";
                row.PqmExceptionReason = isExact ? "" :
                    $"Pass - CESM review required because CESM leading digits matched against PQM.CESM_CODE. " +
                    $"Qualification Name ({row.QualificationDescription003}): '{qualName}' = Authorised_Qualification_Name: '{best.Name}' | " +
                    $"CESM._006: '{cesmCode}' | PQM CESM_Code: '{resolvedPqmCode}'";
                row.ValidationExplanation = isExact
                    ? $"PASS — PQM code '{resolvedPqmCode}' (exact) and name '{qualName}' confirmed against PQM register."
                    : $"PASS (review) — CESM leading digits matched PQM code '{resolvedPqmCode}'. Name '{qualName}' confirmed. Review CESM code precision.";
                return;
            }

            if (codeRows.Count == 0)
            {
                var review = GetCesmReviewMatch(cesmCode, pqm);
                if (review != null)
                {
                    var reviewNameMatches = string.Equals(NormName(review.PqmName), hNorm, StringComparison.Ordinal);
                    row.PqmCode = review.PqmCode ?? "";
                    row.PqmName = review.PqmName ?? "";
                    row.PqmCodeMatch = false;
                    row.PqmNameMatch = reviewNameMatches;
                    row.PqmNeedsReview = reviewNameMatches;
                    row.PqmResult = reviewNameMatches ? "PASS" : "FAIL";
                    row.ValidationResult = row.PqmResult;
                    row.PqmExceptionReason = reviewNameMatches
                        ? $"Pass - CESM review required ({review.Reason}). " +
                          $"Qualification Name: '{qualName}' = Authorised_Qualification_Name: '{review.PqmName}' | " +
                          $"CESM._006: '{cesmCode}' | PQM CESM_Code: '{review.PqmCode}' (CESM leading digits matched for review)"
                        : $"Fail - qualification name did not align. " +
                          $"Qualification Name: '{qualName}' ≠ Authorised_Qualification_Name: '{review.PqmName}' | " +
                          $"CESM._006: '{cesmCode}' | PQM CESM_Code: '{review.PqmCode}' (CESM leading digits matched for review)";
                    row.ValidationExplanation = reviewNameMatches
                        ? $"PASS (review) — {review.Reason}. Name '{qualName}' confirmed. CESM '{cesmCode}' → PQM '{review.PqmCode}'."
                        : $"FAIL — Name mismatch after leading-digit CESM match. Expected '{qualName}', PQM has '{review.PqmName}'. CESM '{cesmCode}' → PQM '{review.PqmCode}'.";
                    return;
                }
                row.PqmCode = "";
                row.PqmName = "";
                row.PqmCodeMatch = false;
                row.PqmNameMatch = false;
                row.PqmNeedsReview = false;
                row.PqmResult = "FAIL";
                row.ValidationResult = "FAIL";
                row.PqmExceptionReason = $"Fail - no PQM match found. CESM._006: '{cesmCode}' not found in PQM (no 4-digit prefix match in CESM_Code or CESM_Code2)";
                row.ValidationExplanation = $"FAIL — No PQM match. CESM code '{cesmCode}' not found in PQM register (CESM_Code / CESM_Code2).";
                return;
            }

            // Code matched but name didn't
            var bestCode = codeRows[0];
            var resolvedCode = ResolveMatchedPqmCode(hDigits, bestCode);
            var pqmNames = string.Join(" | ", codeRows.Take(3).Select(p => p.Name?.Trim()).Where(n => n != null).Distinct());
            row.PqmCode = resolvedCode ?? "";
            row.PqmName = bestCode.Name ?? "";
            row.PqmCodeMatch = true;
            row.PqmNameMatch = false;
            row.PqmNeedsReview = false;
            row.PqmResult = "FAIL";
            row.ValidationResult = "FAIL";
            row.PqmExceptionReason = $"Fail - qualification name did not align. " +
                $"Qualification Name: '{qualName}' ≠ Authorised_Qualification_Name: '{pqmNames}' | " +
                $"CESM._006: '{cesmCode}' | PQM CESM_Code: '{resolvedCode}'";
            row.ValidationExplanation = $"FAIL — Name mismatch. CESM code '{cesmCode}' → PQM '{resolvedCode}'. PQM name(s): '{pqmNames}'. Expected: '{qualName}'.";
        }

        public async Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName, string tableRole)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME=@t ORDER BY ORDINAL_POSITION";
                cmd.Parameters.AddWithValue("@t", tableName);
                await using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(0));

                string? autoSelected = tableRole?.ToLowerInvariant() switch
                {
                    "cesm_id"   => columns.FirstOrDefault(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "cesm_code" => columns.FirstOrDefault(c => c.Equals("_006", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_id"   => columns.FirstOrDefault(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_name" => columns.FirstOrDefault(c => c.Equals("_003", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_name"  => columns.FirstOrDefault(c => c.Contains("Authorised", StringComparison.OrdinalIgnoreCase) || c.Contains("Qualification_Name", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_code1" => columns.FirstOrDefault(c => c.Equals("CESM_Code", StringComparison.OrdinalIgnoreCase) || c.Equals("CESM_Code1", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_code2" => columns.FirstOrDefault(c => c.Equals("CESM_Code2", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    _           => columns.FirstOrDefault()
                };

                return new ColumnListResult { Success = true, Columns = columns, AutoSelected = autoSelected };
            }
            catch (Exception ex)
            {
                return new ColumnListResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<Rule13ValidationSummary> AnalyseAsync(Rule13ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var cesmTable   = Sanitise(request.StudTable);
            var qualTable   = Sanitise(request.QualTable);
            var studTable   = Sanitise(request.CregTable);
            var cesmIdCol   = Sanitise(string.IsNullOrWhiteSpace(request.CesmIdCol)   ? "_001" : request.CesmIdCol);
            var cesmCodeCol = Sanitise(string.IsNullOrWhiteSpace(request.CesmCodeCol) ? "_006" : request.CesmCodeCol);
            var qualIdCol   = Sanitise(string.IsNullOrWhiteSpace(request.QualIdCol)   ? "_001" : request.QualIdCol);
            var qualNameCol = Sanitise(string.IsNullOrWhiteSpace(request.QualNameCol) ? "_003" : request.QualNameCol);
            var studIdCol   = Sanitise(string.IsNullOrWhiteSpace(request.StudIdCol)   ? "_001" : request.StudIdCol);
            var pqmTable    = Sanitise(request.PqmTable ?? "");
            var pqmNameCol  = Sanitise(string.IsNullOrWhiteSpace(request.PqmNameCol)  ? "Authorised_Qualification_Name" : request.PqmNameCol);
            var pqmCode1Col = Sanitise(string.IsNullOrWhiteSpace(request.PqmCode1Col) ? "CESM_Code" : request.PqmCode1Col);
            var pqmCode2Col = Sanitise(string.IsNullOrWhiteSpace(request.PqmCode2Col) ? "CESM_Code2" : request.PqmCode2Col);
            var hasPqm = !string.IsNullOrWhiteSpace(pqmTable);

            // Step 1: Build temp tables (#Rule13_CESM_Extracted_Population, #Rule13_Validation)
            await using (var prepCmd = conn.CreateConfiguredCommand())
            {
                prepCmd.CommandTimeout = SqlCommandTimeoutSeconds;
                prepCmd.CommandText = $@"
DROP TABLE IF EXISTS #Rule13_CESM_Extracted_Population;
DROP TABLE IF EXISTS #Rule13_Validation;

SELECT DISTINCT
    ROW_NUMBER() OVER (ORDER BY CESM.[{cesmIdCol}], CESM.[{cesmCodeCol}]) AS [Extract_Number],
    CAST(CESM.[{cesmIdCol}] AS NVARCHAR(255)) AS [Qualification_Code],
    CAST(CESM.[{cesmCodeCol}] AS NVARCHAR(255)) AS [Major_Field_CESM],
    CAST(ISNULL(QUAL.[{qualNameCol}], '') AS NVARCHAR(500)) AS [Qualification_Name_Designator],
    CAST(ISNULL(QUAL.[_004], '') AS NVARCHAR(50))  AS [Qualification_Approval_Status],
    CAST(ISNULL(QUAL.[_005], '') AS NVARCHAR(100)) AS [Qualification_Type_Descriptor],
    CAST(ISNULL(QUAL.[_084], '') AS NVARCHAR(50))  AS [Legacy_Indicator],
    CAST(ISNULL(QUAL.[_085], '') AS NVARCHAR(50))  AS [NQF_Exit_Level],
    CAST(ISNULL(QUAL.[_086], '') AS NVARCHAR(50))  AS [Minimum_Total_Credits]
INTO #Rule13_CESM_Extracted_Population
FROM [{cesmTable}] CESM
LEFT JOIN [{qualTable}] QUAL ON CAST(QUAL.[{qualIdCol}] AS NVARCHAR(255)) = CAST(CESM.[{cesmIdCol}] AS NVARCHAR(255))
WHERE CESM.[{cesmIdCol}] IS NOT NULL
  AND LTRIM(RTRIM(ISNULL(CAST(CESM.[{cesmCodeCol}] AS NVARCHAR(255)), ''))) NOT IN ('', 'ZZZZZZ');

SELECT
    P.[Extract_Number],
    P.[Qualification_Code],
    P.[Major_Field_CESM],
    P.[Qualification_Name_Designator],
    P.[Qualification_Approval_Status],
    P.[Qualification_Type_Descriptor],
    P.[Legacy_Indicator],
    P.[NQF_Exit_Level],
    P.[Minimum_Total_Credits],
    COUNT(DISTINCT CAST(STUD.[_007] AS NVARCHAR(255))) AS [Linked_Student_Count],
    CASE WHEN COUNT(DISTINCT CAST(STUD.[_007] AS NVARCHAR(255))) >= 1 THEN 'PASS' ELSE 'FAIL' END AS [Validation_Result],
    CASE
        WHEN COUNT(DISTINCT CAST(STUD.[_007] AS NVARCHAR(255))) >= 1
            THEN 'CESM qualification has at least one linked student record.'
        ELSE 'CESM qualification has no linked student record in [{studTable}].'
    END AS [Validation_Reason]
INTO #Rule13_Validation
FROM #Rule13_CESM_Extracted_Population P
LEFT JOIN [{studTable}] STUD ON CAST(STUD.[{studIdCol}] AS NVARCHAR(255)) = CAST(P.[Qualification_Code] AS NVARCHAR(255))
GROUP BY
    P.[Extract_Number], P.[Qualification_Code], P.[Major_Field_CESM],
    P.[Qualification_Name_Designator], P.[Qualification_Approval_Status],
    P.[Qualification_Type_Descriptor], P.[Legacy_Indicator], P.[NQF_Exit_Level], P.[Minimum_Total_Credits];";
                await prepCmd.ExecuteNonQueryAsync();
            }

            // Step 2: Query rows from #Rule13_Validation
            await using var command = conn.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = "SELECT * FROM #Rule13_Validation ORDER BY [Extract_Number];";

            var reviewRows = new List<Rule13ReviewRowViewModel>();
            await using var reader = await command.ExecuteReaderAsync();
            var validationNumber = 0;
            while (await reader.ReadAsync())
            {
                validationNumber++;
                string ReadCol(string col) { var o = reader.GetOrdinal(col); return reader.IsDBNull(o) ? "" : Convert.ToString(reader.GetValue(o), CultureInfo.InvariantCulture) ?? ""; }
                int ReadIntCol(string col) { var o = reader.GetOrdinal(col); return reader.IsDBNull(o) ? 0 : Convert.ToInt32(reader.GetValue(o)); }

                var qualificationCode   = ReadCol("Qualification_Code");
                var majorField          = ReadCol("Major_Field_CESM");
                var qualDesc            = ReadCol("Qualification_Name_Designator");
                var approvalStatus      = ReadCol("Qualification_Approval_Status");
                var qualType            = ReadCol("Qualification_Type_Descriptor");
                var legacyIndicator     = ReadCol("Legacy_Indicator");
                var nqfExitLevel        = ReadCol("NQF_Exit_Level");
                var minCredits          = ReadCol("Minimum_Total_Credits");
                var studentCount        = ReadIntCol("Linked_Student_Count");
                var validationResult    = ReadCol("Validation_Result");
                var validationReason    = ReadCol("Validation_Reason");

                var studLinkResult = studentCount >= 1 ? "PASS" : "FAIL";
                reviewRows.Add(new Rule13ReviewRowViewModel
                {
                    ValidationNumber        = validationNumber,
                    PartCode                = ScopeCode,
                    PartTitle               = ScopeTitle,
                    PartDescription         = ScopeDescription,
                    StudentNumber007        = qualificationCode,
                    QualificationCode001    = qualificationCode,
                    QualificationDescription003 = qualDesc,
                    QualificationType005    = qualType,
                    FoundationFlag106       = approvalStatus,
                    BridgeQualificationCode001 = legacyIndicator,
                    CourseCode030           = majorField,
                    CrseCourseCode030       = nqfExitLevel,
                    FoundationCourse091     = minCredits,
                    StudentType             = studentCount.ToString(),
                    StudLinkCount           = studentCount,
                    StudLinkResult          = studLinkResult,
                    NotebookStatus          = validationResult == "PASS" ? "VALID" : "INVALID",
                    ValidationResult        = validationResult,
                    ValidationExplanation   = validationReason
                });
            }

            await reader.CloseAsync();

            // Step 3: Load PQM data and apply in-memory matching if PQM table is configured
            List<PqmRow> pqmRows = new();
            if (hasPqm)
            {
                var pqmSql = string.IsNullOrWhiteSpace(pqmCode2Col)
                    ? $"SELECT [{pqmNameCol}], [{pqmCode1Col}], NULL FROM [{pqmTable}]"
                    : $"SELECT [{pqmNameCol}], [{pqmCode1Col}], [{pqmCode2Col}] FROM [{pqmTable}]";
                await using var pqmCmd = conn.CreateConfiguredCommand();
                pqmCmd.CommandTimeout = SqlCommandTimeoutSeconds;
                pqmCmd.CommandText = pqmSql;
                await using var pqmReader = await pqmCmd.ExecuteReaderAsync();
                while (await pqmReader.ReadAsync())
                {
                    pqmRows.Add(new PqmRow(
                        pqmReader.IsDBNull(1) ? null : pqmReader.GetValue(1)?.ToString(),
                        pqmReader.IsDBNull(2) ? null : pqmReader.GetValue(2)?.ToString(),
                        pqmReader.IsDBNull(0) ? null : pqmReader.GetValue(0)?.ToString()));
                }
            }

            if (hasPqm && pqmRows.Count > 0)
            {
                foreach (var row in reviewRows)
                {
                    ApplyPqmMatch(row, row.CourseCode030, row.QualificationDescription003, pqmRows);
                    // STUD linkage overrides PQM: no linked student = FAIL regardless of PQM
                    if (row.StudLinkResult == "FAIL")
                    {
                        row.ValidationResult = "FAIL";
                        row.ValidationExplanation = string.IsNullOrWhiteSpace(row.PqmExceptionReason)
                            ? $"FAIL — No linked STUD record (Linked_Student_Count = 0). PQM result: {row.PqmResult}."
                            : $"FAIL — No linked STUD record (Linked_Student_Count = 0). PQM: {row.PqmResult}. {row.PqmExceptionReason}";
                    }
                }
            }
            else
            {
                // No PQM — STUD linkage result is the sole validation result
                foreach (var row in reviewRows)
                {
                    if (row.StudLinkResult == "FAIL")
                    {
                        row.ValidationResult = "FAIL";
                    }
                }
            }

            reviewRows = NormalizeReviewRows(reviewRows);

            var totalValidated = reviewRows.Count;
            var passCount = reviewRows.Count(x => string.Equals(x.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase));
            var failCount = reviewRows.Count(x => string.Equals(x.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase));
            var reviewCount = reviewRows.Count(x => x.PqmNeedsReview);
            var validCesmCount = totalValidated;
            var partSummaries = new List<Rule13PartSummaryItemViewModel>
            {
                new Rule13PartSummaryItemViewModel
                {
                    PartCode = ScopeCode,
                    PartTitle = ScopeTitle,
                    PartDescription = ScopeDescription,
                    TotalCount = totalValidated,
                    PassCount = passCount,
                    FailCount = failCount,
                    Status = failCount == 0 ? "PASS" : "FAIL"
                }
            };
            var displayedRows = includeAllReviewRows
                ? reviewRows
                : reviewRows.Take(BrowserPreviewRowLimit).ToList();
            var isPreviewOnly = !includeAllReviewRows && totalValidated > displayedRows.Count;
            var overallStatusRuleText = hasPqm
                ? "Overall PASS requires every qualifying CESM qualification to have a matching PQM row (CESM code and qualification name) and link to at least one STUD row."
                : "Overall PASS requires every qualifying CESM qualification to link to at least one STUD row.";
            var runStatus = failCount == 0 ? (reviewCount > 0 ? "PASS WITH REVIEW" : "PASS") : "FAIL";

            return new Rule13ValidationSummary
            {
                Success = true,
                FoundationStudentCount = validCesmCount,
                TotalValidated = totalValidated,
                DisplayedCount = displayedRows.Count,
                IsPreviewOnly = isPreviewOnly,
                PreviewLimit = isPreviewOnly ? BrowserPreviewRowLimit : 0,
                PassCount = passCount,
                FailCount = failCount,
                ReviewCount = reviewCount,
                ExceptionRate = totalValidated == 0 ? 0m : Math.Round(failCount * 100m / totalValidated, 2),
                Status = runStatus,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                StudTable = request.StudTable,
                QualTable = request.QualTable,
                CregTable = request.CregTable,
                CrseTable = request.CregTable,
                PgTypesText = "",
                PgTypes = new List<string>(),
                GoverningPartCodes = [ScopeCode],
                GoverningPartCodesText = "100% population",
                OverallStatusRuleText = overallStatusRuleText,
                TableLinkageText = $"{request.StudTable}.{cesmIdCol} → {request.QualTable}.{qualIdCol} → {request.CregTable}.{studIdCol}",
                ProcedureSteps = new List<string>
                {
                    $"Step 1: Extract 100% of {request.StudTable} where {cesmCodeCol} <> 'ZZZZZZ'.",
                    $"Step 2: Match {request.StudTable}.{cesmIdCol} to {request.QualTable}.{qualIdCol}.",
                    $"Step 3: Match {request.QualTable}.{qualIdCol} to {request.CregTable}.{studIdCol}.",
                    "Step 4: PASS when the qualifying CESM qualification links to at least one STUD row.",
                    hasPqm
                        ? $"Step 5 (PQM): CESM.{cesmCodeCol} 4-digit prefix matches PQM.{pqmCode1Col} or PQM.{pqmCode2Col}, AND QUAL.{qualNameCol} (case-insensitive) matches PQM.{pqmNameCol} — both on the same PQM row."
                        : "Overall PASS: every qualifying CESM qualification must link to at least one STUD row."
                },
                ClientId = request.ClientId,
                PartSummaries = partSummaries,
                ReviewRows = displayedRows,
                PqmTable = request.PqmTable ?? "",
                CesmIdCol = cesmIdCol,
                CesmCodeCol = cesmCodeCol,
                QualIdCol = qualIdCol,
                QualNameCol = qualNameCol,
                StudIdCol = studIdCol,
                PqmNameCol = pqmNameCol,
                PqmCode1Col = pqmCode1Col,
                PqmCode2Col = pqmCode2Col,
                Warning = includeAllReviewRows
                    ? "Rule 13 completed with the full CESM qualification population."
                    : "Counts reflect the full CESM qualification population. Browser review rows are limited for performance."
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule13ValidationRequest request, Rule13ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 13);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 13);
            var failRows = summary.ReviewRows.Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(
    ClientID, UserID, RuleNumber, RuleName, Status, TotalRecords, PassCount, FailCount, ExceptionRate, RunTimestamp,
    HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
    ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, IsCurrent
)
VALUES
(
    @ClientID, @UserID, 13, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @QualTable, @CregTable, @CrseTable,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "CESM Qualification Population Validation");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@QualTable", request.QualTable);
            command.Parameters.AddWithValue("@CregTable", request.CregTable);
            command.Parameters.AddWithValue("@CrseTable", request.CrseTable);
            var persistedSummary = CreateBrowserPreview(summary);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(failRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persistedSummary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET RecordHash = @RecordHash
WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule13|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private async Task<Dictionary<string, List<Rule13ReviewRowViewModel>>> LoadNotebookRowsByPartAsync(
            SqlConnection connection,
            string studTable,
            string qualTable,
            string cregTable,
            string crseTable,
            List<string> pgTypes,
            string pgTypeSqlList)
        {
            var rowsByPart = new Dictionary<string, List<Rule13ReviewRowViewModel>>(StringComparer.OrdinalIgnoreCase);
            foreach (var partCode in PartOrder)
            {
                rowsByPart[partCode] = await LoadNotebookPartRowsAsync(
                    connection,
                    partCode,
                    studTable,
                    qualTable,
                    cregTable,
                    crseTable,
                    pgTypes,
                    pgTypeSqlList);
            }

            return rowsByPart;
        }

        private static async Task<List<Rule13ReviewRowViewModel>> LoadNotebookPartRowsAsync(
            SqlConnection connection,
            string partCode,
            string studTable,
            string qualTable,
            string cregTable,
            string crseTable,
            List<string> pgTypes,
            string pgTypeSqlList)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = BuildNotebookPartQuery(partCode, studTable, qualTable, cregTable, crseTable, pgTypeSqlList);

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule13ReviewRowViewModel>();
            while (await reader.ReadAsync())
            {
                var qualificationType = ReadString(reader, 6);
                var foundationCourse = ReadString(reader, 10);
                var notebookStatus = IsNotebookFoundationCourse(foundationCourse) ? "VALID" : "INVALID";

                rows.Add(new Rule13ReviewRowViewModel
                {
                    PartCode = partCode,
                    PartTitle = GetPartTitle(partCode),
                    PartDescription = GetPartDescription(partCode),
                    StudentNumber007 = ReadString(reader, 0),
                    QualificationCode001 = ReadString(reader, 1),
                    Name019 = ReadString(reader, 2),
                    IdNumber024 = ReadString(reader, 3),
                    FoundationFlag106 = ReadString(reader, 4),
                    QualificationDescription003 = ReadString(reader, 5),
                    QualificationType005 = qualificationType,
                    BridgeQualificationCode001 = ReadString(reader, 7),
                    CourseCode030 = ReadString(reader, 8),
                    CrseCourseCode030 = ReadString(reader, 9),
                    FoundationCourse091 = foundationCourse,
                    StudentType = GetNotebookStudentType(partCode, qualificationType, pgTypes),
                    NotebookStatus = notebookStatus,
                    ValidationResult = notebookStatus == "VALID" ? "PASS" : "FAIL",
                    ValidationExplanation = BuildValidationExplanation(
                        ReadString(reader, 4),
                        ReadString(reader, 8),
                        foundationCourse)
                });
            }

            return rows;
        }

        private static string BuildNotebookPartQuery(
            string partCode,
            string studTable,
            string qualTable,
            string cregTable,
            string crseTable,
            string pgTypeSqlList)
        {
            return $@"
SELECT
    CAST(S.[_007] AS nvarchar(255)) AS StudentNumber007,
    CAST(S.[_001] AS nvarchar(255)) AS QualificationCode001,
    CAST(S.[_019] AS nvarchar(255)) AS Name019,
    CAST(S.[_024] AS nvarchar(255)) AS IdNumber024,
    CAST(S.[_106] AS nvarchar(255)) AS FoundationFlag106,
    CAST(Q.[_003] AS nvarchar(255)) AS QualificationDescription003,
    CAST(Q.[_005] AS nvarchar(255)) AS QualificationType005,
    CAST(BRIDGE.[_001] AS nvarchar(255)) AS BridgeQualificationCode001,
    CAST(BRIDGE.[_030] AS nvarchar(255)) AS CourseCode030,
    CAST(CRSE.[_030] AS nvarchar(255)) AS CrseCourseCode030,
    CAST(CRSE.[_091] AS nvarchar(255)) AS FoundationCourse091
{BuildBaseJoinClause(studTable, qualTable, cregTable, crseTable)}
WHERE {FoundationStudentCondition("S")}
  AND {IsFoundationCourseCondition("CRSE")}
ORDER BY S.[_007];";
        }

        private static List<Rule13PartSummaryItemViewModel> BuildNotebookPartSummaries(
            IReadOnlyDictionary<string, List<Rule13ReviewRowViewModel>> rowsByPart) =>
            PartOrder
                .Select(partCode =>
                {
                    var rows = rowsByPart.TryGetValue(partCode, out var partRows)
                        ? partRows
                        : new List<Rule13ReviewRowViewModel>();
                    var passCount = rows.Count(row => string.Equals(row.ValidationResult, "PASS", StringComparison.Ordinal));
                    var failCount = rows.Count - passCount;

                    return new Rule13PartSummaryItemViewModel
                    {
                        PartCode = partCode,
                        PartTitle = GetPartTitle(partCode),
                        PartDescription = GetPartDescription(partCode),
                        TotalCount = rows.Count,
                        PassCount = passCount,
                        FailCount = failCount,
                        Status = failCount == 0 ? "PASS" : "FAIL"
                    };
                })
                .ToList();

        private static async Task<(List<Rule13PartSummaryItemViewModel> PartSummaries, List<Rule13ReviewRowViewModel> ReviewRows)> LoadNotebookPreviewAnalysisAsync(
            SqlConnection connection,
            string studTable,
            string qualTable,
            string cregTable,
            string crseTable,
            string pgTypeSqlList)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = BuildNotebookPreviewAnalysisQuery(studTable, qualTable, cregTable, crseTable, pgTypeSqlList);

            var partSummaries = new List<Rule13PartSummaryItemViewModel>();
            var reviewRows = new List<Rule13ReviewRowViewModel>();

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var passCount = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4));
                var failCount = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5));

                partSummaries.Add(new Rule13PartSummaryItemViewModel
                {
                    PartCode = ReadString(reader, 0),
                    PartTitle = ReadString(reader, 1),
                    PartDescription = ReadString(reader, 2),
                    TotalCount = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)),
                    PassCount = passCount,
                    FailCount = failCount,
                    Status = failCount == 0 ? "PASS" : "FAIL"
                });
            }

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    reviewRows.Add(new Rule13ReviewRowViewModel
                    {
                        PartCode = ReadString(reader, 1),
                        PartTitle = ReadString(reader, 2),
                        PartDescription = ReadString(reader, 3),
                        StudentNumber007 = ReadString(reader, 4),
                        QualificationCode001 = ReadString(reader, 5),
                        Name019 = ReadString(reader, 6),
                        IdNumber024 = ReadString(reader, 7),
                        FoundationFlag106 = ReadString(reader, 8),
                        QualificationDescription003 = ReadString(reader, 9),
                        QualificationType005 = ReadString(reader, 10),
                        BridgeQualificationCode001 = ReadString(reader, 11),
                        CourseCode030 = ReadString(reader, 12),
                        CrseCourseCode030 = ReadString(reader, 13),
                        FoundationCourse091 = ReadString(reader, 14),
                        StudentType = ReadString(reader, 15),
                        NotebookStatus = ReadString(reader, 16),
                        ValidationResult = ReadString(reader, 17),
                        ValidationExplanation = BuildValidationExplanation(
                            ReadString(reader, 8),
                            ReadString(reader, 12),
                            ReadString(reader, 14))
                    });
                }
            }

            return
            (
                PartOrder
                    .Select(partCode => partSummaries.FirstOrDefault(item => string.Equals(item.PartCode, partCode, StringComparison.OrdinalIgnoreCase))
                        ?? new Rule13PartSummaryItemViewModel
                        {
                            PartCode = partCode,
                            PartTitle = GetPartTitle(partCode),
                            PartDescription = GetPartDescription(partCode),
                            TotalCount = 0,
                            PassCount = 0,
                            FailCount = 0,
                            Status = "PASS"
                        })
                    .ToList(),
                reviewRows
            );
        }

        private static async Task<List<Rule13ReviewRowViewModel>> LoadNotebookRowsAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule13ReviewRowViewModel>();
            while (await reader.ReadAsync())
            {
                rows.Add(new Rule13ReviewRowViewModel
                {
                    PartCode = ReadString(reader, 1),
                    PartTitle = ReadString(reader, 2),
                    PartDescription = ReadString(reader, 3),
                    StudentNumber007 = ReadString(reader, 4),
                    QualificationCode001 = ReadString(reader, 5),
                    Name019 = ReadString(reader, 6),
                    IdNumber024 = ReadString(reader, 7),
                    FoundationFlag106 = ReadString(reader, 8),
                    QualificationDescription003 = ReadString(reader, 9),
                    QualificationType005 = ReadString(reader, 10),
                    BridgeQualificationCode001 = ReadString(reader, 11),
                    CourseCode030 = ReadString(reader, 12),
                    CrseCourseCode030 = ReadString(reader, 13),
                    FoundationCourse091 = ReadString(reader, 14),
                    StudentType = ReadString(reader, 15),
                    NotebookStatus = ReadString(reader, 16),
                    ValidationResult = ReadString(reader, 17),
                    ValidationExplanation = BuildValidationExplanation(
                        ReadString(reader, 8),
                        ReadString(reader, 12),
                        ReadString(reader, 14))
                });
            }

            return rows;
        }

        private static string GetNotebookStudentType(string partCode, string qualificationType, IEnumerable<string> pgTypes)
        {
            if (string.Equals(partCode, "A", StringComparison.OrdinalIgnoreCase))
                return "Undergraduate";
            if (string.Equals(partCode, "C", StringComparison.OrdinalIgnoreCase))
                return "Postgraduate";

            var notebookValue = (qualificationType ?? "").Trim();
            return pgTypes.Any(pgType => string.Equals(pgType, notebookValue, StringComparison.Ordinal))
                ? "Postgraduate"
                : "Undergraduate";
        }

        private static bool IsNotebookFoundationCourse(string? foundationCourseValue) =>
            string.Equals(NormalizeRowText(foundationCourseValue), "Y", StringComparison.OrdinalIgnoreCase);

        private static string ReadString(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? "" : Convert.ToString(reader.GetValue(ordinal)) ?? "";

        private async Task EnsureColumnsExistAsync(string server, string database, string driver, string studTable, string qualTable, string cregTable, string crseTable)
        {
            var studColumns = await GetTableColumnsAsync(server, database, driver, studTable);
            var qualColumns = await GetTableColumnsAsync(server, database, driver, qualTable);
            var cregColumns = await GetTableColumnsAsync(server, database, driver, cregTable);

            EnsureRequiredColumns(studTable, studColumns, ["_001", "_006"]);
            EnsureRequiredColumns(qualTable, qualColumns, ["_001", "_003"]);
            EnsureRequiredColumns(cregTable, cregColumns, ["_001"]);
        }

        private async Task<List<string>> GetTableColumnsAsync(string server, string database, string driver, string tableName)
        {
            tableName = NormalizeObjectName(tableName);
            ValidateObjectName(tableName);

            var connStr = BuildConnectionString(server, database, driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @TableName
ORDER BY ORDINAL_POSITION;";
            cmd.Parameters.AddWithValue("@TableName", Sanitise(tableName));

            await using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                    columns.Add(reader.GetString(0).Trim());
            }

            return columns;
        }

        private static void EnsureRequiredColumns(string tableName, List<string> actualColumns, IEnumerable<string> requiredColumns)
        {
            foreach (var requiredColumn in requiredColumns)
            {
                if (!actualColumns.Any(c => c.Equals(requiredColumn, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Column '{requiredColumn}' was not found on table '{tableName}'.");
            }
        }

        private static void ValidateRequest(Rule13ValidationRequest request)
        {
            NormalizeRequest(request);

            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable) ||
                string.IsNullOrWhiteSpace(request.QualTable) ||
                string.IsNullOrWhiteSpace(request.CregTable))
            {
                throw new InvalidOperationException("CESM, QUAL, and STUD tables are required.");
            }

            ValidateObjectName(request.StudTable);
            ValidateObjectName(request.QualTable);
            ValidateObjectName(request.CregTable);
            if (!string.IsNullOrWhiteSpace(request.CrseTable))
                ValidateObjectName(request.CrseTable);
        }

        private static void ValidateRequest(Rule13VerifyRequest request) =>
            ValidateRequest(new Rule13ValidationRequest
            {
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                StudTable = request.StudTable,
                QualTable = request.QualTable,
                CregTable = request.CregTable,
                CrseTable = request.CrseTable,
                PgTypesText = request.PgTypesText,
                GoverningPartCodes = request.GoverningPartCodes
            });

        private async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var countCommand = connection.CreateConfiguredCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCommand.Parameters.AddWithValue("@RunID", runId);
            var clearedSignoffs = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCommand.Parameters.AddWithValue("@RunID", runId);
            await deleteCommand.ExecuteNonQueryAsync();

            await SetRunStatusAsync(connection, runId, "Needs Review");
            return clearedSignoffs;
        }

        private async Task UpdateRunStatusFromSignoffsAsync(SqlConnection connection, int runId)
        {
            var hasAllSignoffs = await HasAllRequiredSignoffsAsync(connection, runId);
            await SetRunStatusAsync(connection, runId, hasAllSignoffs ? "Reviewed and Completed" : "Needs Review");
        }

        private async Task<bool> HasAllRequiredSignoffsAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'DataAnalyst') THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Manager') THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Director') THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return false;

            return reader.GetInt32(0) == 1 && reader.GetInt32(1) == 1 && reader.GetInt32(2) == 1;
        }

        private async Task SetRunStatusAsync(SqlConnection connection, int runId, string status)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET Status = @Status WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@Status", status);
            await command.ExecuteNonQueryAsync();
        }

        private async Task SetRunCurrentStateAsync(SqlConnection connection, int runId, bool isCurrent)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent = @IsCurrent WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@IsCurrent", isCurrent);
            await command.ExecuteNonQueryAsync();
        }

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ValidationRuns
SET IsCurrent = 0
WHERE ClientID = @ClientID
  AND RuleNumber = @RuleNumber
  AND IsCurrent = 1;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(SqlConnection connection, int runId, int? currentUserId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT rs.SignoffID,
       ISNULL(rs.SignoffRole, '') AS SignoffRole,
       LTRIM(RTRIM(ISNULL(u.FirstName, '') + ' ' + ISNULL(u.LastName, ''))) AS ReviewerName,
       ISNULL(u.Email, '') AS ReviewerEmail,
       ISNULL(rs.Comment, '') AS Comment,
       rs.SignedOffAt,
       CASE WHEN @CurrentUserID IS NOT NULL AND rs.ReviewerID = @CurrentUserID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsCurrentUser
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID = rs.ReviewerID
WHERE rs.RunID = @RunID
ORDER BY CASE ISNULL(rs.SignoffRole, '')
            WHEN 'DataAnalyst' THEN 1
            WHEN 'Manager' THEN 2
            WHEN 'Director' THEN 3
            ELSE 4
         END,
         rs.SignedOffAt DESC;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@CurrentUserID", currentUserId.HasValue ? currentUserId.Value : DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync();
            var signoffs = new List<RunSignoffViewModel>();
            while (await reader.ReadAsync())
            {
                signoffs.Add(new RunSignoffViewModel
                {
                    Id = reader.GetInt32(0),
                    SignoffRole = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ReviewerName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    ReviewerEmail = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Comment = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    SignedOffAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                    IsCurrentUser = !reader.IsDBNull(6) && reader.GetBoolean(6)
                });
            }

            return signoffs;
        }

        private async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            command.Parameters.AddWithValue("@Email", email);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 EngagementRole
FROM dbo.UserClientAssignments
WHERE ClientID = @ClientID
  AND UserID = @UserID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@UserID", userId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }
        private static async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM dbo.ValidationRuns
        WHERE RunID = @RunID
          AND (
                WorkspaceSavedAt IS NOT NULL
                OR EXISTS (
                    SELECT 1
                    FROM dbo.ReviewSignoffs rs
                    WHERE rs.RunID = ValidationRuns.RunID
                      AND rs.SignoffRole = 'DataAnalyst'
                )
          )
    ) THEN 1
    ELSE 0
END;";
            command.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }
        private async Task<bool> HasSignoffRoleAsync(SqlConnection connection, int runId, string signoffRole)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM dbo.ReviewSignoffs
    WHERE RunID = @RunID
      AND SignoffRole = @SignoffRole
) THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }

        private async Task<string?> GetValidationRecordHashAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private async Task<Rule13ValidationSummary?> GetValidationSummaryAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ResultsJSON FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value
                ? null
                : DeserializeSummary(Convert.ToString(value));
        }

        private async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 RecordHash
FROM dbo.ValidationRuns
WHERE ClientID = @ClientID
  AND RuleNumber = @RuleNumber
  AND RecordHash IS NOT NULL
ORDER BY RunTimestamp DESC, RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            var status = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
        }

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trust = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                IntegratedSecurity = true,
                TrustServerCertificate = trust,
                Encrypt = false,
                ConnectTimeout = 180
            };

            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";

        private static string BuildNotebookFullQuery(string studTable, string qualTable, string cregTable, string crseTable, string pgTypeSqlList) =>
            $@"
WITH Source AS (
{BuildUnionQuery(studTable, qualTable, cregTable, crseTable, pgTypeSqlList)}
)
SELECT
    PartOrder,
    PartCode,
    PartTitle,
    PartDescription,
    StudentNumber007,
    QualificationCode001,
    Name019,
    IdNumber024,
    FoundationFlag106,
    QualificationDescription003,
    QualificationType005,
    BridgeQualificationCode001,
    CourseCode030,
    CrseCourseCode030,
    FoundationCourse091,
    StudentType,
    NotebookStatus,
    ValidationResult
FROM Source
ORDER BY PartOrder,
         CASE WHEN ValidationResult = 'PASS' THEN 0 ELSE 1 END,
         CASE WHEN TRY_CONVERT(bigint, StudentNumber007) IS NULL THEN 1 ELSE 0 END,
         TRY_CONVERT(bigint, StudentNumber007),
         StudentNumber007,
         QualificationCode001,
         CourseCode030;";

        private static string BuildNotebookPreviewAnalysisQuery(string studTable, string qualTable, string cregTable, string crseTable, string pgTypeSqlList) =>
            $@"
WITH Source AS (
{BuildUnionQuery(studTable, qualTable, cregTable, crseTable, pgTypeSqlList)}
)
SELECT
    PartOrder,
    PartCode,
    PartTitle,
    PartDescription,
    StudentNumber007,
    QualificationCode001,
    Name019,
    IdNumber024,
    FoundationFlag106,
    QualificationDescription003,
    QualificationType005,
    BridgeQualificationCode001,
    CourseCode030,
    CrseCourseCode030,
    FoundationCourse091,
    StudentType,
    NotebookStatus,
    ValidationResult
INTO #Rule13Source
FROM Source;

SELECT
    PartCode,
    MAX(PartTitle) AS PartTitle,
    MAX(PartDescription) AS PartDescription,
    COUNT(*) AS TotalCount,
    SUM(CASE WHEN ValidationResult = 'PASS' THEN 1 ELSE 0 END) AS PassCount,
    SUM(CASE WHEN ValidationResult = 'FAIL' THEN 1 ELSE 0 END) AS FailCount
FROM #Rule13Source
GROUP BY PartOrder, PartCode
ORDER BY PartOrder;

WITH Ranked AS (
    SELECT
        PartOrder,
        PartCode,
        PartTitle,
        PartDescription,
        StudentNumber007,
        QualificationCode001,
        Name019,
        IdNumber024,
        FoundationFlag106,
        QualificationDescription003,
        QualificationType005,
        BridgeQualificationCode001,
        CourseCode030,
        CrseCourseCode030,
        FoundationCourse091,
        StudentType,
        NotebookStatus,
        ValidationResult,
        ROW_NUMBER() OVER (
            ORDER BY PartOrder,
                     CASE WHEN TRY_CONVERT(bigint, StudentNumber007) IS NULL THEN 1 ELSE 0 END,
                     TRY_CONVERT(bigint, StudentNumber007),
                     StudentNumber007,
                     QualificationCode001,
                     CourseCode030
        ) AS PreviewRowNumber
    FROM #Rule13Source
)
SELECT
    PartOrder,
    PartCode,
    PartTitle,
    PartDescription,
    StudentNumber007,
    QualificationCode001,
    Name019,
    IdNumber024,
    FoundationFlag106,
    QualificationDescription003,
    QualificationType005,
    BridgeQualificationCode001,
    CourseCode030,
    CrseCourseCode030,
    FoundationCourse091,
    StudentType,
    NotebookStatus,
    ValidationResult
FROM Ranked
WHERE PreviewRowNumber <= {BrowserPreviewRowLimit}
ORDER BY PartOrder,
         CASE WHEN ValidationResult = 'PASS' THEN 0 ELSE 1 END,
         PreviewRowNumber,
         StudentNumber007,
         QualificationCode001,
         CourseCode030;

DROP TABLE #Rule13Source;";

        private static string BuildUnionQuery(string studTable, string qualTable, string cregTable, string crseTable, string pgTypeSqlList) =>
            $@"
SELECT
    1 AS PartOrder,
    '{ScopeCode}' AS PartCode,
    '{ScopeTitle}' AS PartTitle,
    '{ScopeDescription}' AS PartDescription,
    CAST(S.[_007] AS nvarchar(255)) AS StudentNumber007,
    CAST(S.[_001] AS nvarchar(255)) AS QualificationCode001,
    CAST(S.[_019] AS nvarchar(255)) AS Name019,
    CAST(S.[_024] AS nvarchar(255)) AS IdNumber024,
    CAST(S.[_106] AS nvarchar(255)) AS FoundationFlag106,
    CAST(Q.[_003] AS nvarchar(255)) AS QualificationDescription003,
    CAST(Q.[_005] AS nvarchar(255)) AS QualificationType005,
    CAST(BRIDGE.[_001] AS nvarchar(255)) AS BridgeQualificationCode001,
    CAST(BRIDGE.[_030] AS nvarchar(255)) AS CourseCode030,
    CAST(CRSE.[_030] AS nvarchar(255)) AS CrseCourseCode030,
    CAST(CRSE.[_091] AS nvarchar(255)) AS FoundationCourse091,
    CASE WHEN {IsPostgraduateQualificationCondition("Q", pgTypeSqlList)} THEN 'Postgraduate' ELSE 'Undergraduate' END AS StudentType,
    CASE WHEN {IsFoundationValidationPassCondition("CRSE")} THEN 'VALID' ELSE 'INVALID' END AS NotebookStatus,
    CASE WHEN {IsFoundationValidationPassCondition("CRSE")} THEN 'PASS' ELSE 'FAIL' END AS ValidationResult
{BuildBaseJoinClause(studTable, qualTable, cregTable, crseTable)}
WHERE {FoundationStudentCondition("S")}
  AND {IsFoundationCourseCondition("CRSE")}";

        private static string BuildFoundationStudentCountQuery(string studTable, string qualTable, string bridgeTable, string crseTable) =>
            $@"
SELECT COUNT(*) AS Foundation_Students
FROM [{studTable}] S
WHERE {FoundationStudentCondition("S")};";

        private static string BuildBaseJoinClause(string studTable, string qualTable, string bridgeTable, string crseTable) =>
            $@"
FROM [{studTable}] S
LEFT JOIN [{qualTable}] Q ON S.[_001] = Q.[_001]
LEFT JOIN [{bridgeTable}] BRIDGE ON S.[_001] = BRIDGE.[_001]
LEFT JOIN [{crseTable}] CRSE ON BRIDGE.[_030] = CRSE.[_030]";

        private static List<string> BuildProcedureSteps(string cesmTable, string qualTable, string studTable, string ignoredTable) =>
            new()
            {
                $"Step 1: use the full {cesmTable} population where {cesmTable}._006 <> 'ZZZZZZ' and {cesmTable}._001 is populated.",
                $"Step 2: match {cesmTable}._001 to {qualTable}._001 to carry qualification description details.",
                $"Step 3: match {qualTable}._001 to {studTable}._001 and count linked student records for each qualifying CESM qualification.",
                "Step 4: mark the qualification PASS when at least one STUD row exists; otherwise mark it FAIL.",
                "Step 5: overall PASS requires every qualifying CESM qualification in the full population to have a linked STUD row."
            };

        private static string BuildTableLinkageText(string cesmTable, string qualTable, string studTable, string ignoredTable) =>
            $"{cesmTable}._001 -> {qualTable}._001 -> {studTable}._001";

        private static string BuildValidationExplanation(string step1Status, string qualificationCode001, string linkedStud001)
        {
            var failedChecks = new List<string>();
            step1Status = NormalizeRowText(step1Status);
            qualificationCode001 = NormalizeRowText(qualificationCode001);
            linkedStud001 = NormalizeRowText(linkedStud001);

            if (!string.Equals(step1Status, "PASS", StringComparison.OrdinalIgnoreCase))
                failedChecks.Add("CESM._006 is blank or equals 'ZZZZZZ'");
            if (string.IsNullOrWhiteSpace(qualificationCode001))
                failedChecks.Add("qualification code _001 is blank");
            if (string.IsNullOrWhiteSpace(linkedStud001))
                failedChecks.Add("no linked STUD row was found through QUAL._001 = STUD._001");

            return failedChecks.Count == 0
                ? $"PASS: the CESM qualification is part of the 100% qualifying population and links to STUD._001 '{linkedStud001}'."
                : $"FAIL: {string.Join("; ", failedChecks)}.";
        }

        private static string FoundationStudentCondition(string alias) =>
            $"CAST({alias}.[_106] AS nvarchar(10)) = 'Y'";

        private static string IsFoundationCourseCondition(string alias) =>
            $"CAST({alias}.[_091] AS nvarchar(10)) = 'Y'";

        private static string IsFoundationValidationPassCondition(string alias) =>
            IsFoundationCourseCondition(alias);

        private static string QualificationTypeRawValue(string alias) =>
            $"CAST({alias}.[_005] AS nvarchar(50))";

        private static string QualificationTypeValue(string alias) =>
            $"LTRIM(RTRIM(ISNULL(CAST({alias}.[_005] AS nvarchar(50)), '')))";

        private static string IsPostgraduateQualificationCondition(string alias, string pgTypeSqlList) =>
            string.IsNullOrWhiteSpace(pgTypeSqlList)
                ? "1 = 0"
                : $"{QualificationTypeValue(alias)} IN ({pgTypeSqlList})";

        private static string BuildPartACondition(string pgTypeSqlList) =>
            string.IsNullOrWhiteSpace(pgTypeSqlList)
                ? "1 = 1"
                : $"{QualificationTypeValue("Q")} NOT IN ({pgTypeSqlList})";

        private static string BuildPartCCondition(string pgTypeSqlList) =>
            string.IsNullOrWhiteSpace(pgTypeSqlList)
                ? "1 = 0"
                : $"{QualificationTypeValue("Q")} IN ({pgTypeSqlList})";

        private static string BuildSqlStringList(List<string> values) =>
            values.Count == 0
                ? ""
                : string.Join(", ", values.Select(value => $"'{value.Replace("'", "''")}'"));

        private static List<string> ParsePgTypes(string? pgTypesText)
        {
            var values = (pgTypesText ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (values.Count == 0)
                values = DefaultPgTypes.ToList();

            foreach (var value in values)
            {
                if (value.Length > 10 || value.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')))
                    throw new InvalidOperationException($"Invalid postgraduate type value '{value}'.");
            }

            return values;
        }

        private static List<string> NormalizeGoverningPartCodes(IEnumerable<string>? governingPartCodes)
        {
            _ = (governingPartCodes ?? Enumerable.Empty<string>())
                .Select(code => (code ?? "").Trim().ToUpperInvariant())
                .Where(code => code == ScopeCode || code == "A" || code == "B" || code == "C")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return [ScopeCode];
        }

        private static string FormatGoverningPartCodes(IEnumerable<string>? governingPartCodes)
        {
            var normalized = NormalizeGoverningPartCodes(governingPartCodes);
            return string.Join(", ", normalized.Select(GetPartTitle));
        }

        private static string BuildOverallStatusRuleText(IEnumerable<string>? governingPartCodes)
            => "Overall PASS requires every qualifying CESM qualification in the 100% population to link to at least one STUD row.";

        private static string? FindFirst(IEnumerable<string> columns, IEnumerable<string> exactMatches, IEnumerable<string> partialMatches)
        {
            var list = columns.ToList();

            foreach (var exact in exactMatches)
            {
                var match = list.FirstOrDefault(c => c.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            foreach (var partial in partialMatches)
            {
                var match = list.FirstOrDefault(c => c.Contains(partial, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return list.FirstOrDefault();
        }

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static string NormalizeObjectName(string? name, string fallback = "")
        {
            var normalized = string.IsNullOrWhiteSpace(name) ? "" : Sanitise(name);
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }

        private static void NormalizeRequest(Rule13ValidationRequest request)
        {
            request.Server = request.Server?.Trim() ?? "";
            request.Database = request.Database?.Trim() ?? "";
            request.Driver = string.IsNullOrWhiteSpace(request.Driver)
                ? "ODBC Driver 17 for SQL Server"
                : request.Driver.Trim();
            request.StudTable = NormalizeObjectName(request.StudTable);
            request.QualTable = NormalizeObjectName(request.QualTable);
            request.CregTable = NormalizeObjectName(request.CregTable);
            request.CrseTable = NormalizeObjectName(request.CrseTable, request.CregTable);
            request.PgTypesText = request.PgTypesText?.Trim() ?? "";
            request.GoverningPartCodes = NormalizeGoverningPartCodes(request.GoverningPartCodes);
        }

        private static void ValidateObjectName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Object name cannot be blank.");
            if (name.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')))
                throw new InvalidOperationException($"Invalid object name '{name}'.");
        }

        private static async Task<int> CountAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private static Rule13ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                var summary = JsonConvert.DeserializeObject<Rule13ValidationSummary>(
                    decoded,
                    new JsonSerializerSettings
                    {
                        ObjectCreationHandling = ObjectCreationHandling.Replace
                    });
                return summary == null ? null : NormalizeSummary(summary);
            }
            catch
            {
                return null;
            }
        }

        private static Rule13ValidationSummary NormalizeSummary(Rule13ValidationSummary summary)
        {
            summary.Database = summary.Database?.Trim() ?? "";
            summary.StudTable = NormalizeObjectName(summary.StudTable, "dbo_CESM");
            summary.QualTable = NormalizeObjectName(summary.QualTable, "dbo_QUAL");
            summary.CregTable = NormalizeObjectName(summary.CregTable, "dbo_STUD");
            summary.CrseTable = NormalizeObjectName(summary.CrseTable, summary.CregTable);
            summary.Timestamp = summary.Timestamp?.Trim() ?? "";
            summary.Status = summary.Status?.Trim() ?? "";
            summary.Warning = string.IsNullOrWhiteSpace(summary.Warning) ? summary.Warning : summary.Warning.Trim();
            summary.Error = string.IsNullOrWhiteSpace(summary.Error) ? summary.Error : summary.Error.Trim();
            summary.PgTypes ??= new List<string>();
            if (summary.PgTypes.Count == 0)
                summary.PgTypes = ParsePgTypes(summary.PgTypesText);

            summary.PgTypesText = string.IsNullOrWhiteSpace(summary.PgTypesText)
                ? string.Join(", ", summary.PgTypes)
                : summary.PgTypesText;
            summary.GoverningPartCodes = NormalizeGoverningPartCodes(summary.GoverningPartCodes);
            summary.GoverningPartCodesText = FormatGoverningPartCodes(summary.GoverningPartCodes);
            summary.OverallStatusRuleText = BuildOverallStatusRuleText(summary.GoverningPartCodes);
            summary.TableLinkageText = BuildTableLinkageText(summary.StudTable, summary.QualTable, summary.CregTable, summary.CrseTable);
            summary.ProcedureSteps = BuildProcedureSteps(summary.StudTable, summary.QualTable, summary.CregTable, summary.CrseTable);
            summary.ReviewRows = NormalizeReviewRows(summary.ReviewRows);

            var partSummaries = summary.PartSummaries ?? new List<Rule13PartSummaryItemViewModel>();
            var aggregateTotalCount = partSummaries.Any()
                ? partSummaries.Sum(x => x.TotalCount)
                : summary.ReviewRows.Count;
            var aggregatePassCount = partSummaries.Any()
                ? partSummaries.Sum(x => x.PassCount)
                : summary.ReviewRows.Count(r => string.Equals(r.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase));
            var aggregateFailCount = partSummaries.Any()
                ? partSummaries.Sum(x => x.FailCount)
                : summary.ReviewRows.Count(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase));

            summary.PartSummaries =
            [
                new Rule13PartSummaryItemViewModel
                {
                    PartCode = ScopeCode,
                    PartTitle = ScopeTitle,
                    PartDescription = ScopeDescription,
                    TotalCount = aggregateTotalCount,
                    PassCount = aggregatePassCount,
                    FailCount = aggregateFailCount,
                    Status = aggregateFailCount == 0 ? "PASS" : "FAIL"
                }
            ];

            summary.TotalValidated = summary.PartSummaries.Sum(x => x.TotalCount);
            summary.PassCount = summary.PartSummaries.Sum(x => x.PassCount);
            summary.FailCount = summary.PartSummaries.Sum(x => x.FailCount);
            summary.ExceptionRate = summary.TotalValidated == 0 ? 0m : Math.Round(summary.FailCount * 100m / summary.TotalValidated, 2);
            summary.Status = DetermineOverallStatus(summary.PartSummaries, summary.GoverningPartCodes);
            summary.DisplayedCount = summary.ReviewRows.Count;
            summary.IsPreviewOnly = summary.TotalValidated > summary.DisplayedCount;
            summary.PreviewLimit = summary.IsPreviewOnly ? summary.DisplayedCount : 0;

            return summary;
        }

        private async Task<Rule13ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule13ValidationSummary summary, string? server)
        {
            var maxPreviewRows = BrowserPreviewRowLimit;
            var looksLikeStoredPreviewSample =
                summary.ReviewRows.Count > 0 &&
                summary.ReviewRows.Count <= maxPreviewRows &&
                summary.TotalValidated > 0;

            if (!summary.IsPreviewOnly &&
                summary.ReviewRows.Count >= summary.TotalValidated &&
                !looksLikeStoredPreviewSample)
                return summary;

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(summary.Database) ||
                string.IsNullOrWhiteSpace(summary.StudTable) ||
                string.IsNullOrWhiteSpace(summary.QualTable) ||
                string.IsNullOrWhiteSpace(summary.CregTable) ||
                string.IsNullOrWhiteSpace(summary.CrseTable))
            {
                return summary;
            }

            try
            {
                var expanded = await AnalyseAsync(
                    new Rule13ValidationRequest
                    {
                        ClientId          = summary.ClientId,
                        RunId             = summary.SavedRunId,
                        Server            = server,
                        Database          = summary.Database,
                        Driver            = "ODBC Driver 17 for SQL Server",
                        StudTable         = summary.StudTable,
                        QualTable         = summary.QualTable,
                        CregTable         = summary.CregTable,
                        CrseTable         = summary.CrseTable,
                        PgTypesText       = summary.PgTypesText,
                        GoverningPartCodes = summary.GoverningPartCodes?.ToList() ?? new List<string>(),
                        // Preserve column mappings and PQM config from the saved run
                        CesmIdCol         = string.IsNullOrWhiteSpace(summary.CesmIdCol)  ? "_001" : summary.CesmIdCol,
                        CesmCodeCol       = string.IsNullOrWhiteSpace(summary.CesmCodeCol) ? "_006" : summary.CesmCodeCol,
                        QualIdCol         = string.IsNullOrWhiteSpace(summary.QualIdCol)   ? "_001" : summary.QualIdCol,
                        QualNameCol       = string.IsNullOrWhiteSpace(summary.QualNameCol)  ? "_003" : summary.QualNameCol,
                        StudIdCol         = string.IsNullOrWhiteSpace(summary.StudIdCol)   ? "_001" : summary.StudIdCol,
                        PqmTable          = summary.PqmTable  ?? "",
                        PqmNameCol        = string.IsNullOrWhiteSpace(summary.PqmNameCol)  ? "Authorised_Qualification_Name" : summary.PqmNameCol,
                        PqmCode1Col       = string.IsNullOrWhiteSpace(summary.PqmCode1Col) ? "CESM_Code"  : summary.PqmCode1Col,
                        PqmCode2Col       = string.IsNullOrWhiteSpace(summary.PqmCode2Col) ? "CESM_Code2" : summary.PqmCode2Col,
                    },
                    includeAllReviewRows: true);

                expanded.Timestamp = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                expanded.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                    ? "Saved Rule 13 results were expanded from the stored preview to the full result set."
                    : $"{summary.Warning} Full saved results were reloaded from the saved Rule 13 configuration.";

                return expanded;
            }
            catch
            {
                return summary;
            }
        }

        private static Rule13ValidationSummary CreateBrowserPreview(Rule13ValidationSummary summary)
        {
            var normalizedRows = NormalizeReviewRows(summary.ReviewRows);
            var previewRows = BuildBrowserPreviewRows(normalizedRows);

            return new Rule13ValidationSummary
            {
                Success = summary.Success,
                FoundationStudentCount = summary.FoundationStudentCount,
                TotalValidated = summary.TotalValidated,
                DisplayedCount = previewRows.Count,
                IsPreviewOnly = summary.TotalValidated > previewRows.Count,
                PreviewLimit = BrowserPreviewRowLimit,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ReviewCount = summary.ReviewCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                StudTable = summary.StudTable,
                QualTable = summary.QualTable,
                CregTable = summary.CregTable,
                CrseTable = summary.CrseTable,
                PgTypesText = summary.PgTypesText,
                PgTypes = summary.PgTypes.ToList(),
                GoverningPartCodes = summary.GoverningPartCodes.ToList(),
                GoverningPartCodesText = summary.GoverningPartCodesText,
                OverallStatusRuleText = summary.OverallStatusRuleText,
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                PartSummaries = (summary.PartSummaries ?? new List<Rule13PartSummaryItemViewModel>())
                    .Select(item => new Rule13PartSummaryItemViewModel
                    {
                        PartCode = item.PartCode,
                        PartTitle = item.PartTitle,
                        PartDescription = item.PartDescription,
                        TotalCount = item.TotalCount,
                        PassCount = item.PassCount,
                        FailCount = item.FailCount,
                        Status = item.Status
                    })
                    .ToList(),
                ReviewRows = previewRows,
                Warning = summary.Warning,
                Error = summary.Error,
                // Preserve PQM and column config so ExpandSavedSummaryIfNeededAsync can re-apply them
                CesmIdCol   = summary.CesmIdCol,
                CesmCodeCol = summary.CesmCodeCol,
                QualIdCol   = summary.QualIdCol,
                QualNameCol = summary.QualNameCol,
                StudIdCol   = summary.StudIdCol,
                PqmTable    = summary.PqmTable,
                PqmNameCol  = summary.PqmNameCol,
                PqmCode1Col = summary.PqmCode1Col,
                PqmCode2Col = summary.PqmCode2Col,
            };
        }

        private static List<Rule13ReviewRowViewModel> BuildBrowserPreviewRows(IEnumerable<Rule13ReviewRowViewModel>? rows)
        {
            var normalizedRows = NormalizeReviewRows(rows);
            return NormalizeReviewRows(
                normalizedRows
                    .OrderBy(row => GetPartSortOrder(row.PartCode))
                    .ThenBy(row => TryParseLong(row.StudentNumber007).HasValue ? 0 : 1)
                    .ThenBy(row => TryParseLong(row.StudentNumber007))
                    .ThenBy(row => row.StudentNumber007)
                    .ThenBy(row => row.QualificationCode001)
                    .ThenBy(row => row.CourseCode030)
                    .Take(BrowserPreviewRowLimit));
        }

        private static void ApplyBrowserPreview(Rule13ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.ReviewRows = preview.ReviewRows;
        }

        private static string DetermineOverallStatus(
            IEnumerable<Rule13PartSummaryItemViewModel>? partSummaries,
            IEnumerable<string>? governingPartCodes)
        {
            var summaries = (partSummaries ?? Enumerable.Empty<Rule13PartSummaryItemViewModel>()).ToList();
            var governingParts = NormalizeGoverningPartCodes(governingPartCodes);

            return governingParts.All(partCode =>
            {
                var summary = summaries.FirstOrDefault(x => string.Equals(x.PartCode, partCode, StringComparison.OrdinalIgnoreCase));
                return string.Equals(summary?.Status, "PASS", StringComparison.OrdinalIgnoreCase);
            })
                ? "PASS"
                : "FAIL";
        }

        private static string DetermineOverallStatus(IEnumerable<Rule13PartSummaryItemViewModel>? partSummaries)
            => DetermineOverallStatus(partSummaries, [ScopeCode]);

        private static List<Rule13ReviewRowViewModel> NormalizeReviewRows(IEnumerable<Rule13ReviewRowViewModel>? rows)
        {
            var normalizedRows = (rows ?? Enumerable.Empty<Rule13ReviewRowViewModel>())
                .ToList();

            for (var i = 0; i < normalizedRows.Count; i++)
            {
                normalizedRows[i].StudentNumber007 = NormalizeRowText(normalizedRows[i].StudentNumber007);
                normalizedRows[i].QualificationCode001 = NormalizeRowText(normalizedRows[i].QualificationCode001);
                normalizedRows[i].FoundationFlag106 = NormalizeRowText(normalizedRows[i].FoundationFlag106);
                normalizedRows[i].QualificationDescription003 = NormalizeRowText(normalizedRows[i].QualificationDescription003);
                normalizedRows[i].QualificationType005 = NormalizeRowText(normalizedRows[i].QualificationType005);
                normalizedRows[i].BridgeQualificationCode001 = NormalizeRowText(normalizedRows[i].BridgeQualificationCode001);
                normalizedRows[i].CourseCode030 = NormalizeRowText(normalizedRows[i].CourseCode030);
                normalizedRows[i].CrseCourseCode030 = NormalizeRowText(normalizedRows[i].CrseCourseCode030);
                normalizedRows[i].FoundationCourse091 = NormalizeRowText(normalizedRows[i].FoundationCourse091);
                normalizedRows[i].StudentType = NormalizeRowText(normalizedRows[i].StudentType);
                normalizedRows[i].NotebookStatus = NormalizeRowText(normalizedRows[i].NotebookStatus);
                normalizedRows[i].ValidationResult = NormalizeRowText(normalizedRows[i].ValidationResult);
                normalizedRows[i].ValidationExplanation = NormalizeRowText(normalizedRows[i].ValidationExplanation);
                normalizedRows[i].PartCode = ScopeCode;
                normalizedRows[i].PartTitle = ScopeTitle;
                normalizedRows[i].PartDescription = ScopeDescription;
            }

            normalizedRows = normalizedRows
                .OrderBy(r => TryParseLong(r.StudentNumber007).HasValue ? 0 : 1)
                .ThenBy(r => TryParseLong(r.StudentNumber007))
                .ThenBy(r => r.StudentNumber007)
                .ThenBy(r => r.QualificationCode001)
                .ThenBy(r => r.CourseCode030)
                .ToList();

            for (var i = 0; i < normalizedRows.Count; i++)
            {
                normalizedRows[i].ValidationNumber = i + 1;
                var hasPqmFail = !string.IsNullOrEmpty(normalizedRows[i].PqmResult) &&
                    string.Equals(normalizedRows[i].PqmResult, "FAIL", StringComparison.OrdinalIgnoreCase);
                var isPass = !hasPqmFail && (
                    IsRule13RowPass(normalizedRows[i]) ||
                    string.Equals(normalizedRows[i].ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedRows[i].NotebookStatus, "VALID", StringComparison.OrdinalIgnoreCase));
                normalizedRows[i].NotebookStatus = isPass ? "VALID" : "INVALID";
                normalizedRows[i].ValidationResult = isPass ? "PASS" : "FAIL";
                normalizedRows[i].ValidationExplanation = string.IsNullOrWhiteSpace(normalizedRows[i].ValidationExplanation)
                    ? BuildValidationExplanation(
                        normalizedRows[i].FoundationFlag106,
                        normalizedRows[i].QualificationCode001,
                        normalizedRows[i].FoundationCourse091)
                    : normalizedRows[i].ValidationExplanation;
            }

            return normalizedRows;
        }

        private static string GetPartTitle(string partCode) => partCode.ToUpperInvariant() switch
        {
            ScopeCode => ScopeTitle,
            "A" => "Part A",
            "B" => "Part B",
            "C" => "Part C",
            _ => partCode
        };

        private static string GetPartDescription(string partCode) => partCode.ToUpperInvariant() switch
        {
            ScopeCode => ScopeDescription,
            _ => ""
        };

        private static int GetPartSortOrder(string? partCode) =>
            Array.IndexOf(PartOrder, (partCode ?? "").Trim().ToUpperInvariant()) switch
            {
                >= 0 and var idx => idx,
                _ => PartOrder.Length
            };

        private static long? TryParseLong(string? value) =>
            long.TryParse(value, out var parsed) ? parsed : null;

        private static string NormalizeRowText(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

        private static bool IsRule13RowPass(Rule13ReviewRowViewModel row) =>
            string.Equals(NormalizeRowText(row.FoundationFlag106), "PASS", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(NormalizeRowText(row.StudentType), out var studentCount) &&
            studentCount >= 1 &&
            !string.IsNullOrWhiteSpace(NormalizeRowText(row.FoundationCourse091));

        private static async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);
    }
}



