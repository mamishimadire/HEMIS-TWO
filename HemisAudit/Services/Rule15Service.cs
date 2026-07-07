using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule15Service : IRule15Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule15Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<Rule15TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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
                    tables.Add(reader.GetString(0));

                return new Rule15TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = FindFirst(tables, ["dbo_QUAL", "QUAL"], ["qual"]),
                    AutoBridgeTable = FindFirst(tables, ["dbo_CRED", "CRED"], ["cred"]),
                    AutoCrseTable = FindFirst(tables, ["dbo_CREG", "CREG"], ["creg"])
                };
            }
            catch (Exception ex)
            {
                return new Rule15TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule15VerifyResult> VerifyTablesAsync(Rule15VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable, request.QualApprovedCol ?? "_004", request.CredQualJoinCol ?? "_001", request.CredCregJoinCol1 ?? "_001", request.CredCregJoinCol2 ?? "_030", request.CregStudentCol ?? "_007");

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var studTable = Sanitise(request.StudTable);
                var bridgeTable = Sanitise(request.BridgeTable);
                var crseTable = Sanitise(request.CrseTable);

                var studCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{studTable}];");
                var bridgeCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{bridgeTable}];");
                var crseCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{crseTable}];");

                await using var command = conn.CreateConfiguredCommand();
                command.CommandText = BuildPopulationCountSql(studTable, bridgeTable, crseTable);
                await using var reader = await command.ExecuteReaderAsync();

                var result = new Rule15VerifyResult
                {
                    Success = true,
                    StudRecordCount = studCount,
                    BridgeRecordCount = bridgeCount,
                    CrseRecordCount = crseCount
                };

                if (await reader.ReadAsync())
                {
                    result.ApprovedQualificationCount = GetInt(reader, 0);
                    result.ApprovedCredentialCount = GetInt(reader, 1);
                    result.RegisteredCredentialCount = GetInt(reader, 2);
                    result.Control1PopulationCount = result.ApprovedCredentialCount;
                    result.Control2PopulationCount = 0;
                    result.Control3PopulationCount = 0;
                }

                return result;
            }
            catch (Exception ex)
            {
                return new Rule15VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule15ValidationSummary> RunValidationAsync(Rule15ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable, request.QualApprovedCol ?? "_004", request.CredQualJoinCol ?? "_001", request.CredCregJoinCol1 ?? "_001", request.CredCregJoinCol2 ?? "_030", request.CregStudentCol ?? "_007");

                // Run once with all rows — used for both save and browser preview
                var summary = await AnalyseAsync(request, includeAllReviewRows: true);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        var summaryToPersist = CloneSummary(summary);
                        summaryToPersist.SavedRunId = null;
                        summary.SavedRunId = await SaveValidationRunAsync(request, summaryToPersist, userEmail, userName, markWorkspaceSaved: false);

                        if (!string.IsNullOrWhiteSpace(userEmail))
                            _pendingValidationCache.ClearPending(15, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        summary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!summary.SavedRunId.HasValue)
                {
                    if (summary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(15, request.ClientId, userEmail!, request, CloneSummary(summary), userName);

                    summary.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                        ? "Rule 15 validation completed. Click Save Workspace to write this validated result to the system database."
                        : summary.Warning;
                }
                else
                {
                    summary.Warning = "The current Rule 15 run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule15ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule15ValidationSummary> GetExportSummaryAsync(Rule15ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public Task<Rule15ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail)
        {
            var pending = _pendingValidationCache.GetPending<Rule15ValidationRequest, Rule15ValidationSummary>(15, clientId, reviewerEmail);
            if (pending == null)
                return Task.FromResult<Rule15ValidationSummary?>(null);

            var preview = CloneSummary(pending.Summary);
            preview.SavedRunId = null;
            preview.Warning = "This Rule 15 validation is still pending. Click Save Workspace to write it to the system database.";
            ApplyBrowserPreview(preview);
            return Task.FromResult<Rule15ValidationSummary?>(preview);
        }

        public Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail)
            => Task.FromResult(_pendingValidationCache.HasPending(15, clientId, reviewerEmail));

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule15WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.DeceasedTable, '') AS BridgeTable,
    ISNULL(vr.StudColumn, '') AS CrseTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 15
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var deserializedSummary = DeserializeSummary(reader.IsDBNull(10) ? null : reader.GetString(10));
            if (deserializedSummary != null && includeSummary)
                ApplyBrowserPreview(deserializedSummary);
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule15WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StudTable = reader.IsDBNull(4) ? "dbo_CRED" : reader.GetString(4),
                BridgeTable = reader.IsDBNull(5) ? "dbo_QUAL" : reader.GetString(5),
                CrseTable = reader.IsDBNull(6) ? "dbo_CREG" : reader.GetString(6),
                QualApprovedCol = deserializedSummary?.QualApprovedCol ?? "_004",
                QualApprovedVal = deserializedSummary?.QualApprovedVal ?? "A",
                CredQualJoinCol = deserializedSummary?.CredQualJoinCol ?? "_001",
                CredCregJoinCol1 = deserializedSummary?.CredCregJoinCol1 ?? "_001",
                CredCregJoinCol2 = deserializedSummary?.CredCregJoinCol2 ?? "_030",
                CregStudentCol = deserializedSummary?.CregStudentCol ?? "_007",
                CurrentStatus = reader.IsDBNull(7) ? "" : reader.GetString(7),
                LastEditedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8),
                LastEditedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                Summary = summary
            };

            if (summary != null)
                workspace.CurrentStatus = summary.Status;

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

        public async Task<Rule15RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 15;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var sourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            var clientId = reader.GetInt32(1);
            summary.ClientId = clientId;
            if (summary.SavedRunId.GetValueOrDefault() <= 0)
                summary.SavedRunId = runId;

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

            var review = new Rule15RunReviewViewModel
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

        public async Task<Rule15WorkspaceSaveResult> SaveWorkspaceAsync(Rule15ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                ValidateRequest(request);

                if (request.RunId.HasValue && request.RunId.Value > 0)
                {
                    await using var connection = await OpenSystemConnectionAsync();
                    var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                    if (!clientId.HasValue || clientId.Value != request.ClientId)
                    {
                        return new Rule15WorkspaceSaveResult
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
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID
  AND ClientID = @ClientID;";
                    command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                    command.Parameters.AddWithValue("@ClientID", request.ClientId);
                    command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                    command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                    command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule15|{request.RunId.Value}|{request.ClientId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                    await command.ExecuteNonQueryAsync();

                    if (!string.IsNullOrWhiteSpace(reviewerEmail))
                        _pendingValidationCache.ClearPending(15, request.ClientId, reviewerEmail);

                    var currentWorkspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                    return new Rule15WorkspaceSaveResult
                    {
                        Success = true,
                        Message = clearedSignoffs > 0
                            ? "Workspace saved. Existing signoffs were removed and the run must be reviewed again."
                            : "Workspace saved and marked for review again.",
                        SignoffsCleared = clearedSignoffs > 0,
                        ClearedSignoffCount = clearedSignoffs,
                        Workspace = currentWorkspace
                    };
                }

                var pending = _pendingValidationCache.GetPending<Rule15ValidationRequest, Rule15ValidationSummary>(15, request.ClientId, reviewerEmail);
                if (pending == null)
                {
                    return new Rule15WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run Rule 15 first so the current workspace is written to the system database."
                    };
                }

                if (!RequestsMatchForPendingSave(request, pending.Request))
                {
                    return new Rule15WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Workspace settings changed after validation. Run Rule 15 again before saving."
                    };
                }

                var summaryToSave = CloneSummary(pending.Summary);
                if (summaryToSave.IsPreviewOnly || summaryToSave.ReviewRows.Count < summaryToSave.TotalValidated)
                {
                    summaryToSave = await AnalyseAsync(pending.Request, includeAllReviewRows: true);
                }

                summaryToSave.SavedRunId = null;
                var savedRunId = await SaveValidationRunAsync(pending.Request, summaryToSave, reviewerEmail, reviewerName ?? pending.ReviewerName, markWorkspaceSaved: true);
                _pendingValidationCache.ClearPending(15, request.ClientId, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule15WorkspaceSaveResult
                {
                    Success = true,
                    Message = $"Workspace saved as Run #{savedRunId}. Sign off this saved workspace when you are ready.",
                    SignoffsCleared = false,
                    ClearedSignoffCount = 0,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule15WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule15WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule15WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash = await GetValidationRecordHashAsync(connection, runId);

                await using var markEdit = connection.CreateConfiguredCommand();
                markEdit.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = NULL,
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID;";
                markEdit.Parameters.AddWithValue("@RunID", runId);
                markEdit.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule15|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(15, clientId.Value, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule15WorkspaceSaveResult
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
                return new Rule15WorkspaceSaveResult
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
                throw new InvalidOperationException("The selected Rule 15 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off a Rule 15 run.");

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
                throw new InvalidOperationException("The selected Rule 15 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule15ValidationRequest request)
        {
            ValidateRequest(request);

            var credTable = Sanitise(request.StudTable);
            var qualTable = Sanitise(request.BridgeTable);
            var cregTable = Sanitise(request.CrseTable);
            var aCol  = Sanitise(request.QualApprovedCol ?? "_004");
            var aVal  = request.QualApprovedVal ?? "A";
            var qjCol = Sanitise(request.CredQualJoinCol ?? "_001");
            var cj1   = Sanitise(request.CredCregJoinCol1 ?? "_001");
            var cj2   = Sanitise(request.CredCregJoinCol2 ?? "_030");
            var sCol  = Sanitise(request.CregStudentCol ?? "_007");

            var sql = $@"-- HEMIS RULE 15: COURSE CREDENTIALS VALIDATION
-- Step 1: Extract 100% CRED population from [{credTable}]
-- Step 2: Extract approved qualifications from [{qualTable}] where {aCol} = '{aVal}'
-- Step 3: Extract CREG registration population from [{cregTable}]
-- Step 4: Validate CRED -> QUAL -> CREG linkage (PASS when both links found)

{BuildRule15PrepSql(credTable, qualTable, cregTable, aCol, aVal, qjCol, cj1, cj2, sCol, true, true, true, true)}

-- Step 5: Full CRED population
SELECT * FROM #Rule15_CRED_Population ORDER BY [Extract_Number];

-- Step 6: Full validation result
SELECT * FROM #Rule15_Validation ORDER BY [Extract_Number];

-- Step 7: Exceptions only
SELECT * FROM #Rule15_Validation WHERE [Validation_Result] = 'FAIL' ORDER BY [Extract_Number];

-- Step 8: Summary
SELECT
    (SELECT COUNT(1) FROM #Rule15_Approved_QUAL)   AS [Approved_Qualifications],
    (SELECT COUNT(1) FROM #Rule15_CRED_Population) AS [Total_Course_Credentials],
    SUM(CASE WHEN [Validation_Result] = 'PASS' THEN 1 ELSE 0 END) AS [PASS_Count],
    SUM(CASE WHEN [Validation_Result] = 'FAIL' THEN 1 ELSE 0 END) AS [FAIL_Count],
    CAST(SUM(CASE WHEN [Validation_Result] = 'FAIL' THEN 1 ELSE 0 END) * 100.0
        / NULLIF(COUNT(*), 0) AS DECIMAL(5,2)) AS [Exception_Rate_Pct]
FROM #Rule15_Validation;

DROP TABLE IF EXISTS #Rule15_CRED_Population;
DROP TABLE IF EXISTS #Rule15_Approved_QUAL;
DROP TABLE IF EXISTS #Rule15_CREG_Population;
DROP TABLE IF EXISTS #Rule15_Validation;";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule15ValidationSummary> AnalyseAsync(Rule15ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // StudTable = CRED (primary), BridgeTable = QUAL (filter), CrseTable = CREG (registration)
            var credTable        = Sanitise(request.StudTable);
            var qualTable        = Sanitise(request.BridgeTable);
            var cregTable        = Sanitise(request.CrseTable);
            var qualApprovedCol  = Sanitise(string.IsNullOrWhiteSpace(request.QualApprovedCol)  ? "_004" : request.QualApprovedCol);
            var qualApprovedVal  = request.QualApprovedVal ?? "A";
            var credQualJoinCol  = Sanitise(string.IsNullOrWhiteSpace(request.CredQualJoinCol)  ? "_001" : request.CredQualJoinCol);
            var credCregJoinCol1 = Sanitise(string.IsNullOrWhiteSpace(request.CredCregJoinCol1) ? "_001" : request.CredCregJoinCol1);
            var credCregJoinCol2 = Sanitise(string.IsNullOrWhiteSpace(request.CredCregJoinCol2) ? "_030" : request.CredCregJoinCol2);
            var cregStudentCol   = Sanitise(string.IsNullOrWhiteSpace(request.CregStudentCol)   ? "_007" : request.CregStudentCol);

            // Check which optional display columns exist
            var credCols = new HashSet<string>(await GetColumnsOnConnectionAsync(conn, credTable), StringComparer.OrdinalIgnoreCase);
            var qualCols = new HashSet<string>(await GetColumnsOnConnectionAsync(conn, qualTable), StringComparer.OrdinalIgnoreCase);
            bool hasCredFiller  = credCols.Contains("_065");
            bool hasCredCredit1 = credCols.Contains("_036");
            bool hasCredCredit2 = credCols.Contains("_050");
            bool hasQualName    = qualCols.Contains("_003");

            var credCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{credTable}];");
            var qualCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{qualTable}];");
            var cregCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{cregTable}];");

            var (approvedQualCount, credPopCount, passCount, failCount) =
                await RunRule15PrepAndGetCountsAsync(conn, credTable, qualTable, cregTable,
                    qualApprovedCol, qualApprovedVal, credQualJoinCol, credCregJoinCol1, credCregJoinCol2, cregStudentCol,
                    hasCredFiller, hasCredCredit1, hasCredCredit2, hasQualName);

            var reviewRows = await LoadControlRowsFromPrepAsync(conn, includeAllReviewRows ? null : BrowserPreviewRowLimit);
            reviewRows = NormalizeReviewRows(reviewRows);

            var totalValidated = credPopCount;
            var isPreviewOnly  = !includeAllReviewRows && totalValidated > reviewRows.Count;

            var controlSummaries = BuildControlSummaries(credPopCount, passCount, qualApprovedCol, qualApprovedVal, credQualJoinCol, credCregJoinCol1, credCregJoinCol2);

            return new Rule15ValidationSummary
            {
                Success = true,
                StudRecordCount = qualCount,
                BridgeRecordCount = credCount,
                CrseRecordCount = cregCount,
                ApprovedQualificationCount = approvedQualCount,
                ApprovedCredentialCount = credPopCount,
                RegisteredCredentialCount = passCount,
                TotalRequested = totalValidated,
                TotalValidated = totalValidated,
                DisplayedCount = reviewRows.Count,
                IsPreviewOnly = isPreviewOnly,
                PreviewLimit = isPreviewOnly ? BrowserPreviewRowLimit : 0,
                PassCount = passCount,
                FailCount = failCount,
                ExceptionRate = totalValidated == 0 ? 0m : Math.Round(failCount * 100m / totalValidated, 2),
                Status = failCount == 0 ? "PASS" : "FAIL",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                StudTable = request.StudTable,
                BridgeTable = request.BridgeTable,
                CrseTable = request.CrseTable,
                QualApprovedCol = qualApprovedCol,
                QualApprovedVal = qualApprovedVal,
                CredQualJoinCol = credQualJoinCol,
                CredCregJoinCol1 = credCregJoinCol1,
                CredCregJoinCol2 = credCregJoinCol2,
                CregStudentCol = cregStudentCol,
                TableLinkageText = $"{request.StudTable} -> {request.BridgeTable} -> {request.CrseTable}",
                RuleModeText = "100% population testing of CRED rows (CRED -> QUAL approval filter -> CREG registration check)",
                ProcedureSteps = BuildProcedureSteps(request.StudTable, request.BridgeTable, request.CrseTable),
                ClientId = request.ClientId,
                ControlSummaries = controlSummaries,
                ReviewRows = reviewRows,
                Warning = null
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule15ValidationRequest request, Rule15ValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 15);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 15);
            var failRows = summary.ReviewRows.Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(
    ClientID, UserID, RuleNumber, RuleName, Status, TotalRecords, PassCount, FailCount, ExceptionRate, RunTimestamp,
    HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
    ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, WorkspaceSavedAt, IsCurrent
)
VALUES
(
    @ClientID, @UserID, 15, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @BridgeTable, @CrseTable, NULL,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, @WorkspaceSavedAt, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Course Credentials Validation");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@BridgeTable", request.BridgeTable);
            command.Parameters.AddWithValue("@CrseTable", request.CrseTable);
            var persistedSummary = CloneSummary(summary);
            persistedSummary.SavedRunId = summary.SavedRunId;
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(failRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persistedSummary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
            command.Parameters.AddWithValue("@WorkspaceSavedAt", markWorkspaceSaved ? DateTime.UtcNow : (object)DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET RecordHash = @RecordHash
WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule15|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private static string BuildPopulationCountSql(
            string credTable, string qualTable, string cregTable,
            string qualApprovedCol = "_004", string qualApprovedVal = "A",
            string credQualJoinCol = "_001", string credCregJoinCol1 = "_001", string credCregJoinCol2 = "_030") => $@"
WITH CredPop AS (
    SELECT DISTINCT
        CONVERT(nvarchar(255), CRED.[{credCregJoinCol1}]) AS CRED__LINK1,
        CONVERT(nvarchar(255), CRED.[{credCregJoinCol2}]) AS CRED__LINK2,
        CONVERT(nvarchar(255), CRED.[{credQualJoinCol}])  AS CRED__QUAL_KEY
    FROM [{credTable}] CRED
    WHERE CRED.[{credCregJoinCol1}] IS NOT NULL AND CRED.[{credCregJoinCol2}] IS NOT NULL
),
ApprQual AS (
    SELECT DISTINCT CONVERT(nvarchar(255), QUAL.[{credQualJoinCol}]) AS QUAL__LINK
    FROM [{qualTable}] QUAL
    WHERE ISNULL(CONVERT(nvarchar(255), QUAL.[{qualApprovedCol}]), '') = '{qualApprovedVal}'
),
CregSmp AS (
    SELECT CONVERT(nvarchar(255), CREG.[{credCregJoinCol1}]) AS CREG__LINK1,
           CONVERT(nvarchar(255), CREG.[{credCregJoinCol2}]) AS CREG__LINK2
    FROM [{cregTable}] CREG
    WHERE CREG.[{credCregJoinCol1}] IS NOT NULL AND CREG.[{credCregJoinCol2}] IS NOT NULL
    GROUP BY CREG.[{credCregJoinCol1}], CREG.[{credCregJoinCol2}]
)
SELECT
    (SELECT COUNT(*) FROM ApprQual) AS ApprovedQualificationCount,
    COUNT(1) AS ApprovedCredentialCount,
    SUM(CASE WHEN CS.CREG__LINK1 IS NOT NULL THEN 1 ELSE 0 END) AS RegisteredCredentialCount
FROM CredPop CP
INNER JOIN ApprQual AQ ON AQ.QUAL__LINK = CP.CRED__QUAL_KEY
LEFT JOIN CregSmp CS ON CS.CREG__LINK1 = CP.CRED__LINK1 AND CS.CREG__LINK2 = CP.CRED__LINK2;";

        // ── New prep-SQL pattern (like Rule 12) ───────────────────────────────────

        private static string BuildRule15PrepSql(
            string credTable, string qualTable, string cregTable,
            string qualApprovedCol, string qualApprovedVal,
            string credQualJoinCol, string credCregJoinCol1, string credCregJoinCol2, string cregStudentCol,
            bool hasCredFiller, bool hasCredCredit1, bool hasCredCredit2, bool hasQualName)
        {
            var credFillerSel  = hasCredFiller  ? "    CONVERT(NVARCHAR(255), CRED.[_065]) AS [Filler1]," : "    NULL AS [Filler1],";
            var credCredit1Sel = hasCredCredit1 ? "    CONVERT(NVARCHAR(255), CRED.[_036]) AS [Course_Level_Credit_Value]," : "    NULL AS [Course_Level_Credit_Value],";
            var credCredit2Sel = hasCredCredit2 ? "    CONVERT(NVARCHAR(255), CRED.[_050]) AS [Completed_Research_Course_Credit_Value]" : "    NULL AS [Completed_Research_Course_Credit_Value]";
            var qualNameSel    = hasQualName    ? $"    CONVERT(NVARCHAR(500), QUAL.[_003]) AS [Qualification_Name_Designator]," : "    NULL AS [Qualification_Name_Designator],";
            var ctrlLabel = $"CRED.{credCregJoinCol1} AND QUAL.{qualApprovedCol}=''{qualApprovedVal}'' AND CRED.{credCregJoinCol1}=CREG.{credCregJoinCol1} AND CRED.{credCregJoinCol2}=CREG.{credCregJoinCol2}";

            return $@"
DROP TABLE IF EXISTS #Rule15_CRED_Extracted_Population;
DROP TABLE IF EXISTS #Rule15_Approved_Qualifications;
DROP TABLE IF EXISTS #Rule15_CREG_Extracted_Population;
DROP TABLE IF EXISTS #Rule15_Validation;

-- STEP 1: Extract 100% CRED population
SELECT DISTINCT
    ROW_NUMBER() OVER (ORDER BY
        UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), CRED.[{credCregJoinCol1}])))),
        UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), CRED.[{credCregJoinCol2}]))))
    ) AS [Extract_Number],
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), CRED.[{credCregJoinCol1}])))) AS [Qualification_Code],
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), CRED.[{credCregJoinCol2}])))) AS [Course_Code],
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), CRED.[{credQualJoinCol}])))) AS [CRED__QUAL_KEY],
{credFillerSel}
{credCredit1Sel}
{credCredit2Sel}
INTO #Rule15_CRED_Extracted_Population
FROM [{credTable}] CRED
WHERE CRED.[{credCregJoinCol1}] IS NOT NULL
  AND CRED.[{credCregJoinCol2}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(NVARCHAR(255), CRED.[{credCregJoinCol1}]))) <> ''
  AND LTRIM(RTRIM(CONVERT(NVARCHAR(255), CRED.[{credCregJoinCol2}]))) <> '';

CREATE INDEX IX_Rule15_CRED ON #Rule15_CRED_Extracted_Population ([Qualification_Code], [Course_Code]);

-- STEP 2: Extract approved QUAL population
SELECT DISTINCT
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), QUAL.[{credQualJoinCol}])))) AS [QUAL__JOIN_KEY],
{qualNameSel}
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), QUAL.[{qualApprovedCol}])))) AS [Approval_Status]
INTO #Rule15_Approved_Qualifications
FROM [{qualTable}] QUAL
WHERE UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), QUAL.[{qualApprovedCol}])))) = '{qualApprovedVal}'
  AND QUAL.[{credQualJoinCol}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(NVARCHAR(255), QUAL.[{credQualJoinCol}]))) <> '';

CREATE INDEX IX_Rule15_QUAL ON #Rule15_Approved_Qualifications ([QUAL__JOIN_KEY]);

-- STEP 3: Extract CREG registration population
SELECT
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), CREG.[{credCregJoinCol1}])))) AS [CREG__JOIN_KEY1],
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), CREG.[{credCregJoinCol2}])))) AS [CREG__JOIN_KEY2],
    MIN(CONVERT(NVARCHAR(255), CREG.[{cregStudentCol}])) AS [First_Student_Number],
    COUNT(DISTINCT CONVERT(NVARCHAR(255), CREG.[{cregStudentCol}])) AS [Registered_Student_Count]
INTO #Rule15_CREG_Extracted_Population
FROM [{cregTable}] CREG
WHERE CREG.[{credCregJoinCol1}] IS NOT NULL
  AND CREG.[{credCregJoinCol2}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(NVARCHAR(255), CREG.[{credCregJoinCol1}]))) <> ''
  AND LTRIM(RTRIM(CONVERT(NVARCHAR(255), CREG.[{credCregJoinCol2}]))) <> ''
GROUP BY
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), CREG.[{credCregJoinCol1}])))),
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), CREG.[{credCregJoinCol2}]))));

CREATE INDEX IX_Rule15_CREG ON #Rule15_CREG_Extracted_Population ([CREG__JOIN_KEY1], [CREG__JOIN_KEY2]);

-- STEP 4: Validate CRED -> QUAL -> CREG
SELECT
    CP.[Extract_Number],
    1 AS [Control_Sort],
    'Control_1' AS [Control_Type],
    'CONTROL 1: {ctrlLabel}' AS [Control_Label],
    CP.[Qualification_Code],
    CP.[Course_Code],
    AQ.[Qualification_Name_Designator],
    AQ.[Approval_Status],
    CP.[Filler1],
    CP.[Course_Level_Credit_Value],
    CP.[Completed_Research_Course_Credit_Value],
    CREG.[First_Student_Number],
    CREG.[Registered_Student_Count],
    CASE WHEN CREG.[CREG__JOIN_KEY1] IS NOT NULL THEN 'FOUND' ELSE 'NOT FOUND' END AS [CREG__MATCH],
    CASE
        WHEN AQ.[QUAL__JOIN_KEY] IS NULL     THEN 'FAIL'
        WHEN CREG.[CREG__JOIN_KEY1] IS NULL  THEN 'FAIL'
        ELSE 'PASS'
    END AS [Validation_Result],
    CASE
        WHEN AQ.[QUAL__JOIN_KEY] IS NULL
            THEN 'Qualification code in CRED is not approved or does not exist in QUAL.'
        WHEN CREG.[CREG__JOIN_KEY1] IS NULL
            THEN 'Credential qualification/course combination does not exist in CREG.'
        ELSE 'Approved qualification and matching CREG registration found.'
    END AS [Validation_Reason]
INTO #Rule15_Validation
FROM #Rule15_CRED_Extracted_Population CP
LEFT JOIN #Rule15_Approved_Qualifications AQ   ON AQ.[QUAL__JOIN_KEY]  = CP.[CRED__QUAL_KEY]
LEFT JOIN #Rule15_CREG_Extracted_Population CREG
    ON CREG.[CREG__JOIN_KEY1] = CP.[Qualification_Code]
   AND CREG.[CREG__JOIN_KEY2] = CP.[Course_Code];";
        }

        private static string BuildRule15CountAfterPrepSql() => @"
SELECT
    (SELECT COUNT(1) FROM #Rule15_Approved_Qualifications)     AS ApprovedQualCount,
    (SELECT COUNT(1) FROM #Rule15_CRED_Extracted_Population)   AS CredPopCount,
    SUM(CASE WHEN [Validation_Result] = 'PASS' THEN 1 ELSE 0 END) AS PassCount,
    SUM(CASE WHEN [Validation_Result] = 'FAIL' THEN 1 ELSE 0 END) AS FailCount
FROM #Rule15_Validation;";

        private static async Task<(int approvedQualCount, int credPopCount, int passCount, int failCount)>
            RunRule15PrepAndGetCountsAsync(
                SqlConnection conn,
                string credTable, string qualTable, string cregTable,
                string qualApprovedCol, string qualApprovedVal,
                string credQualJoinCol, string credCregJoinCol1, string credCregJoinCol2, string cregStudentCol,
                bool hasCredFiller, bool hasCredCredit1, bool hasCredCredit2, bool hasQualName)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SET NOCOUNT ON;\n"
                + BuildRule15PrepSql(credTable, qualTable, cregTable, qualApprovedCol, qualApprovedVal,
                    credQualJoinCol, credCregJoinCol1, credCregJoinCol2, cregStudentCol,
                    hasCredFiller, hasCredCredit1, hasCredCredit2, hasQualName)
                + "\n" + BuildRule15CountAfterPrepSql();
            cmd.CommandTimeout = 300;
            await using var reader = await cmd.ExecuteReaderAsync();
            do
            {
                if (reader.FieldCount >= 4 && await reader.ReadAsync())
                    return (GetInt(reader, 0), GetInt(reader, 1), GetInt(reader, 2), GetInt(reader, 3));
            }
            while (await reader.NextResultAsync());
            return (0, 0, 0, 0);
        }

        private static async Task<List<Rule15ValidationRowRecord>> LoadControlRowsFromPrepAsync(
            SqlConnection conn, int? maxRows)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            var top = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value} " : "";
            cmd.CommandText = $"SELECT {top}* FROM #Rule15_Validation ORDER BY [Extract_Number];";

            await using var reader = await cmd.ExecuteReaderAsync();
            var rows = new List<Rule15ValidationRowRecord>();
            while (await reader.ReadAsync())
            {
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                rows.Add(new Rule15ValidationRowRecord
                {
                    ValidationNumber = rows.Count + 1,
                    ControlType = ReadValue(displayValues, "Control_Type"),
                    ControlLabel = ReadValue(displayValues, "Control_Label"),
                    ValidationResult = ReadValue(displayValues, "Validation_Result"),
                    ValidationExplanation = ReadValue(displayValues, "Validation_Reason"),
                    DisplayValues = displayValues
                });

                EnrichRule15DisplayValues(rows[^1]);
            }
            return rows;
        }

        private static async Task<List<string>> GetColumnsOnConnectionAsync(SqlConnection conn, string tableName)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @T ORDER BY ORDINAL_POSITION;";
            cmd.Parameters.AddWithValue("@T", tableName);
            await using var reader = await cmd.ExecuteReaderAsync();
            var cols = new List<string>();
            while (await reader.ReadAsync())
                if (!reader.IsDBNull(0)) cols.Add(reader.GetString(0));
            return cols;
        }

        private static List<Rule15ControlSummaryItemViewModel> BuildControlSummaries(
            int approvedCredentialCount, int registeredCredentialCount,
            string qualApprovedCol = "_004", string qualApprovedVal = "A",
            string credQualJoinCol = "_001", string credCregJoinCol1 = "_001", string credCregJoinCol2 = "_030")
        {
            return new List<Rule15ControlSummaryItemViewModel>
            {
                BuildControlSummary(
                    "Control_1",
                    "Control 1",
                    $"CRED.{credCregJoinCol1} AND QUAL.{qualApprovedCol}='{qualApprovedVal}' AND CRED.{credCregJoinCol1}=CREG.{credCregJoinCol1} AND CRED.{credCregJoinCol2}=CREG.{credCregJoinCol2}",
                    approvedCredentialCount,
                    registeredCredentialCount)
            };
        }

        private static Rule15ControlSummaryItemViewModel BuildControlSummary(
            string controlType,
            string controlLabel,
            string criteriaText,
            int totalCount,
            int passCount)
        {
            var failCount = Math.Max(totalCount - passCount, 0);
            return new Rule15ControlSummaryItemViewModel
            {
                ControlType = controlType,
                ControlLabel = controlLabel,
                CriteriaText = criteriaText,
                RequestedCount = totalCount,
                AvailableCount = totalCount,
                AchievedCount = totalCount,
                TotalCount = totalCount,
                PassCount = passCount,
                FailCount = failCount,
                Status = failCount == 0 ? "PASS" : "FAIL"
            };
        }

        private static List<Rule15ValidationRowRecord> NormalizeReviewRows(IEnumerable<Rule15ValidationRowRecord>? rows)
        {
            var normalized = (rows ?? Enumerable.Empty<Rule15ValidationRowRecord>())
                .Select((row, index) =>
                {
                    row.ValidationNumber = index + 1;
                    return row;
                })
                .ToList();

            return normalized;
        }

        private async Task<Rule15ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule15ValidationSummary summary, string? server)
        {
            var looksLikeStoredPreviewSample =
                summary.ReviewRows.Count > 0 &&
                summary.ReviewRows.Count <= BrowserPreviewRowLimit &&
                summary.TotalValidated > 0;

            if (!summary.IsPreviewOnly &&
                summary.ReviewRows.Count >= summary.TotalValidated &&
                !looksLikeStoredPreviewSample)
            {
                return summary;
            }

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(summary.Database) ||
                string.IsNullOrWhiteSpace(summary.StudTable) ||
                string.IsNullOrWhiteSpace(summary.BridgeTable) ||
                string.IsNullOrWhiteSpace(summary.CrseTable))
            {
                return summary;
            }

            try
            {
                var expanded = await AnalyseAsync(
                    new Rule15ValidationRequest
                    {
                        ClientId = summary.ClientId,
                        RunId = summary.SavedRunId,
                        Server = server,
                        Database = summary.Database,
                        Driver = "ODBC Driver 17 for SQL Server",
                        StudTable = summary.StudTable,
                        BridgeTable = summary.BridgeTable,
                        CrseTable = summary.CrseTable
                    },
                    includeAllReviewRows: true);

                expanded.Timestamp = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                expanded.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                    ? "Saved Rule 15 results were expanded from the stored browser preview to the full result set."
                    : $"{summary.Warning} Full saved results were reloaded from the saved Rule 15 configuration.";

                return expanded;
            }
            catch
            {
                return summary;
            }
        }

        private static Rule15ValidationSummary CloneSummary(Rule15ValidationSummary summary)
        {
            return new Rule15ValidationSummary
            {
                Success = summary.Success,
                StudRecordCount = summary.StudRecordCount,
                BridgeRecordCount = summary.BridgeRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                UnfulfilledPopulationCount = summary.UnfulfilledPopulationCount,
                TotalRequested = summary.TotalRequested,
                TotalValidated = summary.TotalValidated,
                DisplayedCount = summary.DisplayedCount,
                IsPreviewOnly = summary.IsPreviewOnly,
                PreviewLimit = summary.PreviewLimit,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                StudTable = summary.StudTable,
                BridgeTable = summary.BridgeTable,
                CrseTable = summary.CrseTable,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule15ControlSummaryItemViewModel
                    {
                        ControlType = item.ControlType,
                        ControlLabel = item.ControlLabel,
                        CriteriaText = item.CriteriaText,
                        RequestedCount = item.RequestedCount,
                        AvailableCount = item.AvailableCount,
                        AchievedCount = item.AchievedCount,
                        TotalCount = item.TotalCount,
                        PassCount = item.PassCount,
                        FailCount = item.FailCount,
                        Status = item.Status
                    })
                    .ToList(),
                ReviewRows = summary.ReviewRows
                    .Select(CloneReviewRow)
                    .ToList(),
                Warning = summary.Warning,
                Error = summary.Error
            };
        }

        private static Rule15ValidationRowRecord CloneReviewRow(Rule15ValidationRowRecord row)
        {
            return new Rule15ValidationRowRecord
            {
                ValidationNumber = row.ValidationNumber,
                ControlType = row.ControlType,
                ControlLabel = row.ControlLabel,
                ValidationResult = row.ValidationResult,
                ValidationExplanation = row.ValidationExplanation,
                DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static Rule15ValidationSummary CreateBrowserPreview(Rule15ValidationSummary summary)
        {
            var perControlLimit = Math.Max(BrowserPreviewRowLimit / 3, 1);
            var previewRows = summary.ReviewRows
                .GroupBy(row => row.ControlType, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => GetControlSort(group.Key))
                .SelectMany(group => group.OrderBy(row => row.ValidationNumber).Take(perControlLimit))
                .Take(BrowserPreviewRowLimit)
                .ToList();

            return new Rule15ValidationSummary
            {
                Success = summary.Success,
                StudRecordCount = summary.StudRecordCount,
                BridgeRecordCount = summary.BridgeRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                UnfulfilledPopulationCount = summary.UnfulfilledPopulationCount,
                TotalRequested = summary.TotalRequested,
                TotalValidated = summary.TotalValidated,
                DisplayedCount = previewRows.Count,
                IsPreviewOnly = summary.TotalValidated > previewRows.Count,
                PreviewLimit = summary.TotalValidated > previewRows.Count ? previewRows.Count : 0,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                StudTable = summary.StudTable,
                BridgeTable = summary.BridgeTable,
                CrseTable = summary.CrseTable,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule15ControlSummaryItemViewModel
                    {
                        ControlType = item.ControlType,
                        ControlLabel = item.ControlLabel,
                        CriteriaText = item.CriteriaText,
                        RequestedCount = item.RequestedCount,
                        AvailableCount = item.AvailableCount,
                        AchievedCount = item.AchievedCount,
                        TotalCount = item.TotalCount,
                        PassCount = item.PassCount,
                        FailCount = item.FailCount,
                        Status = item.Status
                    })
                    .ToList(),
                ReviewRows = previewRows,
                Warning = summary.Warning,
                Error = summary.Error
            };
        }

        private static void ApplyBrowserPreview(Rule15ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.ReviewRows = preview.ReviewRows;
        }

        private static int GetControlSort(string? controlType) => controlType switch
        {
            "Control_1" => 1,
            _ => 99
        };

        private static List<string> BuildProcedureSteps(string qualTable, string credTable, string cregTable) =>
            new()
            {
                $"Filter {qualTable} to approved qualifications where _004 = 'A'.",
                $"Join approved qualifications to {credTable} on _001 to retrieve every credential course row.",
                $"For each approved credential row, test whether a matching {cregTable} row exists on _001 and _030.",
                "Mark rows PASS when a matching registration exists and FAIL when no registration exists.",
                "Return the full approved-credential population; no sampling is applied."
            };

        private async Task EnsureColumnsExistAsync(
            string server, string database, string driver, string studTable, string bridgeTable, string crseTable,
            string qualApprovedCol = "_004", string credQualJoinCol = "_001",
            string credCregJoinCol1 = "_001", string credCregJoinCol2 = "_030", string cregStudentCol = "_007")
        {
            // studTable=CRED, bridgeTable=QUAL, crseTable=CREG
            var credColumns = await GetTableColumnsAsync(server, database, driver, studTable);
            var qualColumns = await GetTableColumnsAsync(server, database, driver, bridgeTable);
            var cregColumns = await GetTableColumnsAsync(server, database, driver, crseTable);

            EnsureHasColumns(studTable, credColumns, credQualJoinCol, credCregJoinCol1, credCregJoinCol2);
            EnsureHasColumns(bridgeTable, qualColumns, credQualJoinCol, qualApprovedCol);
            EnsureHasColumns(crseTable, cregColumns, credCregJoinCol1, credCregJoinCol2, cregStudentCol);
        }

        public Task<List<string>> GetTableColumnsListAsync(string server, string database, string driver, string tableName)
            => GetTableColumnsAsync(server, database, driver, tableName);

        private async Task<List<string>> GetTableColumnsAsync(string server, string database, string driver, string tableName)
        {
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
                    columns.Add(reader.GetString(0));
            }

            return columns;
        }

        private static void EnsureHasColumns(string tableName, IReadOnlyCollection<string> availableColumns, params string[] requiredColumns)
        {
            var missing = requiredColumns
                .Where(required => !availableColumns.Contains(required, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (missing.Count > 0)
                throw new InvalidOperationException($"Table {tableName} is missing required column(s): {string.Join(", ", missing)}.");
        }

        private static void ValidateRequest(Rule15ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable))
                throw new InvalidOperationException("QUAL table is required.");
            if (string.IsNullOrWhiteSpace(request.BridgeTable))
                throw new InvalidOperationException("CRED table is required.");
            if (string.IsNullOrWhiteSpace(request.CrseTable))
                throw new InvalidOperationException("CREG table is required.");

            ValidateObjectName(request.StudTable);
            ValidateObjectName(request.BridgeTable);
            ValidateObjectName(request.CrseTable);
        }

        private static void ValidateRequest(Rule15VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable))
                throw new InvalidOperationException("QUAL table is required.");
            if (string.IsNullOrWhiteSpace(request.BridgeTable))
                throw new InvalidOperationException("CRED table is required.");
            if (string.IsNullOrWhiteSpace(request.CrseTable))
                throw new InvalidOperationException("CREG table is required.");

            ValidateObjectName(request.StudTable);
            ValidateObjectName(request.BridgeTable);
            ValidateObjectName(request.CrseTable);
        }

        private static void ValidateObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Table or column name is required.");

            foreach (var bad in new[] { ";", "'", "\"", "--", "/*", "*/" })
            {
                if (value.Contains(bad, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsafe table or column name was provided.");
            }
        }

        private static string? FindFirst(IEnumerable<string> values, string[] exactMatches, string[] containsMatches)
        {
            foreach (var exact in exactMatches)
            {
                var match = values.FirstOrDefault(c => c.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            foreach (var fragment in containsMatches)
            {
                var match = values.FirstOrDefault(c => c.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return values.FirstOrDefault();
        }

        private async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var countCommand = connection.CreateConfiguredCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCommand.Parameters.AddWithValue("@RunID", runId);
            var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCommand.Parameters.AddWithValue("@RunID", runId);
            await deleteCommand.ExecuteNonQueryAsync();

            await using var updateCommand = connection.CreateConfiguredCommand();
            updateCommand.CommandText = "UPDATE dbo.ValidationRuns SET Status = 'Needs Review' WHERE RunID = @RunID;";
            updateCommand.Parameters.AddWithValue("@RunID", runId);
            await updateCommand.ExecuteNonQueryAsync();

            return existingCount;
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

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static async Task<int> CountAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private static Rule15ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule15ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

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

        private static void EnrichRule15DisplayValues(Rule15ValidationRowRecord row)
        {
            var values = row.DisplayValues;
            var isPass        = string.Equals(ReadValue(values, "Validation_Result"), "PASS", StringComparison.OrdinalIgnoreCase);
            var approvalStatus = ReadValue(values, "Approval_Status");
            var qualCode       = FormatRule15ColumnValue(ReadValue(values, "Qualification_Code"));
            var courseCode     = FormatRule15ColumnValue(ReadValue(values, "Course_Code"));
            var firstStudent   = FormatRule15ColumnValue(ReadValue(values, "First_Student_Number"));
            var cregMatch      = ReadValue(values, "CREG__MATCH");
            var controlLabel   = ReadValue(values, "Control_Label");
            var isQualFound    = !string.IsNullOrWhiteSpace(approvalStatus);

            string qualCriteriaMessage, registrationMessage, validationExplanation, finalResultMessage;
            if (isPass)
            {
                qualCriteriaMessage   = $"'{approvalStatus}': FOUND";
                registrationMessage   = $"FOUND — QualCode='{qualCode}', CourseCode='{courseCode}', Student='{firstStudent}'.";
                validationExplanation = $"QUAL Approval_Status='{approvalStatus}': FOUND | CREG: FOUND ('{qualCode}','{courseCode}') | Student: '{firstStudent}'";
                finalResultMessage    = "Passed: CRED credential found in approved QUAL and matched in CREG.";
            }
            else if (!isQualFound)
            {
                qualCriteriaMessage   = "NOT FOUND in approved QUAL";
                registrationMessage   = "No CREG check — qual not approved.";
                validationExplanation = $"QUAL approval: NOT FOUND — CRED Qualification_Code '{qualCode}' not in approved QUAL.";
                finalResultMessage    = "Failed: CRED qualification not found in approved QUAL.";
            }
            else
            {
                qualCriteriaMessage   = $"'{approvalStatus}': FOUND";
                registrationMessage   = $"NOT FOUND — no CREG match for QualCode='{qualCode}', CourseCode='{courseCode}'.";
                validationExplanation = $"QUAL Approval_Status='{approvalStatus}': FOUND | CREG registration: NOT FOUND (no match for '{qualCode}'+'{courseCode}')";
                finalResultMessage    = "Failed: CRED found in approved QUAL but NOT matched in CREG.";
            }

            values["QUAL_CRITERIA_MESSAGE"] = qualCriteriaMessage;
            values["FINAL_RULE_TEXT"]        = controlLabel;
            values["Validation_Explanation"] = validationExplanation;
            values["CREG_LINK_MESSAGE"]      = registrationMessage;
            values["FINAL_RESULT_MESSAGE"]   = finalResultMessage;
            row.ValidationExplanation        = validationExplanation;
        }

        private static string FormatRule15ColumnValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "[blank]" : value.Trim();

        private static bool RequestsMatchForPendingSave(Rule15ValidationRequest current, Rule15ValidationRequest pending)
        {
            return current.ClientId == pending.ClientId &&
                   string.Equals(current.Server?.Trim(), pending.Server?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Database?.Trim(), pending.Database?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Driver?.Trim(), pending.Driver?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.StudTable?.Trim(), pending.StudTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.BridgeTable?.Trim(), pending.BridgeTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CrseTable?.Trim(), pending.CrseTable?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

        private static string ReadValue(IReadOnlyDictionary<string, string?> values, string key) =>
            values.TryGetValue(key, out var value) ? value ?? "" : "";
    }
}






