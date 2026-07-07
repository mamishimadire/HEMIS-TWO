using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule16Service : IRule16Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule16Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<Rule16TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule16TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = FindFirst(tables, ["dbo_STUD", "STUD"], ["stud"]),
                    AutoBridgeTable = FindFirst(tables, ["dbo_CREG", "CREG", "dbo_CRED", "CRED"], ["creg", "cred"]),
                    AutoCrseTable = FindFirst(tables, ["dbo_CRSE", "CRSE"], ["crse"])
                };
            }
            catch (Exception ex)
            {
                return new Rule16TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule16VerifyResult> VerifyTablesAsync(Rule16VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable);

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

                var result = new Rule16VerifyResult
                {
                    Success = true,
                    StudRecordCount = studCount,
                    BridgeRecordCount = bridgeCount,
                    CrseRecordCount = crseCount
                };

                if (await reader.ReadAsync())
                {
                    result.UnfulfilledPopulationCount = GetInt(reader, 0);
                    result.Control1PopulationCount = GetInt(reader, 1);
                    result.Control2PopulationCount = GetInt(reader, 2);
                    result.Control3PopulationCount = GetInt(reader, 3);
                }

                return result;
            }
            catch (Exception ex)
            {
                return new Rule16VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule16ValidationSummary> RunValidationAsync(Rule16ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable, request.UnfulfilledCol, request.DistanceCol, request.FoundationCol);

                var summary = await AnalyseAsync(request, includeAllReviewRows: false);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        var summaryToPersist = CloneSummary(summary);
                        if (summaryToPersist.IsPreviewOnly || summaryToPersist.ReviewRows.Count < summaryToPersist.TotalValidated)
                            summaryToPersist = await AnalyseAsync(request, includeAllReviewRows: true);

                        summaryToPersist.SavedRunId = null;
                        summary.SavedRunId = await SaveValidationRunAsync(request, summaryToPersist, userEmail, userName, markWorkspaceSaved: false);

                        if (!string.IsNullOrWhiteSpace(userEmail))
                            _pendingValidationCache.ClearPending(16, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        summary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!summary.SavedRunId.HasValue)
                {
                    if (summary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(16, request.ClientId, userEmail!, request, CloneSummary(summary), userName);

                    summary.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                        ? "Rule 16 validation completed. Click Save Workspace to write this validated result to the system database."
                        : summary.Warning;
                }
                else
                {
                    summary.Warning = "The current Rule 16 run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule16ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule16ValidationSummary> GetExportSummaryAsync(Rule16ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable, request.UnfulfilledCol, request.DistanceCol, request.FoundationCol);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public Task<Rule16ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail)
        {
            var pending = _pendingValidationCache.GetPending<Rule16ValidationRequest, Rule16ValidationSummary>(16, clientId, reviewerEmail);
            if (pending == null)
                return Task.FromResult<Rule16ValidationSummary?>(null);

            var preview = CloneSummary(pending.Summary);
            preview.SavedRunId = null;
            preview.Warning = "This Rule 16 validation is still pending. Click Save Workspace to write it to the system database.";
            ApplyBrowserPreview(preview);
            return Task.FromResult<Rule16ValidationSummary?>(preview);
        }

        public Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail)
            => Task.FromResult(_pendingValidationCache.HasPending(16, clientId, reviewerEmail));

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule16WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
  AND vr.RuleNumber = 16
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

            var workspace = new Rule16WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StudTable = reader.IsDBNull(4) ? "dbo_STUD" : reader.GetString(4),
                BridgeTable = reader.IsDBNull(5) ? "dbo_CREG" : reader.GetString(5),
                CrseTable = reader.IsDBNull(6) ? "dbo_CRSE" : reader.GetString(6),
                CurrentStatus = reader.IsDBNull(7) ? "" : reader.GetString(7),
                LastEditedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8),
                LastEditedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                Summary = summary
            };

            if (deserializedSummary != null)
            {
                workspace.CurrentStatus = deserializedSummary.Status;
                workspace.UnfulfilledCol = string.IsNullOrWhiteSpace(deserializedSummary.UnfulfilledCol) ? "_025" : deserializedSummary.UnfulfilledCol;
                workspace.UnfulfilledVal = string.IsNullOrWhiteSpace(deserializedSummary.UnfulfilledVal) ? "N" : deserializedSummary.UnfulfilledVal;
                workspace.FoundationCol = string.IsNullOrWhiteSpace(deserializedSummary.FoundationCol) ? "_091" : deserializedSummary.FoundationCol;
                workspace.FoundationVal = string.IsNullOrWhiteSpace(deserializedSummary.FoundationVal) ? "Y" : deserializedSummary.FoundationVal;
                workspace.DistanceCol = string.IsNullOrWhiteSpace(deserializedSummary.DistanceCol) ? "_024" : deserializedSummary.DistanceCol;
                workspace.DistanceVal = string.IsNullOrWhiteSpace(deserializedSummary.DistanceVal) ? "D" : deserializedSummary.DistanceVal;
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

        public async Task<Rule16RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 16;";
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

            var review = new Rule16RunReviewViewModel
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

        public async Task<Rule16WorkspaceSaveResult> SaveWorkspaceAsync(Rule16ValidationRequest request, string reviewerEmail, string? reviewerName = null)
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
                        return new Rule16WorkspaceSaveResult
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
                    command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule16|{request.RunId.Value}|{request.ClientId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                    await command.ExecuteNonQueryAsync();

                    if (!string.IsNullOrWhiteSpace(reviewerEmail))
                        _pendingValidationCache.ClearPending(16, request.ClientId, reviewerEmail);

                    var currentWorkspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                    return new Rule16WorkspaceSaveResult
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

                var pending = _pendingValidationCache.GetPending<Rule16ValidationRequest, Rule16ValidationSummary>(16, request.ClientId, reviewerEmail);
                if (pending == null)
                {
                    return new Rule16WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run Rule 16 first so the current workspace is written to the system database."
                    };
                }

                if (!RequestsMatchForPendingSave(request, pending.Request))
                {
                    return new Rule16WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Workspace settings changed after validation. Run Rule 16 again before saving."
                    };
                }

                var summaryToSave = CloneSummary(pending.Summary);
                if (summaryToSave.IsPreviewOnly || summaryToSave.ReviewRows.Count < summaryToSave.TotalValidated)
                {
                    summaryToSave = await AnalyseAsync(pending.Request, includeAllReviewRows: true);
                }

                summaryToSave.SavedRunId = null;
                var savedRunId = await SaveValidationRunAsync(pending.Request, summaryToSave, reviewerEmail, reviewerName ?? pending.ReviewerName, markWorkspaceSaved: true);
                _pendingValidationCache.ClearPending(16, request.ClientId, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule16WorkspaceSaveResult
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
                return new Rule16WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule16WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule16WorkspaceSaveResult
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
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule16|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(16, clientId.Value, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule16WorkspaceSaveResult
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
                return new Rule16WorkspaceSaveResult
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
                throw new InvalidOperationException("The selected Rule 16 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off a Rule 16 run.");

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
                throw new InvalidOperationException("The selected Rule 16 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule16ValidationRequest request)
        {
            ValidateRequest(request);

            var studTable = Sanitise(request.StudTable);
            var bridgeTable = Sanitise(request.BridgeTable);
            var crseTable = Sanitise(request.CrseTable);
            var uc = Sanitise(request.UnfulfilledCol.Length > 0 ? request.UnfulfilledCol : "_025");
            var uv = request.UnfulfilledVal;
            var fc = Sanitise(request.FoundationCol.Length > 0 ? request.FoundationCol : "_091");
            var fv = request.FoundationVal;
            var dc = Sanitise(request.DistanceCol.Length > 0 ? request.DistanceCol : "_024");
            var dv = request.DistanceVal;

            var sql = $@"-- CONTROL 1: {uc}='{uv}' AND {fc}='{fv}'
SELECT
    'Control_1' AS Control_Type,
    S.[_007], S.[_001], S.[{uc}], S.[{dc}],
    BRIDGE.[_001], BRIDGE.[_007], BRIDGE.[_030],
    CRSE.[_030], CRSE.[{fc}], CRSE.[_058],
    CASE
        WHEN ISNULL(S.[{uc}], '') = '{uv}' AND ISNULL(CRSE.[{fc}], '') = '{fv}'
        THEN 'PASS' ELSE 'FAIL'
    END AS Control_Check
FROM [{studTable}] S
INNER JOIN [{bridgeTable}] BRIDGE ON S.[_007] = BRIDGE.[_007]
INNER JOIN [{crseTable}] CRSE ON BRIDGE.[_030] = CRSE.[_030]
WHERE ISNULL(S.[{uc}], '') = '{uv}' AND ISNULL(CRSE.[{fc}], '') = '{fv}'
ORDER BY NEWID();

-- CONTROL 2: {uc}='{uv}' AND {fc}='{fv}' AND {dc}='{dv}'
SELECT
    'Control_2' AS Control_Type,
    S.[_007], S.[_001], S.[{uc}], S.[{dc}],
    BRIDGE.[_001], BRIDGE.[_007], BRIDGE.[_030],
    CRSE.[_030], CRSE.[{fc}], CRSE.[_058],
    CASE
        WHEN ISNULL(S.[{uc}], '') = '{uv}'
         AND ISNULL(CRSE.[{fc}], '') = '{fv}'
         AND ISNULL(S.[{dc}], '') = '{dv}'
        THEN 'PASS' ELSE 'FAIL'
    END AS Control_Check
FROM [{studTable}] S
INNER JOIN [{bridgeTable}] BRIDGE ON S.[_007] = BRIDGE.[_007]
INNER JOIN [{crseTable}] CRSE ON BRIDGE.[_030] = CRSE.[_030]
WHERE ISNULL(S.[{uc}], '') = '{uv}'
  AND ISNULL(CRSE.[{fc}], '') = '{fv}'
  AND ISNULL(S.[{dc}], '') = '{dv}'
ORDER BY NEWID();

-- CONTROL 3: {uc}='{uv}' AND {fc}<>'{fv}' AND {dc}<>'{dv}'
SELECT
    'Control_3' AS Control_Type,
    S.[_007], S.[_001], S.[{uc}], S.[{dc}],
    BRIDGE.[_001], BRIDGE.[_007], BRIDGE.[_030],
    CRSE.[_030], CRSE.[{fc}], CRSE.[_058],
    CASE
        WHEN ISNULL(S.[{uc}], '') = '{uv}'
         AND ISNULL(CRSE.[{fc}], '') <> '{fv}'
         AND ISNULL(S.[{dc}], '') <> '{dv}'
        THEN 'PASS' ELSE 'FAIL'
    END AS Control_Check
FROM [{studTable}] S
INNER JOIN [{bridgeTable}] BRIDGE ON S.[_007] = BRIDGE.[_007]
INNER JOIN [{crseTable}] CRSE ON BRIDGE.[_030] = CRSE.[_030]
WHERE ISNULL(S.[{uc}], '') = '{uv}'
  AND ISNULL(CRSE.[{fc}], '') <> '{fv}'
  AND ISNULL(S.[{dc}], '') <> '{dv}'
ORDER BY NEWID();";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule16ValidationSummary> AnalyseAsync(Rule16ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var studTable = Sanitise(request.StudTable);
            var bridgeTable = Sanitise(request.BridgeTable);
            var crseTable = Sanitise(request.CrseTable);
            var unfulfilledCol = Sanitise(request.UnfulfilledCol.Length > 0 ? request.UnfulfilledCol : "_025");
            var unfulfilledVal = request.UnfulfilledVal;
            var foundationCol = Sanitise(request.FoundationCol.Length > 0 ? request.FoundationCol : "_091");
            var foundationVal = request.FoundationVal;
            var distanceCol = Sanitise(request.DistanceCol.Length > 0 ? request.DistanceCol : "_024");
            var distanceVal = request.DistanceVal;

            var studCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{studTable}];");
            var bridgeCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{bridgeTable}];");
            var crseCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{crseTable}];");

            // Build temp tables on this connection (temp tables are session-scoped)
            await using var prepCommand = conn.CreateConfiguredCommand();
            prepCommand.CommandText = BuildRule16PrepSql(studTable, bridgeTable, crseTable, unfulfilledCol, unfulfilledVal, foundationCol, foundationVal, distanceCol, distanceVal);
            await prepCommand.ExecuteNonQueryAsync();

            await using var countCommand = conn.CreateConfiguredCommand();
            countCommand.CommandText = BuildRule16CountAfterPrepSql(distanceVal);
            await using var countReader = await countCommand.ExecuteReaderAsync();

            var unfulfilledPopulationCount = 0;
            var control1PassPopulation = 0;
            var control2PassPopulation = 0;
            var control3PassPopulation = 0;
            if (await countReader.ReadAsync())
            {
                unfulfilledPopulationCount = GetInt(countReader, 0);
                control1PassPopulation    = GetInt(countReader, 0);
                control2PassPopulation    = GetInt(countReader, 1);
                control3PassPopulation    = GetInt(countReader, 2);
            }
            await countReader.CloseAsync();

            var reviewRows = await LoadControlRowsFromTempAsync(conn, includeAllReviewRows ? (int?)null : BrowserPreviewRowLimit, unfulfilledCol, unfulfilledVal, foundationCol, foundationVal, distanceCol, distanceVal);
            reviewRows = NormalizeReviewRows(reviewRows);

            var controlSummaries = BuildControlSummaries(control1PassPopulation, control2PassPopulation, control3PassPopulation, unfulfilledCol, unfulfilledVal, foundationCol, foundationVal, distanceCol, distanceVal);
            var totalValidated = control1PassPopulation;
            var passCount = control1PassPopulation;
            var failCount = 0;
            var isPreviewOnly = !includeAllReviewRows && totalValidated > reviewRows.Count;

            return new Rule16ValidationSummary
            {
                Success = true,
                StudRecordCount = studCount,
                BridgeRecordCount = bridgeCount,
                CrseRecordCount = crseCount,
                UnfulfilledPopulationCount = unfulfilledPopulationCount,
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
                UnfulfilledCol = request.UnfulfilledCol,
                UnfulfilledVal = request.UnfulfilledVal,
                FoundationCol = request.FoundationCol,
                FoundationVal = request.FoundationVal,
                DistanceCol = request.DistanceCol,
                DistanceVal = request.DistanceVal,
                TableLinkageText = $"{request.StudTable} -> {request.BridgeTable} -> {request.CrseTable}",
                RuleModeText = "100% population testing of matching control rows",
                ProcedureSteps = BuildProcedureSteps(request.StudTable, request.BridgeTable, request.CrseTable),
                ClientId = request.ClientId,
                ControlSummaries = controlSummaries,
                ReviewRows = reviewRows,
                Warning = includeAllReviewRows
                    ? "Rule 16 completed with the full matching control result set."
                    : "Counts reflect the full matching control result set. Browser review rows are limited for performance."
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule16ValidationRequest request, Rule16ValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 16);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 16);
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
    @ClientID, @UserID, 16, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @BridgeTable, @CrseTable, NULL,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, @WorkspaceSavedAt, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Student Population Validation");
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
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule16|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private static string BuildRule16PrepSql(
            string studTable, string bridgeTable, string crseTable,
            string unfulfilledCol = "_025", string unfulfilledVal = "N",
            string foundationCol = "_091", string foundationVal = "Y",
            string distanceCol = "_024", string distanceVal = "D") => $@"
SET NOCOUNT ON;
DROP TABLE IF EXISTS #Rule16_Base;
DROP TABLE IF EXISTS #Rule16_Results;

SELECT
    CAST(S.[_007]               AS nvarchar(255)) AS Student_Number,
    CAST(S.[_001]               AS nvarchar(255)) AS Student_Qualification_Code,
    CAST(S.[{unfulfilledCol}]   AS nvarchar(255)) AS Qualification_Fulfilled_Indicator,
    CAST(S.[{distanceCol}]      AS nvarchar(255)) AS Attendance_Mode,
    CAST(CREG.[_001]            AS nvarchar(255)) AS CREG_Qualification_Code,
    CAST(CREG.[_007]            AS nvarchar(255)) AS CREG_Student_Number,
    CAST(CREG.[_030]            AS nvarchar(255)) AS CREG_Course_Code,
    CAST(CRSE.[_030]            AS nvarchar(255)) AS CRSE_Course_Code,
    CAST(CRSE.[{foundationCol}] AS nvarchar(255)) AS Foundation_Course_Indicator,
    CAST(CRSE.[_058]            AS nvarchar(255)) AS CRSE_058
INTO #Rule16_Base
FROM [{studTable}] S
INNER JOIN [{bridgeTable}] CREG ON S.[_007] = CREG.[_007]
INNER JOIN [{crseTable}]   CRSE ON CREG.[_030] = CRSE.[_030]
WHERE ISNULL(CAST(S.[{unfulfilledCol}]   AS nvarchar(255)), '') = '{unfulfilledVal}'
  AND ISNULL(CAST(CRSE.[{foundationCol}] AS nvarchar(255)), '') = '{foundationVal}';

CREATE INDEX IX_Rule16_Base ON #Rule16_Base (Student_Number, Student_Qualification_Code, CREG_Course_Code);

SELECT
    ROW_NUMBER() OVER (ORDER BY Student_Number, Student_Qualification_Code, CREG_Course_Code) AS Extract_Number,
    Student_Number,
    Student_Qualification_Code,
    Qualification_Fulfilled_Indicator,
    Attendance_Mode,
    CREG_Qualification_Code,
    CREG_Student_Number,
    CREG_Course_Code,
    CRSE_Course_Code,
    Foundation_Course_Indicator,
    CRSE_058,
    'PASS' AS Control_Check
INTO #Rule16_Results
FROM #Rule16_Base;";

        private static string BuildRule16CountAfterPrepSql(string distanceVal = "D") => $@"
SELECT
    COUNT(*)                                                                                       AS TotalPopulation,
    SUM(CASE WHEN ISNULL(Attendance_Mode,'') = '{distanceVal}'  THEN 1 ELSE 0 END)               AS Control2Pass,
    SUM(CASE WHEN ISNULL(Attendance_Mode,'') <> '{distanceVal}' THEN 1 ELSE 0 END)               AS Control3Pass
FROM #Rule16_Results;";

        private static string BuildPopulationCountSql(
            string studTable, string bridgeTable, string crseTable,
            string unfulfilledCol = "_025", string unfulfilledVal = "N",
            string foundationCol = "_091", string foundationVal = "Y",
            string distanceCol = "_024", string distanceVal = "D") => $@"
SET NOCOUNT ON;
DROP TABLE IF EXISTS #Rule16_VerifyBase;
SELECT
    CAST(S.[_007] AS nvarchar(10)) AS s7,
    CASE WHEN ISNULL(CAST(S.[{unfulfilledCol}]   AS nvarchar(10)),'') = '{unfulfilledVal}'
          AND ISNULL(CAST(CRSE.[{foundationCol}] AS nvarchar(10)),'') = '{foundationVal}' THEN 1 ELSE 0 END AS IsC1,
    CASE WHEN ISNULL(CAST(S.[{unfulfilledCol}]   AS nvarchar(10)),'') = '{unfulfilledVal}'
          AND ISNULL(CAST(CRSE.[{foundationCol}] AS nvarchar(10)),'') = '{foundationVal}'
          AND ISNULL(CAST(S.[{distanceCol}]       AS nvarchar(10)),'') = '{distanceVal}' THEN 1 ELSE 0 END AS IsC2,
    CASE WHEN ISNULL(CAST(S.[{unfulfilledCol}]   AS nvarchar(10)),'') = '{unfulfilledVal}'
          AND ISNULL(CAST(CRSE.[{foundationCol}] AS nvarchar(10)),'') = '{foundationVal}'
          AND ISNULL(CAST(S.[{distanceCol}]       AS nvarchar(10)),'') <> '{distanceVal}' THEN 1 ELSE 0 END AS IsC3
INTO #Rule16_VerifyBase
FROM [{studTable}] S
INNER JOIN [{bridgeTable}] CREG ON S.[_007] = CREG.[_007]
INNER JOIN [{crseTable}]   CRSE ON CREG.[_030] = CRSE.[_030];
SELECT
    SUM(IsC1) AS UnfulfilledPopulationCount,
    SUM(IsC1) AS Control1PassPopulation,
    SUM(IsC2) AS Control2PassPopulation,
    SUM(IsC3) AS Control3PassPopulation
FROM #Rule16_VerifyBase;
DROP TABLE IF EXISTS #Rule16_VerifyBase;";

        private async Task<List<Rule16ValidationRowRecord>> LoadControlRowsFromTempAsync(
            SqlConnection connection, int? maxRows,
            string unfulfilledCol = "_025", string unfulfilledVal = "N",
            string foundationCol = "_091", string foundationVal = "Y",
            string distanceCol = "_024", string distanceVal = "D")
        {
            var topClause = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value}" : "";
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = $@"
SELECT {topClause}
    Extract_Number,
    'Control_1'              AS Control_Type,
    'PASS'                   AS Validation_Result,
    Student_Number,
    Student_Qualification_Code,
    Qualification_Fulfilled_Indicator,
    Attendance_Mode,
    CREG_Qualification_Code,
    CREG_Student_Number,
    CREG_Course_Code,
    CRSE_Course_Code,
    Foundation_Course_Indicator,
    CRSE_058,
    Control_Check
FROM #Rule16_Results
ORDER BY Extract_Number;";

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule16ValidationRowRecord>();
            while (await reader.ReadAsync())
            {
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                rows.Add(new Rule16ValidationRowRecord
                {
                    ValidationNumber = rows.Count + 1,
                    ControlType      = ReadValue(displayValues, "Control_Type"),
                    ControlLabel     = ReadValue(displayValues, "Control_Label"),
                    ValidationResult = ReadValue(displayValues, "Validation_Result"),
                    ValidationExplanation = "",
                    DisplayValues    = displayValues
                });

                EnrichRule16DisplayValues(rows[^1], unfulfilledCol, unfulfilledVal, foundationCol, foundationVal, distanceCol, distanceVal);
            }

            return rows;
        }


        private static List<Rule16ControlSummaryItemViewModel> BuildControlSummaries(
            int control1PassPopulation,
            int control2PassPopulation,
            int control3PassPopulation,
            string unfulfilledCol = "_025", string unfulfilledVal = "N",
            string foundationCol = "_091", string foundationVal = "Y",
            string distanceCol = "_024", string distanceVal = "D")
        {
            return new List<Rule16ControlSummaryItemViewModel>
            {
                BuildControlSummary("Control_1", "Control 1", $"{unfulfilledCol}='{unfulfilledVal}' AND {foundationCol}='{foundationVal}'", control1PassPopulation),
                BuildControlSummary("Control_2", "Control 2", $"{unfulfilledCol}='{unfulfilledVal}' AND {foundationCol}='{foundationVal}' AND {distanceCol}='{distanceVal}'", control2PassPopulation),
                BuildControlSummary("Control_3", "Control 3", $"{unfulfilledCol}='{unfulfilledVal}' AND {foundationCol}='{foundationVal}' AND {distanceCol}<>'{distanceVal}'", control3PassPopulation)
            };
        }

        private static Rule16ControlSummaryItemViewModel BuildControlSummary(
            string controlType,
            string controlLabel,
            string criteriaText,
            int passCount)
        {
            return new Rule16ControlSummaryItemViewModel
            {
                ControlType = controlType,
                ControlLabel = controlLabel,
                CriteriaText = criteriaText,
                RequestedCount = passCount,
                AvailableCount = passCount,
                AchievedCount = passCount,
                TotalCount = passCount,
                PassCount = passCount,
                FailCount = 0,
                Status = passCount > 0 ? "PASS" : "NO DATA"
            };
        }

        private static List<Rule16ValidationRowRecord> NormalizeReviewRows(IEnumerable<Rule16ValidationRowRecord>? rows)
        {
            var normalized = (rows ?? Enumerable.Empty<Rule16ValidationRowRecord>())
                .Select((row, index) =>
                {
                    row.ValidationNumber = index + 1;
                    return row;
                })
                .ToList();

            return normalized;
        }

        private async Task<Rule16ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule16ValidationSummary summary, string? server)
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
                    new Rule16ValidationRequest
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
                    ? "Saved Rule 16 results were expanded from the stored browser preview to the full result set."
                    : $"{summary.Warning} Full saved results were reloaded from the saved Rule 16 configuration.";

                return expanded;
            }
            catch
            {
                return summary;
            }
        }

        private static Rule16ValidationSummary CloneSummary(Rule16ValidationSummary summary)
        {
            return new Rule16ValidationSummary
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
                    .Select(item => new Rule16ControlSummaryItemViewModel
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

        private static Rule16ValidationRowRecord CloneReviewRow(Rule16ValidationRowRecord row)
        {
            return new Rule16ValidationRowRecord
            {
                ValidationNumber = row.ValidationNumber,
                ControlType = row.ControlType,
                ControlLabel = row.ControlLabel,
                ValidationResult = row.ValidationResult,
                ValidationExplanation = row.ValidationExplanation,
                DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static Rule16ValidationSummary CreateBrowserPreview(Rule16ValidationSummary summary)
        {
            var previewRows = summary.ReviewRows
                .OrderBy(row => row.ValidationNumber)
                .Take(BrowserPreviewRowLimit)
                .ToList();

            return new Rule16ValidationSummary
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
                    .Select(item => new Rule16ControlSummaryItemViewModel
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

        private static void ApplyBrowserPreview(Rule16ValidationSummary summary)
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
            "Control_2" => 2,
            "Control_3" => 3,
            _ => 99
        };

        private static List<string> BuildProcedureSteps(string studTable, string bridgeTable, string crseTable) =>
            new()
            {
                $"Link {studTable}._007 to {bridgeTable}._007.",
                $"Link {bridgeTable}._030 to {crseTable}._030.",
                "Filter the joined population to students where dbo_STUD._025 = 'N'.",
                "Count full matching joined rows per control using the exact control-specific distance and foundation conditions.",
                "Return the full matching control result set for Control 1, Control 2, and Control 3."
            };

        private async Task EnsureColumnsExistAsync(
            string server, string database, string driver,
            string studTable, string bridgeTable, string crseTable,
            string unfulfilledCol = "_025", string distanceCol = "_024",
            string foundationCol = "_091")
        {
            var studColumns = await GetTableColumnsAsync(server, database, driver, studTable);
            var bridgeColumns = await GetTableColumnsAsync(server, database, driver, bridgeTable);
            var crseColumns = await GetTableColumnsAsync(server, database, driver, crseTable);

            EnsureHasColumns(studTable, studColumns, "_001", "_007", unfulfilledCol, distanceCol);
            EnsureHasColumns(bridgeTable, bridgeColumns, "_001", "_007", "_030");
            EnsureHasColumns(crseTable, crseColumns, "_030", foundationCol);
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

        private static void ValidateRequest(Rule16ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable))
                throw new InvalidOperationException("STUD table is required.");
            if (string.IsNullOrWhiteSpace(request.BridgeTable))
                throw new InvalidOperationException("Bridge table is required.");
            if (string.IsNullOrWhiteSpace(request.CrseTable))
                throw new InvalidOperationException("CRSE table is required.");

            ValidateObjectName(request.StudTable);
            ValidateObjectName(request.BridgeTable);
            ValidateObjectName(request.CrseTable);
        }

        private static void ValidateRequest(Rule16VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable))
                throw new InvalidOperationException("STUD table is required.");
            if (string.IsNullOrWhiteSpace(request.BridgeTable))
                throw new InvalidOperationException("Bridge table is required.");
            if (string.IsNullOrWhiteSpace(request.CrseTable))
                throw new InvalidOperationException("CRSE table is required.");

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

        private static Rule16ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule16ValidationSummary>(decoded);
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

        private static void EnrichRule16DisplayValues(
            Rule16ValidationRowRecord row,
            string unfulfilledCol = "_025", string unfulfilledVal = "N",
            string foundationCol = "_091", string foundationVal = "Y",
            string distanceCol = "_024", string distanceVal = "D")
        {
            var values = row.DisplayValues;
            var attendanceMode    = FormatRule16ColumnValue(ReadValue(values, "Attendance_Mode"));
            var foundationDisplay = FormatRule16ColumnValue(ReadValue(values, "Foundation_Course_Indicator"));
            var studentNum        = FormatRule16ColumnValue(ReadValue(values, "Student_Number"));
            var qualCode          = FormatRule16ColumnValue(ReadValue(values, "Student_Qualification_Code"));
            var courseCode        = FormatRule16ColumnValue(ReadValue(values, "CREG_Course_Code"));
            var isDistance        = string.Equals(attendanceMode.Trim(), distanceVal, StringComparison.OrdinalIgnoreCase);
            var category          = isDistance ? "Distance Learning" : "Normal Student";

            var criteriaText        = $"{unfulfilledCol}='{unfulfilledVal}' AND {foundationCol}='{foundationVal}'";
            var studCriteriaMessage = $"Passed: {unfulfilledCol}='{unfulfilledVal}' FOUND. Foundation: {foundationCol}='{foundationDisplay}'.";
            var bridgeLinkMessage   = $"Linked via CREG: Student={studentNum}, QualCode={qualCode}, CourseCode={courseCode}.";
            var validationExplanation = $"{unfulfilledCol}='{unfulfilledVal}': FOUND | {foundationCol}='{foundationDisplay}': FOUND | {distanceCol}='{attendanceMode}' | Category: {category}";

            values["FINAL_RULE_TEXT"]        = criteriaText;
            values["Validation_Explanation"] = validationExplanation;
            values["STUD_CRITERIA_MESSAGE"]  = studCriteriaMessage;
            values["BRIDGE_LINK_MESSAGE"]    = bridgeLinkMessage;
            values["Student_Category"]       = category;
            row.ValidationExplanation        = validationExplanation;
        }

        private static string FormatRule16ColumnValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "[blank]" : value.Trim();

        private static bool RequestsMatchForPendingSave(Rule16ValidationRequest current, Rule16ValidationRequest pending)
        {
            return current.ClientId == pending.ClientId &&
                   string.Equals(current.Server?.Trim(), pending.Server?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Database?.Trim(), pending.Database?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Driver?.Trim(), pending.Driver?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.StudTable?.Trim(), pending.StudTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.BridgeTable?.Trim(), pending.BridgeTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CrseTable?.Trim(), pending.CrseTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   current.SampleControl1Size == pending.SampleControl1Size &&
                   current.SampleControl2Size == pending.SampleControl2Size &&
                   current.SampleControl3Size == pending.SampleControl3Size;
        }

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

        private static string ReadValue(IReadOnlyDictionary<string, string?> values, string key) =>
            values.TryGetValue(key, out var value) ? value ?? "" : "";
    }
}





