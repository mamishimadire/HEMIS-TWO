using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using MiniExcelLibs;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace HemisAudit.Services
{
    public class Rule18Service : IRule18Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule18Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<ColumnValuesResult> GetColumnValuesAsync(string server, string database, string driver, string tableName, string columnName)
        {
            try
            {
                ValidateObjectName(tableName);
                ValidateObjectName(columnName);
                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = $@"
SELECT DISTINCT CAST([{Sanitise(columnName)}] AS nvarchar(255))
FROM [{Sanitise(tableName)}]
WHERE [{Sanitise(columnName)}] IS NOT NULL
ORDER BY CAST([{Sanitise(columnName)}] AS nvarchar(255));";

                await using var reader = await cmd.ExecuteReaderAsync();
                var values = new List<string>();
                while (await reader.ReadAsync() && values.Count < 200)
                {
                    if (!reader.IsDBNull(0))
                        values.Add(reader.GetString(0));
                }

                return new ColumnValuesResult { Success = true, Values = values };
            }
            catch (Exception ex)
            {
                return new ColumnValuesResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<ColumnValuesResult> GetTableColumnsListAsync(string server, string database, string driver, string tableName)
        {
            try
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
                while (await reader.ReadAsync() && columns.Count < 500)
                {
                    if (!reader.IsDBNull(0))
                        columns.Add(reader.GetString(0));
                }

                return new ColumnValuesResult { Success = true, Values = columns };
            }
            catch (Exception ex)
            {
                return new ColumnValuesResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule18TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule18TableDiscoveryResult
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
                return new Rule18TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule18VerifyResult> VerifyTablesAsync(Rule18VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                var nsfasCol = Sanitise(string.IsNullOrWhiteSpace(request.NsfasFilterCol) ? "_019" : request.NsfasFilterCol);
                var nsfasVal = string.IsNullOrWhiteSpace(request.NsfasFilterValue) ? "NS" : request.NsfasFilterValue;
                var distCol = Sanitise(string.IsNullOrWhiteSpace(request.DistanceFilterCol) ? "_024" : request.DistanceFilterCol);
                var distVal = string.IsNullOrWhiteSpace(request.DistanceFilterValue) ? "D" : request.DistanceFilterValue;
                var foundCol = Sanitise(string.IsNullOrWhiteSpace(request.FoundationFilterCol) ? "_091" : request.FoundationFilterCol);
                var foundVal = string.IsNullOrWhiteSpace(request.FoundationFilterValue) ? "Y" : request.FoundationFilterValue;
                var credJoinCol = Sanitise(string.IsNullOrWhiteSpace(request.CredJoinCol) ? "_001" : request.CredJoinCol);
                var credCourseCol = Sanitise(string.IsNullOrWhiteSpace(request.CredCourseCol) ? "_030" : request.CredCourseCol);
                var crseCourseCol = Sanitise(string.IsNullOrWhiteSpace(request.CrseCourseCol) ? "_030" : request.CrseCourseCol);
                var crseNameCol = Sanitise(string.IsNullOrWhiteSpace(request.CrseNameCol) ? "_058" : request.CrseNameCol);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable,
                    nsfasCol, distCol, foundCol, credJoinCol, credCourseCol, crseCourseCol, crseNameCol);

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var studTable = Sanitise(request.StudTable);
                var bridgeTable = Sanitise(request.BridgeTable);
                var crseTable = Sanitise(request.CrseTable);

                var studCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{studTable}];");
                var bridgeCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{bridgeTable}];");
                var crseCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{crseTable}];");

                await using (var prepCmd = conn.CreateConfiguredCommand())
                {
                    prepCmd.CommandText = BuildRule18PrepSql(studTable, bridgeTable, crseTable,
                        nsfasCol, nsfasVal.Replace("'", "''"),
                        foundCol, foundVal.Replace("'", "''"),
                        distCol, distVal.Replace("'", "''"),
                        credJoinCol, credCourseCol, crseCourseCol, crseNameCol);
                    prepCmd.CommandTimeout = 120;
                    await prepCmd.ExecuteNonQueryAsync();
                }

                await using var command = conn.CreateConfiguredCommand();
                command.CommandText = BuildRule18CountSql(nsfasVal.Replace("'", "''"));
                await using var reader = await command.ExecuteReaderAsync();

                var result = new Rule18VerifyResult
                {
                    Success = true,
                    StudRecordCount = studCount,
                    BridgeRecordCount = bridgeCount,
                    CrseRecordCount = crseCount
                };

                if (await reader.ReadAsync())
                {
                    result.NsfasPopulationCount = GetInt(reader, 0);
                    result.Control1PopulationCount = GetInt(reader, 1);
                    result.Control2PopulationCount = GetInt(reader, 2);
                    result.Control3PopulationCount = GetInt(reader, 3);
                    result.Control4PopulationCount = 0;
                }

                return result;
            }
            catch (Exception ex)
            {
                return new Rule18VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule18ValidationSummary> RunValidationAsync(Rule18ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable,
                    Sanitise(string.IsNullOrWhiteSpace(request.NsfasFilterCol) ? "_019" : request.NsfasFilterCol),
                    Sanitise(string.IsNullOrWhiteSpace(request.DistanceFilterCol) ? "_024" : request.DistanceFilterCol),
                    Sanitise(string.IsNullOrWhiteSpace(request.FoundationFilterCol) ? "_091" : request.FoundationFilterCol),
                    Sanitise(string.IsNullOrWhiteSpace(request.CredJoinCol) ? "_001" : request.CredJoinCol),
                    Sanitise(string.IsNullOrWhiteSpace(request.CredCourseCol) ? "_030" : request.CredCourseCol),
                    Sanitise(string.IsNullOrWhiteSpace(request.CrseCourseCol) ? "_030" : request.CrseCourseCol),
                    Sanitise(string.IsNullOrWhiteSpace(request.CrseNameCol) ? "_058" : request.CrseNameCol));

                var summary = await AnalyseAsync(request, includeAllReviewRows: false);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        var summaryToPersist = CloneSummary(summary);
                        summaryToPersist.SavedRunId = null;
                        summary.SavedRunId = await SaveValidationRunAsync(request, summaryToPersist, userEmail, userName, markWorkspaceSaved: false);
                        if (summary.SavedRunId.HasValue)
                        {
                            var savedId = summary.SavedRunId.Value;
                            var capturedRequest = request;
                            _ = Task.Run(async () =>
                            {
                                try { await BulkCopyToRule18ResultsAsync(capturedRequest, savedId); }
                                catch { /* background copy failed; download will fall back to HEMIS re-query */ }
                            });
                        }

                        if (!string.IsNullOrWhiteSpace(userEmail))
                            _pendingValidationCache.ClearPending(18, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        summary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!summary.SavedRunId.HasValue)
                {
                    if (summary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(18, request.ClientId, userEmail!, request, CloneSummary(summary), userName);

                    summary.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                        ? "Rule 18 validation completed. Click Save Workspace to write this validated result to the system database."
                        : summary.Warning;
                }
                else
                {
                    summary.Warning = "The current Rule 18 run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule18ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule18ValidationSummary> GetExportSummaryAsync(Rule18ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable,
                Sanitise(string.IsNullOrWhiteSpace(request.NsfasFilterCol) ? "_019" : request.NsfasFilterCol),
                Sanitise(string.IsNullOrWhiteSpace(request.DistanceFilterCol) ? "_024" : request.DistanceFilterCol),
                Sanitise(string.IsNullOrWhiteSpace(request.FoundationFilterCol) ? "_091" : request.FoundationFilterCol));
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public Task<Rule18ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail)
        {
            var pending = _pendingValidationCache.GetPending<Rule18ValidationRequest, Rule18ValidationSummary>(18, clientId, reviewerEmail);
            if (pending == null)
                return Task.FromResult<Rule18ValidationSummary?>(null);

            var preview = CloneSummary(pending.Summary);
            preview.SavedRunId = null;
            preview.Warning = "This Rule 18 validation is still pending. Click Save Workspace to write it to the system database.";
            ApplyBrowserPreview(preview);
            return Task.FromResult<Rule18ValidationSummary?>(preview);
        }

        public Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail)
            => Task.FromResult(_pendingValidationCache.HasPending(18, clientId, reviewerEmail));

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule18WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
  AND vr.RuleNumber = 18
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

            var workspace = new Rule18WorkspaceStateViewModel
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
                Summary = summary,
                Control1FilterCol = deserializedSummary?.Control1FilterCol ?? "_019",
                Control1FilterValue = deserializedSummary?.Control1FilterValue ?? "NS",
                NsfasFilterCol = deserializedSummary?.NsfasFilterCol ?? "_019",
                NsfasFilterValue = deserializedSummary?.NsfasFilterValue ?? "NS",
                FoundationFilterCol = deserializedSummary?.FoundationFilterCol ?? "_091",
                FoundationFilterValue = deserializedSummary?.FoundationFilterValue ?? "Y",
                DistanceFilterCol = deserializedSummary?.DistanceFilterCol ?? "_024",
                DistanceFilterValue = deserializedSummary?.DistanceFilterValue ?? "D",
                CredJoinCol = string.IsNullOrWhiteSpace(deserializedSummary?.CredJoinCol) ? "_001" : deserializedSummary!.CredJoinCol,
                CredCourseCol = string.IsNullOrWhiteSpace(deserializedSummary?.CredCourseCol) ? "_030" : deserializedSummary!.CredCourseCol,
                CrseCourseCol = string.IsNullOrWhiteSpace(deserializedSummary?.CrseCourseCol) ? "_030" : deserializedSummary!.CrseCourseCol,
                CrseNameCol = string.IsNullOrWhiteSpace(deserializedSummary?.CrseNameCol) ? "_058" : deserializedSummary!.CrseNameCol,
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

        public async Task<Rule18RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 18;";
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

            var review = new Rule18RunReviewViewModel
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

        public async Task<Rule18WorkspaceSaveResult> SaveWorkspaceAsync(Rule18ValidationRequest request, string reviewerEmail, string? reviewerName = null)
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
                        return new Rule18WorkspaceSaveResult
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
                    command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule18|{request.RunId.Value}|{request.ClientId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                    await command.ExecuteNonQueryAsync();

                    if (!string.IsNullOrWhiteSpace(reviewerEmail))
                        _pendingValidationCache.ClearPending(18, request.ClientId, reviewerEmail);

                    var currentWorkspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                    return new Rule18WorkspaceSaveResult
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

                var pending = _pendingValidationCache.GetPending<Rule18ValidationRequest, Rule18ValidationSummary>(18, request.ClientId, reviewerEmail);
                if (pending == null)
                {
                    return new Rule18WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run Rule 18 first so the current workspace is written to the system database."
                    };
                }

                if (!RequestsMatchForPendingSave(request, pending.Request))
                {
                    return new Rule18WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Workspace settings changed after validation. Run Rule 18 again before saving."
                    };
                }

                var summaryToSave = CloneSummary(pending.Summary);
                summaryToSave.SavedRunId = null;
                var savedRunId = await SaveValidationRunAsync(pending.Request, summaryToSave, reviewerEmail, reviewerName ?? pending.ReviewerName, markWorkspaceSaved: true);
                _pendingValidationCache.ClearPending(18, request.ClientId, reviewerEmail);
                try { await BulkCopyToRule18ResultsAsync(pending.Request, savedRunId); }
                catch { /* non-fatal; download falls back to HEMIS re-query */ }

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule18WorkspaceSaveResult
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
                return new Rule18WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule18WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule18WorkspaceSaveResult
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
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule18|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(18, clientId.Value, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule18WorkspaceSaveResult
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
                return new Rule18WorkspaceSaveResult
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
                throw new InvalidOperationException("The selected Rule 18 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off a Rule 18 run.");

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
                throw new InvalidOperationException("The selected Rule 18 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule18ValidationRequest request)
        {
            ValidateRequest(request);

            var studTable = Sanitise(request.StudTable);
            var bridgeTable = Sanitise(request.BridgeTable);
            var crseTable = Sanitise(request.CrseTable);
            var nsfasCol = Sanitise(string.IsNullOrWhiteSpace(request.NsfasFilterCol) ? "_019" : request.NsfasFilterCol);
            var nsfasVal = (string.IsNullOrWhiteSpace(request.NsfasFilterValue) ? "NS" : request.NsfasFilterValue).Replace("'", "''");
            var foundCol = Sanitise(string.IsNullOrWhiteSpace(request.FoundationFilterCol) ? "_091" : request.FoundationFilterCol);
            var foundVal = (string.IsNullOrWhiteSpace(request.FoundationFilterValue) ? "Y" : request.FoundationFilterValue).Replace("'", "''");
            var distCol = Sanitise(string.IsNullOrWhiteSpace(request.DistanceFilterCol) ? "_024" : request.DistanceFilterCol);
            var distVal = (string.IsNullOrWhiteSpace(request.DistanceFilterValue) ? "D" : request.DistanceFilterValue).Replace("'", "''");
            var credJoinCol = Sanitise(string.IsNullOrWhiteSpace(request.CredJoinCol) ? "_001" : request.CredJoinCol);
            var credCourseCol = Sanitise(string.IsNullOrWhiteSpace(request.CredCourseCol) ? "_030" : request.CredCourseCol);
            var crseCourseCol = Sanitise(string.IsNullOrWhiteSpace(request.CrseCourseCol) ? "_030" : request.CrseCourseCol);
            var crseNameCol = Sanitise(string.IsNullOrWhiteSpace(request.CrseNameCol) ? "_058" : request.CrseNameCol);

            var sql = $@"-- HEMIS RULE 18: NSFAS STUDENTS VALIDATION
SET NOCOUNT ON;
DROP TABLE IF EXISTS #Rule18_Base;
DROP TABLE IF EXISTS #Rule18_Validation;

-- STEP 1: Build base population (all STUD + CREG + CRSE rows)
SELECT
    CAST(S.[_007]                AS nvarchar(255)) AS Student_Number,
    CAST(S.[_001]                AS nvarchar(255)) AS Student_Qualification_Code,
    CAST(S.[{nsfasCol}]          AS nvarchar(255)) AS NSFAS_Status,
    CAST(S.[{distCol}]           AS nvarchar(255)) AS Attendance_Mode,
    CAST(S.[_025]                AS nvarchar(255)) AS Qualification_Fulfilled_Indicator,
    CAST(BRIDGE.[{credJoinCol}]  AS nvarchar(255)) AS CREG_Qualification_Code,
    CAST(BRIDGE.[{credCourseCol}] AS nvarchar(255)) AS CREG_Course_Code,
    CAST(CRSE.[{crseCourseCol}]  AS nvarchar(255)) AS CRSE_Course_Code,
    CAST(CRSE.[{foundCol}]       AS nvarchar(255)) AS Foundation_Course_Indicator,
    CAST(CRSE.[{crseNameCol}]    AS nvarchar(255)) AS CRSE_058
INTO #Rule18_Base
FROM [{studTable}] S
INNER JOIN [{bridgeTable}] BRIDGE ON S.[{credJoinCol}] = BRIDGE.[{credJoinCol}]
INNER JOIN [{crseTable}] CRSE ON BRIDGE.[{credCourseCol}] = CRSE.[{crseCourseCol}];

-- STEP 2: Extract control populations
SELECT *
INTO #Rule18_Validation
FROM (
    -- Control 1: NSFAS students enrolled in Foundation courses
    SELECT 'Control_1' AS Control_Type,
           Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
           Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
           CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058,
           'PASS' AS Validation_Result
    FROM #Rule18_Base
    WHERE ISNULL(NSFAS_Status, '') = '{nsfasVal}'
      AND ISNULL(Foundation_Course_Indicator, '') = '{foundVal}'

    UNION ALL

    -- Control 2: NSFAS students in Foundation courses studying via Distance
    SELECT 'Control_2' AS Control_Type,
           Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
           Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
           CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058,
           'PASS' AS Validation_Result
    FROM #Rule18_Base
    WHERE ISNULL(NSFAS_Status, '') = '{nsfasVal}'
      AND ISNULL(Foundation_Course_Indicator, '') = '{foundVal}'
      AND ISNULL(Attendance_Mode, '') = '{distVal}'

    UNION ALL

    -- Control 3: NSFAS students NOT in Foundation courses and NOT studying via Distance
    SELECT 'Control_3' AS Control_Type,
           Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
           Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
           CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058,
           'PASS' AS Validation_Result
    FROM #Rule18_Base
    WHERE ISNULL(NSFAS_Status, '') = '{nsfasVal}'
      AND ISNULL(Foundation_Course_Indicator, '') <> '{foundVal}'
      AND ISNULL(Attendance_Mode, '') <> '{distVal}'
) A;

-- STEP 3: Final results with Extract_Number
SELECT
    ROW_NUMBER() OVER (ORDER BY Control_Type, Student_Number, Student_Qualification_Code, CREG_Course_Code) AS Extract_Number,
    Control_Type, Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
    Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
    CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058, Validation_Result
FROM #Rule18_Validation
ORDER BY Control_Type, Student_Number, Student_Qualification_Code, CREG_Course_Code;

DROP TABLE IF EXISTS #Rule18_Base;
DROP TABLE IF EXISTS #Rule18_Validation;";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule18ValidationSummary> AnalyseAsync(Rule18ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var studTable = Sanitise(request.StudTable);
            var bridgeTable = Sanitise(request.BridgeTable);
            var crseTable = Sanitise(request.CrseTable);

            var studCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{studTable}];");
            var bridgeCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{bridgeTable}];");
            var crseCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{crseTable}];");

            var nsfasFilterCol = Sanitise(string.IsNullOrWhiteSpace(request.NsfasFilterCol) ? "_019" : request.NsfasFilterCol);
            var nsfasFilterValue = string.IsNullOrWhiteSpace(request.NsfasFilterValue) ? "NS" : request.NsfasFilterValue;
            var foundationFilterCol = Sanitise(string.IsNullOrWhiteSpace(request.FoundationFilterCol) ? "_091" : request.FoundationFilterCol);
            var foundationFilterValue = string.IsNullOrWhiteSpace(request.FoundationFilterValue) ? "Y" : request.FoundationFilterValue;
            var distanceFilterCol = Sanitise(string.IsNullOrWhiteSpace(request.DistanceFilterCol) ? "_024" : request.DistanceFilterCol);
            var distanceFilterValue = string.IsNullOrWhiteSpace(request.DistanceFilterValue) ? "D" : request.DistanceFilterValue;
            var credJoinCol = Sanitise(string.IsNullOrWhiteSpace(request.CredJoinCol) ? "_001" : request.CredJoinCol);
            var credCourseCol = Sanitise(string.IsNullOrWhiteSpace(request.CredCourseCol) ? "_030" : request.CredCourseCol);
            var crseCourseCol = Sanitise(string.IsNullOrWhiteSpace(request.CrseCourseCol) ? "_030" : request.CrseCourseCol);
            var crseNameCol = Sanitise(string.IsNullOrWhiteSpace(request.CrseNameCol) ? "_058" : request.CrseNameCol);

            await using (var prepCmd = conn.CreateConfiguredCommand())
            {
                prepCmd.CommandText = BuildRule18PrepSql(studTable, bridgeTable, crseTable,
                    nsfasFilterCol, nsfasFilterValue.Replace("'", "''"),
                    foundationFilterCol, foundationFilterValue.Replace("'", "''"),
                    distanceFilterCol, distanceFilterValue.Replace("'", "''"),
                    credJoinCol, credCourseCol, crseCourseCol, crseNameCol);
                prepCmd.CommandTimeout = 120;
                await prepCmd.ExecuteNonQueryAsync();
            }

            await using var countCommand = conn.CreateConfiguredCommand();
            countCommand.CommandText = BuildRule18CountSql(nsfasFilterValue.Replace("'", "''"));
            await using var countReader = await countCommand.ExecuteReaderAsync();

            var nsfasPopulationCount = 0;
            var control1PopulationCount = 0;
            var control2PassPopulation = 0;
            var control3PassPopulation = 0;
            if (await countReader.ReadAsync())
            {
                nsfasPopulationCount = GetInt(countReader, 0);
                control1PopulationCount = GetInt(countReader, 1);
                control2PassPopulation = GetInt(countReader, 2);
                control3PassPopulation = GetInt(countReader, 3);
            }

            await countReader.CloseAsync();

            var reviewRows = await LoadControlRowsAsync(conn, includeAllReviewRows ? null : BrowserPreviewRowLimit);
            reviewRows = NormalizeReviewRows(reviewRows);

            var controlSummaries = BuildControlSummaries(
                control1PopulationCount, control2PassPopulation, control3PassPopulation,
                nsfasFilterCol, nsfasFilterValue,
                foundationFilterCol, foundationFilterValue,
                distanceFilterCol, distanceFilterValue);
            var totalValidated = controlSummaries.Sum(x => x.TotalCount);
            var passCount = controlSummaries.Sum(x => x.PassCount);
            var failCount = controlSummaries.Sum(x => x.FailCount);
            var isPreviewOnly = !includeAllReviewRows && totalValidated > reviewRows.Count;

            return new Rule18ValidationSummary
            {
                Success = true,
                StudRecordCount = studCount,
                BridgeRecordCount = bridgeCount,
                CrseRecordCount = crseCount,
                NsfasPopulationCount = nsfasPopulationCount,
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
                Server = request.Server,
                Database = request.Database,
                StudTable = request.StudTable,
                BridgeTable = request.BridgeTable,
                CrseTable = request.CrseTable,
                Control1FilterCol = nsfasFilterCol,
                Control1FilterValue = nsfasFilterValue,
                NsfasFilterCol = nsfasFilterCol,
                NsfasFilterValue = nsfasFilterValue,
                FoundationFilterCol = foundationFilterCol,
                FoundationFilterValue = foundationFilterValue,
                DistanceFilterCol = distanceFilterCol,
                DistanceFilterValue = distanceFilterValue,
                CredJoinCol = credJoinCol,
                CredCourseCol = credCourseCol,
                CrseCourseCol = crseCourseCol,
                CrseNameCol = crseNameCol,
                TableLinkageText = $"{request.StudTable}.{credJoinCol} → {request.BridgeTable}.{credJoinCol} | {request.BridgeTable}.{credCourseCol} → {request.CrseTable}.{crseCourseCol}",
                RuleModeText = "100% population testing of all matching control rows",
                ProcedureSteps = BuildProcedureSteps(request.StudTable, request.BridgeTable, request.CrseTable, credJoinCol, credCourseCol, crseCourseCol, crseNameCol),
                ClientId = request.ClientId,
                ControlSummaries = controlSummaries,
                ReviewRows = reviewRows,
                Warning = includeAllReviewRows
                    ? "Rule 18 completed with the full matching control result set."
                    : "Counts reflect the full matching control result set. Browser review rows are limited for performance."
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule18ValidationRequest request, Rule18ValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 18);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 18);
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
    @ClientID, @UserID, 18, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @BridgeTable, @CrseTable, NULL,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, @WorkspaceSavedAt, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "NSFAS Student Validation");
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
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule18|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private static string BuildRule18PrepSql(
            string studTable, string bridgeTable, string crseTable,
            string nsfasCol, string nsfasVal,
            string foundCol, string foundVal,
            string distCol, string distVal,
            string credJoinCol, string credCourseCol,
            string crseCourseCol, string crseNameCol) => $@"
SET NOCOUNT ON;
DROP TABLE IF EXISTS #Rule18_Base;
DROP TABLE IF EXISTS #Rule18_Validation;

SELECT
    CAST(S.[_007]                AS nvarchar(255)) AS Student_Number,
    CAST(S.[_001]                AS nvarchar(255)) AS Student_Qualification_Code,
    CAST(S.[{nsfasCol}]          AS nvarchar(255)) AS NSFAS_Status,
    CAST(S.[{distCol}]           AS nvarchar(255)) AS Attendance_Mode,
    CAST(S.[_025]                AS nvarchar(255)) AS Qualification_Fulfilled_Indicator,
    CAST(BRIDGE.[{credJoinCol}]  AS nvarchar(255)) AS CREG_Qualification_Code,
    CAST(BRIDGE.[{credCourseCol}] AS nvarchar(255)) AS CREG_Course_Code,
    CAST(CRSE.[{crseCourseCol}]  AS nvarchar(255)) AS CRSE_Course_Code,
    CAST(CRSE.[{foundCol}]       AS nvarchar(255)) AS Foundation_Course_Indicator,
    CAST(CRSE.[{crseNameCol}]    AS nvarchar(255)) AS CRSE_058
INTO #Rule18_Base
FROM [{studTable}] S
INNER JOIN [{bridgeTable}] BRIDGE ON S.[{credJoinCol}] = BRIDGE.[{credJoinCol}]
INNER JOIN [{crseTable}] CRSE ON BRIDGE.[{credCourseCol}] = CRSE.[{crseCourseCol}];

SELECT *
INTO #Rule18_Validation
FROM (
    SELECT 'Control_1' AS Control_Type,
           Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
           Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
           CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058,
           'PASS' AS Validation_Result
    FROM #Rule18_Base
    WHERE ISNULL(NSFAS_Status, '') = '{nsfasVal}'
      AND ISNULL(Foundation_Course_Indicator, '') = '{foundVal}'

    UNION ALL

    SELECT 'Control_2' AS Control_Type,
           Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
           Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
           CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058,
           'PASS' AS Validation_Result
    FROM #Rule18_Base
    WHERE ISNULL(NSFAS_Status, '') = '{nsfasVal}'
      AND ISNULL(Foundation_Course_Indicator, '') = '{foundVal}'
      AND ISNULL(Attendance_Mode, '') = '{distVal}'

    UNION ALL

    SELECT 'Control_3' AS Control_Type,
           Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
           Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
           CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058,
           'PASS' AS Validation_Result
    FROM #Rule18_Base
    WHERE ISNULL(NSFAS_Status, '') = '{nsfasVal}'
      AND ISNULL(Foundation_Course_Indicator, '') <> '{foundVal}'
      AND ISNULL(Attendance_Mode, '') <> '{distVal}'
) A;";

        private static string BuildRule18CountSql(string nsfasVal) => $@"
SELECT
    (SELECT COUNT(DISTINCT Student_Number) FROM #Rule18_Base WHERE ISNULL(NSFAS_Status, '') = '{nsfasVal}') AS NsfasCount,
    COUNT(CASE WHEN Control_Type = 'Control_1' THEN 1 END) AS Control1Count,
    COUNT(CASE WHEN Control_Type = 'Control_2' THEN 1 END) AS Control2Count,
    COUNT(CASE WHEN Control_Type = 'Control_3' THEN 1 END) AS Control3Count
FROM #Rule18_Validation;";

        private async Task<List<Rule18ValidationRowRecord>> LoadControlRowsAsync(
            SqlConnection connection, int? maxRows)
        {
            var perControlLimit = maxRows.HasValue && maxRows.Value > 0
                ? Math.Max(maxRows.Value / 3, 1)
                : 0;

            var sql = perControlLimit > 0
                ? $@"
SELECT
    ROW_NUMBER() OVER (ORDER BY Control_Type, Student_Number, CREG_Course_Code) AS Extract_Number,
    Control_Type, Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
    Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
    CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058, Validation_Result
FROM (
    SELECT *,
        ROW_NUMBER() OVER (PARTITION BY Control_Type ORDER BY Student_Number, CREG_Course_Code) AS Preview_Row_Num
    FROM #Rule18_Validation
) X
WHERE Preview_Row_Num <= {perControlLimit}
ORDER BY Control_Type, Student_Number, CREG_Course_Code;"
                : @"
SELECT
    ROW_NUMBER() OVER (ORDER BY Control_Type, Student_Number, CREG_Course_Code) AS Extract_Number,
    Control_Type, Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
    Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
    CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058, Validation_Result
FROM #Rule18_Validation
ORDER BY Control_Type, Student_Number, CREG_Course_Code;";

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule18ValidationRowRecord>();
            while (await reader.ReadAsync())
            {
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                rows.Add(new Rule18ValidationRowRecord
                {
                    ValidationNumber = rows.Count + 1,
                    ControlType = ReadValue(displayValues, "Control_Type"),
                    ValidationResult = ReadValue(displayValues, "Validation_Result"),
                    DisplayValues = displayValues
                });

                EnrichRule18DisplayValues(rows[^1]);
            }

            return rows;
        }


        private static List<Rule18ControlSummaryItemViewModel> BuildControlSummaries(
            int c1Count,
            int c2Count,
            int c3Count,
            string nsfasFilterCol = "_019",
            string nsfasFilterValue = "NS",
            string foundationFilterCol = "_091",
            string foundationFilterValue = "Y",
            string distanceFilterCol = "_024",
            string distanceFilterValue = "D")
        {
            return new List<Rule18ControlSummaryItemViewModel>
            {
                BuildControlSummary("Control_1", "Control 1",
                    $"NSFAS_Status='{nsfasFilterValue}' AND Foundation_Course_Indicator='{foundationFilterValue}'",
                    c1Count),
                BuildControlSummary("Control_2", "Control 2",
                    $"NSFAS_Status='{nsfasFilterValue}' AND Foundation_Course_Indicator='{foundationFilterValue}' AND Attendance_Mode='{distanceFilterValue}'",
                    c2Count),
                BuildControlSummary("Control_3", "Control 3",
                    $"NSFAS_Status='{nsfasFilterValue}' AND Foundation_Course_Indicator<>'{foundationFilterValue}' AND Attendance_Mode<>'{distanceFilterValue}'",
                    c3Count)
            };
        }

        private static Rule18ControlSummaryItemViewModel BuildControlSummary(
            string controlType,
            string controlLabel,
            string criteriaText,
            int passCount)
        {
            return new Rule18ControlSummaryItemViewModel
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

        private static List<Rule18ValidationRowRecord> NormalizeReviewRows(IEnumerable<Rule18ValidationRowRecord>? rows)
        {
            var normalized = (rows ?? Enumerable.Empty<Rule18ValidationRowRecord>())
                .Select((row, index) =>
                {
                    row.ValidationNumber = index + 1;
                    return row;
                })
                .ToList();

            return normalized;
        }

        private async Task<Rule18ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule18ValidationSummary summary, string? server)
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
                    new Rule18ValidationRequest
                    {
                        ClientId = summary.ClientId,
                        RunId = summary.SavedRunId,
                        Server = server,
                        Database = summary.Database,
                        Driver = "ODBC Driver 17 for SQL Server",
                        StudTable = summary.StudTable,
                        BridgeTable = summary.BridgeTable,
                        CrseTable = summary.CrseTable,
                        Control1FilterCol = string.IsNullOrWhiteSpace(summary.Control1FilterCol) ? "_019" : summary.Control1FilterCol,
                        Control1FilterValue = string.IsNullOrWhiteSpace(summary.Control1FilterValue) ? "NS" : summary.Control1FilterValue,
                        NsfasFilterCol = string.IsNullOrWhiteSpace(summary.NsfasFilterCol) ? "_019" : summary.NsfasFilterCol,
                        NsfasFilterValue = string.IsNullOrWhiteSpace(summary.NsfasFilterValue) ? "NS" : summary.NsfasFilterValue,
                        FoundationFilterCol = string.IsNullOrWhiteSpace(summary.FoundationFilterCol) ? "_091" : summary.FoundationFilterCol,
                        FoundationFilterValue = string.IsNullOrWhiteSpace(summary.FoundationFilterValue) ? "Y" : summary.FoundationFilterValue,
                        DistanceFilterCol = string.IsNullOrWhiteSpace(summary.DistanceFilterCol) ? "_024" : summary.DistanceFilterCol,
                        DistanceFilterValue = string.IsNullOrWhiteSpace(summary.DistanceFilterValue) ? "D" : summary.DistanceFilterValue,
                        CredJoinCol = string.IsNullOrWhiteSpace(summary.CredJoinCol) ? "_001" : summary.CredJoinCol,
                        CredCourseCol = string.IsNullOrWhiteSpace(summary.CredCourseCol) ? "_030" : summary.CredCourseCol,
                        CrseCourseCol = string.IsNullOrWhiteSpace(summary.CrseCourseCol) ? "_030" : summary.CrseCourseCol,
                        CrseNameCol = string.IsNullOrWhiteSpace(summary.CrseNameCol) ? "_058" : summary.CrseNameCol
                    },
                    includeAllReviewRows: true);

                expanded.Timestamp = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                expanded.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                    ? "Saved Rule 18 results were expanded from the stored browser preview to the full result set."
                    : $"{summary.Warning} Full saved results were reloaded from the saved Rule 18 configuration.";

                return expanded;
            }
            catch
            {
                return summary;
            }
        }

        private static Rule18ValidationSummary CloneSummary(Rule18ValidationSummary summary)
        {
            return new Rule18ValidationSummary
            {
                Success = summary.Success,
                StudRecordCount = summary.StudRecordCount,
                BridgeRecordCount = summary.BridgeRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                NsfasPopulationCount = summary.NsfasPopulationCount,
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
                Server = summary.Server,
                Database = summary.Database,
                StudTable = summary.StudTable,
                BridgeTable = summary.BridgeTable,
                CrseTable = summary.CrseTable,
                Control1FilterCol = summary.Control1FilterCol,
                Control1FilterValue = summary.Control1FilterValue,
                NsfasFilterCol = summary.NsfasFilterCol,
                NsfasFilterValue = summary.NsfasFilterValue,
                FoundationFilterCol = summary.FoundationFilterCol,
                FoundationFilterValue = summary.FoundationFilterValue,
                DistanceFilterCol = summary.DistanceFilterCol,
                DistanceFilterValue = summary.DistanceFilterValue,
                CredJoinCol = summary.CredJoinCol,
                CredCourseCol = summary.CredCourseCol,
                CrseCourseCol = summary.CrseCourseCol,
                CrseNameCol = summary.CrseNameCol,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule18ControlSummaryItemViewModel
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

        private static Rule18ValidationRowRecord CloneReviewRow(Rule18ValidationRowRecord row)
        {
            return new Rule18ValidationRowRecord
            {
                ValidationNumber = row.ValidationNumber,
                ControlType = row.ControlType,
                ControlLabel = row.ControlLabel,
                ValidationResult = row.ValidationResult,
                ValidationExplanation = row.ValidationExplanation,
                DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static Rule18ValidationSummary CreateBrowserPreview(Rule18ValidationSummary summary)
        {
            var perControlLimit = Math.Max(BrowserPreviewRowLimit / 4, 1);
            var previewRows = summary.ReviewRows
                .GroupBy(row => row.ControlType, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => GetControlSort(group.Key))
                .SelectMany(group => group.OrderBy(row => row.ValidationNumber).Take(perControlLimit))
                .Take(BrowserPreviewRowLimit)
                .ToList();

            return new Rule18ValidationSummary
            {
                Success = summary.Success,
                StudRecordCount = summary.StudRecordCount,
                BridgeRecordCount = summary.BridgeRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                NsfasPopulationCount = summary.NsfasPopulationCount,
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
                Server = summary.Server,
                Database = summary.Database,
                StudTable = summary.StudTable,
                BridgeTable = summary.BridgeTable,
                CrseTable = summary.CrseTable,
                Control1FilterCol = summary.Control1FilterCol,
                Control1FilterValue = summary.Control1FilterValue,
                NsfasFilterCol = summary.NsfasFilterCol,
                NsfasFilterValue = summary.NsfasFilterValue,
                FoundationFilterCol = summary.FoundationFilterCol,
                FoundationFilterValue = summary.FoundationFilterValue,
                DistanceFilterCol = summary.DistanceFilterCol,
                DistanceFilterValue = summary.DistanceFilterValue,
                CredJoinCol = summary.CredJoinCol,
                CredCourseCol = summary.CredCourseCol,
                CrseCourseCol = summary.CrseCourseCol,
                CrseNameCol = summary.CrseNameCol,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule18ControlSummaryItemViewModel
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

        private static void ApplyBrowserPreview(Rule18ValidationSummary summary)
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

        private static List<string> BuildProcedureSteps(string studTable, string bridgeTable, string crseTable,
            string credJoinCol, string credCourseCol, string crseCourseCol, string crseNameCol) =>
            new()
            {
                $"Join {studTable}.[{credJoinCol}] to {bridgeTable}.[{credJoinCol}] (Student Qualification Code).",
                $"Join {bridgeTable}.[{credCourseCol}] to {crseTable}.[{crseCourseCol}] (Course Code link).",
                $"Select course name/type from {crseTable}.[{crseNameCol}].",
                "Evaluate the joined STUD, CRED, and CRSE rows using the three control populations.",
                "Return the full matching control result set for Control 1, Control 2, and Control 3."
            };

        private async Task EnsureColumnsExistAsync(
            string server, string database, string driver,
            string studTable, string bridgeTable, string crseTable,
            string? nsfasFilterCol = null,
            string? distanceFilterCol = null,
            string? foundationFilterCol = null,
            string? credJoinCol = null,
            string? credCourseCol = null,
            string? crseCourseCol = null,
            string? crseNameCol = null)
        {
            var studColumns = await GetTableColumnsAsync(server, database, driver, studTable);
            var bridgeColumns = await GetTableColumnsAsync(server, database, driver, bridgeTable);
            var crseColumns = await GetTableColumnsAsync(server, database, driver, crseTable);

            var requiredStudCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_001", "_007" };
            foreach (var col in new[] { nsfasFilterCol, distanceFilterCol, credJoinCol }.Where(c => !string.IsNullOrWhiteSpace(c)))
                requiredStudCols.Add(col!);

            var effectiveCredJoinCol = string.IsNullOrWhiteSpace(credJoinCol) ? "_001" : credJoinCol;
            var effectiveCredCourseCol = string.IsNullOrWhiteSpace(credCourseCol) ? "_030" : credCourseCol;
            var requiredBridgeCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { effectiveCredJoinCol, effectiveCredCourseCol };

            var effectiveCrseCourseCol = string.IsNullOrWhiteSpace(crseCourseCol) ? "_030" : crseCourseCol;
            var effectiveCrseNameCol = string.IsNullOrWhiteSpace(crseNameCol) ? "_058" : crseNameCol;
            var requiredCrseCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { effectiveCrseCourseCol, effectiveCrseNameCol };
            if (!string.IsNullOrWhiteSpace(foundationFilterCol))
                requiredCrseCols.Add(foundationFilterCol!);

            EnsureHasColumns(studTable, studColumns, requiredStudCols.ToArray());
            EnsureHasColumns(bridgeTable, bridgeColumns, requiredBridgeCols.ToArray());
            EnsureHasColumns(crseTable, crseColumns, requiredCrseCols.ToArray());
        }

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

        private static void ValidateRequest(Rule18ValidationRequest request)
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

        private static void ValidateRequest(Rule18VerifyRequest request)
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

        private string GetSystemConnectionString()
        {
            var server   = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"]   ?? "HEMISBaseSystem";
            var trust    = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);
            return new SqlConnectionStringBuilder
            {
                DataSource = server, InitialCatalog = database,
                IntegratedSecurity = true, TrustServerCertificate = trust,
                Encrypt = false, ConnectTimeout = 180
            }.ConnectionString;
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

        private static Rule18ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule18ValidationSummary>(decoded);
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

        private static void EnrichRule18DisplayValues(Rule18ValidationRowRecord row)
        {
            var values = row.DisplayValues;
            var controlType = ReadValue(values, "Control_Type");
            var nsfasStatus = FormatRule18ColumnValue(ReadValue(values, "NSFAS_Status"));
            var foundationIndicator = FormatRule18ColumnValue(ReadValue(values, "Foundation_Course_Indicator"));
            var attendanceMode = FormatRule18ColumnValue(ReadValue(values, "Attendance_Mode"));

            string controlLabel;
            string validationExplanation;

            switch (controlType)
            {
                case "Control_1":
                    controlLabel = $"NSFAS_Status='{nsfasStatus}' AND Foundation_Course_Indicator='{foundationIndicator}'";
                    validationExplanation = $"NSFAS student (NSFAS_Status='{nsfasStatus}') enrolled in a Foundation course (Foundation_Course_Indicator='{foundationIndicator}').";
                    break;
                case "Control_2":
                    controlLabel = $"NSFAS_Status='{nsfasStatus}' AND Foundation_Course_Indicator='{foundationIndicator}' AND Attendance_Mode='{attendanceMode}'";
                    validationExplanation = $"NSFAS student in a Foundation course studying via Distance (Attendance_Mode='{attendanceMode}').";
                    break;
                case "Control_3":
                    controlLabel = $"NSFAS_Status='{nsfasStatus}' AND Foundation_Course_Indicator<>'{foundationIndicator}' AND Attendance_Mode<>'{attendanceMode}'";
                    validationExplanation = $"NSFAS student NOT in a Foundation course and NOT studying via Distance (Foundation='{foundationIndicator}', Attendance_Mode='{attendanceMode}').";
                    break;
                default:
                    controlLabel = controlType;
                    validationExplanation = "";
                    break;
            }

            row.ControlLabel = controlLabel;
            row.ValidationExplanation = validationExplanation;
            values["Control_Label"] = controlLabel;
            values["Validation_Explanation"] = validationExplanation;
            values["FINAL_RULE_TEXT"] = controlLabel;
        }

        private static string FormatRule18ColumnValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "[blank]" : value.Trim();

        private static bool RequestsMatchForPendingSave(Rule18ValidationRequest current, Rule18ValidationRequest pending)
        {
            return current.ClientId == pending.ClientId &&
                   string.Equals(current.Server?.Trim(), pending.Server?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Database?.Trim(), pending.Database?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Driver?.Trim(), pending.Driver?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.StudTable?.Trim(), pending.StudTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.BridgeTable?.Trim(), pending.BridgeTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CrseTable?.Trim(), pending.CrseTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Control1FilterCol?.Trim(), pending.Control1FilterCol?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Control1FilterValue?.Trim(), pending.Control1FilterValue?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.NsfasFilterCol?.Trim(), pending.NsfasFilterCol?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.NsfasFilterValue?.Trim(), pending.NsfasFilterValue?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.FoundationFilterCol?.Trim(), pending.FoundationFilterCol?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.FoundationFilterValue?.Trim(), pending.FoundationFilterValue?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.DistanceFilterCol?.Trim(), pending.DistanceFilterCol?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.DistanceFilterValue?.Trim(), pending.DistanceFilterValue?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

        private static string ReadValue(IReadOnlyDictionary<string, string?> values, string key) =>
            values.TryGetValue(key, out var value) ? value ?? "" : "";

        // ─── Rule18Results table (system DB) ──────────────────────────────────

        private async Task EnsureRule18ResultsTableAsync(SqlConnection systemConn)
        {
            await using var cmd = systemConn.CreateConfiguredCommand();
            cmd.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
               WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='Rule18Results')
BEGIN
    CREATE TABLE dbo.Rule18Results (
        ResultID            BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RunID               INT           NOT NULL,
        Extract_Number      INT           NOT NULL,
        Control_Type        NVARCHAR(20)  NOT NULL,
        Student_Number      NVARCHAR(255) NULL,
        Student_Qualification_Code    NVARCHAR(255) NULL,
        NSFAS_Status        NVARCHAR(255) NULL,
        Attendance_Mode     NVARCHAR(255) NULL,
        Qualification_Fulfilled_Indicator NVARCHAR(255) NULL,
        CREG_Qualification_Code NVARCHAR(255) NULL,
        CREG_Course_Code    NVARCHAR(255) NULL,
        CRSE_Course_Code    NVARCHAR(255) NULL,
        Foundation_Course_Indicator NVARCHAR(255) NULL,
        CRSE_058            NVARCHAR(255) NULL,
        Validation_Result   NVARCHAR(50)  NULL
    );
    CREATE NONCLUSTERED INDEX IX_Rule18Results_RunID
        ON dbo.Rule18Results(RunID, Control_Type);
END";
            cmd.CommandTimeout = 60;
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<bool> HasRule18ResultsAsync(int runId)
        {
            if (runId <= 0) return false;
            try
            {
                await using var conn = await OpenSystemConnectionAsync();
                await EnsureRule18ResultsTableAsync(conn);
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM dbo.Rule18Results WHERE RunID = @RunID";
                cmd.Parameters.AddWithValue("@RunID", runId);
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return count > 0;
            }
            catch { return false; }
        }

        private async Task BulkCopyToRule18ResultsAsync(Rule18ValidationRequest request, int runId)
        {
            var hemisConnStr = BuildConnectionString(request.Server, request.Database,
                request.Driver ?? "ODBC Driver 17 for SQL Server");
            await using var hemisConn = new SqlConnection(hemisConnStr);
            await hemisConn.OpenAsync();

            await using (var prepCmd = hemisConn.CreateConfiguredCommand())
            {
                prepCmd.CommandText = BuildRule18PrepSql(
                    Sanitise(request.StudTable), Sanitise(request.BridgeTable), Sanitise(request.CrseTable),
                    Sanitise(string.IsNullOrWhiteSpace(request.NsfasFilterCol)       ? "_019" : request.NsfasFilterCol),
                    (string.IsNullOrWhiteSpace(request.NsfasFilterValue)       ? "NS" : request.NsfasFilterValue).Replace("'","''"),
                    Sanitise(string.IsNullOrWhiteSpace(request.FoundationFilterCol)  ? "_091" : request.FoundationFilterCol),
                    (string.IsNullOrWhiteSpace(request.FoundationFilterValue)  ? "Y"  : request.FoundationFilterValue).Replace("'","''"),
                    Sanitise(string.IsNullOrWhiteSpace(request.DistanceFilterCol)    ? "_024" : request.DistanceFilterCol),
                    (string.IsNullOrWhiteSpace(request.DistanceFilterValue)    ? "D"  : request.DistanceFilterValue).Replace("'","''"),
                    Sanitise(string.IsNullOrWhiteSpace(request.CredJoinCol)          ? "_001" : request.CredJoinCol),
                    Sanitise(string.IsNullOrWhiteSpace(request.CredCourseCol)        ? "_030" : request.CredCourseCol),
                    Sanitise(string.IsNullOrWhiteSpace(request.CrseCourseCol)        ? "_030" : request.CrseCourseCol),
                    Sanitise(string.IsNullOrWhiteSpace(request.CrseNameCol)          ? "_058" : request.CrseNameCol));
                prepCmd.CommandTimeout = 600;
                await prepCmd.ExecuteNonQueryAsync();
            }

            await using var selectCmd = hemisConn.CreateConfiguredCommand();
            selectCmd.CommandText = $@"
SELECT
    {runId} AS RunID,
    ROW_NUMBER() OVER (ORDER BY Control_Type, Student_Number, Student_Qualification_Code, CREG_Course_Code) AS Extract_Number,
    Control_Type, Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
    Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
    CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058, Validation_Result
FROM #Rule18_Validation
ORDER BY Control_Type, Student_Number, Student_Qualification_Code, CREG_Course_Code;";
            selectCmd.CommandTimeout = 600;
            await using var reader = await selectCmd.ExecuteReaderAsync();

            await using var systemConn = await OpenSystemConnectionAsync();
            await EnsureRule18ResultsTableAsync(systemConn);

            await using (var delCmd = systemConn.CreateConfiguredCommand())
            {
                delCmd.CommandText = "DELETE FROM dbo.Rule18Results WHERE RunID = @RunID";
                delCmd.Parameters.AddWithValue("@RunID", runId);
                delCmd.CommandTimeout = 60;
                await delCmd.ExecuteNonQueryAsync();
            }

            using var bulk = new SqlBulkCopy(systemConn)
            {
                DestinationTableName = "dbo.Rule18Results",
                BatchSize = 5000,
                BulkCopyTimeout = 600
            };
            await bulk.WriteToServerAsync(reader);
        }

        // ─── Streaming full-population exports ────────────────────────────────

        public async Task<byte[]> ExportFullExcelAsync(Rule18ValidationSummary summary, string? overrideServer = null)
        {
            var runId = summary.SavedRunId ?? 0;
            if (runId > 0 && await HasRule18ResultsAsync(runId))
                return await ExportExcelFromResultsTableAsync(summary, runId);

            // Fallback: re-query HEMIS directly (old runs or background copy still in progress)
            var server = string.IsNullOrWhiteSpace(overrideServer) ? summary.Server : overrideServer;
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(summary.Database))
                throw new InvalidOperationException("Cannot export: server or database is missing from the saved run.");

            var connStr = BuildConnectionString(server, summary.Database, "ODBC Driver 17 for SQL Server");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await Rule18RunPrepSqlAsync(conn, summary);

            var headers = Rule18ExportHeaders();
            using var wb = new XLWorkbook();
            Rule18WriteSummarySheet(wb, summary);
            var controls = new[]
            {
                ("Control_1", "Control 1 - NSFAS Foundation",  "RULE 18 CONTROL 1: NSFAS + FOUNDATION"),
                ("Control_2", "Control 2 - Distance",           "RULE 18 CONTROL 2: NSFAS + FOUNDATION + DISTANCE"),
                ("Control_3", "Control 3 - Non-Foundation",     "RULE 18 CONTROL 3: NSFAS + NOT FOUNDATION + NOT DISTANCE"),
            };
            foreach (var (controlType, sheetName, title) in controls)
                await Rule18WriteControlSheetAsync(wb, conn, sheetName, title, controlType, headers);
            await using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private async Task<byte[]> ExportExcelFromResultsTableAsync(Rule18ValidationSummary summary, int runId)
        {
            var systemConnStr = GetSystemConnectionString();
            var sheets = new Dictionary<string, object>
            {
                ["Summary"] = BuildRule18SummaryRows(summary),
                ["Control 1 - NSFAS Foundation"]  = Rule18ReadResultsSync(systemConnStr, runId, "Control_1"),
                ["Control 2 - Distance"]           = Rule18ReadResultsSync(systemConnStr, runId, "Control_2"),
                ["Control 3 - Non-Foundation"]     = Rule18ReadResultsSync(systemConnStr, runId, "Control_3"),
            };
            await using var ms = new MemoryStream();
            await ms.SaveAsAsync(sheets);
            return ms.ToArray();
        }

        private static IEnumerable<IDictionary<string, object>> BuildRule18SummaryRows(Rule18ValidationSummary summary) =>
            new List<IDictionary<string, object>>
            {
                new Dictionary<string, object> { ["Field"] = "Database",         ["Value"] = summary.Database },
                new Dictionary<string, object> { ["Field"] = "STUD Table",       ["Value"] = summary.StudTable },
                new Dictionary<string, object> { ["Field"] = "Bridge Table",     ["Value"] = summary.BridgeTable },
                new Dictionary<string, object> { ["Field"] = "CRSE Table",       ["Value"] = summary.CrseTable },
                new Dictionary<string, object> { ["Field"] = "Validation Date",  ["Value"] = summary.Timestamp },
                new Dictionary<string, object> { ["Field"] = "Join Path",        ["Value"] = summary.TableLinkageText },
                new Dictionary<string, object> { ["Field"] = "NSFAS Population", ["Value"] = summary.NsfasPopulationCount },
                new Dictionary<string, object> { ["Field"] = "Control Result Rows", ["Value"] = summary.TotalValidated },
                new Dictionary<string, object> { ["Field"] = "Matching Rows",    ["Value"] = summary.PassCount },
                new Dictionary<string, object> { ["Field"] = "Non-Matching Rows",["Value"] = summary.FailCount },
                new Dictionary<string, object> { ["Field"] = "Exception Rate",   ["Value"] = $"{summary.ExceptionRate:F2}%" },
                new Dictionary<string, object> { ["Field"] = "Status",           ["Value"] = summary.Status },
            };

        private static IEnumerable<IDictionary<string, object>> Rule18ReadResultsSync(
            string systemConnStr, int runId, string controlType)
        {
            using var conn = new SqlConnection(systemConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT Extract_Number, Control_Type, Student_Number, Student_Qualification_Code,
       NSFAS_Status, Attendance_Mode, Qualification_Fulfilled_Indicator,
       CREG_Qualification_Code, CREG_Course_Code, CRSE_Course_Code,
       Foundation_Course_Indicator, CRSE_058, Validation_Result
FROM dbo.Rule18Results
WHERE RunID = @RunID AND Control_Type = @ControlType
ORDER BY Extract_Number;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@ControlType", controlType);
            cmd.CommandTimeout = 300;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? "" : (reader.GetValue(i) ?? "");
                yield return row;
            }
        }

        public async Task<byte[]> ExportFullCsvAsync(Rule18ValidationSummary summary, string? overrideServer = null)
        {
            var runId = summary.SavedRunId ?? 0;
            if (runId > 0 && await HasRule18ResultsAsync(runId))
            {
                // Fast path: read from local Rule18Results table
                var systemConnStr = GetSystemConnectionString();
                await using var ms2 = new MemoryStream();
                await using var writer2 = new StreamWriter(ms2, new UTF8Encoding(false), bufferSize: 65536, leaveOpen: true);
                await writer2.WriteLineAsync("Extract_Number,Control_Type,Student_Number,Student_Qualification_Code,NSFAS_Status,Attendance_Mode,Qualification_Fulfilled_Indicator,CREG_Qualification_Code,CREG_Course_Code,CRSE_Course_Code,Foundation_Course_Indicator,CRSE_058,Validation_Result");
                await using var sysConn = new SqlConnection(systemConnStr);
                await sysConn.OpenAsync();
                await using var sysCmd = sysConn.CreateConfiguredCommand();
                sysCmd.CommandText = @"
SELECT Extract_Number, Control_Type, Student_Number, Student_Qualification_Code,
    NSFAS_Status, Attendance_Mode, Qualification_Fulfilled_Indicator,
    CREG_Qualification_Code, CREG_Course_Code, CRSE_Course_Code,
    Foundation_Course_Indicator, CRSE_058, Validation_Result
FROM dbo.Rule18Results WHERE RunID = @RunID
ORDER BY Extract_Number;";
                sysCmd.Parameters.AddWithValue("@RunID", runId);
                sysCmd.CommandTimeout = 300;
                await using var sysReader = await sysCmd.ExecuteReaderAsync();
                while (await sysReader.ReadAsync())
                {
                    var vals = new string[sysReader.FieldCount];
                    for (var i = 0; i < sysReader.FieldCount; i++)
                        vals[i] = Rule18CsvEscape(sysReader.IsDBNull(i) ? "" : sysReader.GetValue(i)?.ToString() ?? "");
                    await writer2.WriteLineAsync(string.Join(",", vals));
                }
                await writer2.FlushAsync();
                return ms2.ToArray();
            }

            // Fallback: re-query HEMIS directly
            var server = string.IsNullOrWhiteSpace(overrideServer) ? summary.Server : overrideServer;
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(summary.Database))
                throw new InvalidOperationException("Cannot export: server or database is missing from the saved run.");

            var connStr = BuildConnectionString(server, summary.Database, "ODBC Driver 17 for SQL Server");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await Rule18RunPrepSqlAsync(conn, summary);

            await using var ms = new MemoryStream();
            await using var writer = new StreamWriter(ms, new UTF8Encoding(false), bufferSize: 65536, leaveOpen: true);

            var headers = Rule18ExportHeaders();
            await writer.WriteLineAsync(string.Join(",", headers.Select(Rule18CsvEscape)));

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT
    ROW_NUMBER() OVER (ORDER BY Control_Type, Student_Number, Student_Qualification_Code, CREG_Course_Code) AS Extract_Number,
    Control_Type, Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
    Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
    CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058, Validation_Result
FROM #Rule18_Validation
ORDER BY Control_Type, Student_Number, Student_Qualification_Code, CREG_Course_Code;";
            cmd.CommandTimeout = 600;

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var values = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    values[i] = reader.IsDBNull(i) ? "" : (reader.GetValue(i)?.ToString() ?? "");
                await writer.WriteLineAsync(string.Join(",", values.Select(Rule18CsvEscape)));
            }

            await writer.FlushAsync();
            return ms.ToArray();
        }

        private async Task Rule18RunPrepSqlAsync(SqlConnection conn, Rule18ValidationSummary summary)
        {
            var studTable  = Sanitise(string.IsNullOrWhiteSpace(summary.StudTable) ? "dbo_STUD" : summary.StudTable);
            var bridgeTable = Sanitise(string.IsNullOrWhiteSpace(summary.BridgeTable) ? "dbo_CREG" : summary.BridgeTable);
            var crseTable  = Sanitise(string.IsNullOrWhiteSpace(summary.CrseTable) ? "dbo_CRSE" : summary.CrseTable);
            var nsfasCol   = Sanitise(string.IsNullOrWhiteSpace(summary.NsfasFilterCol) ? "_019" : summary.NsfasFilterCol);
            var nsfasVal   = (string.IsNullOrWhiteSpace(summary.NsfasFilterValue) ? "NS" : summary.NsfasFilterValue).Replace("'", "''");
            var foundCol   = Sanitise(string.IsNullOrWhiteSpace(summary.FoundationFilterCol) ? "_091" : summary.FoundationFilterCol);
            var foundVal   = (string.IsNullOrWhiteSpace(summary.FoundationFilterValue) ? "Y" : summary.FoundationFilterValue).Replace("'", "''");
            var distCol    = Sanitise(string.IsNullOrWhiteSpace(summary.DistanceFilterCol) ? "_024" : summary.DistanceFilterCol);
            var distVal    = (string.IsNullOrWhiteSpace(summary.DistanceFilterValue) ? "D" : summary.DistanceFilterValue).Replace("'", "''");
            var credJoinCol   = Sanitise(string.IsNullOrWhiteSpace(summary.CredJoinCol) ? "_001" : summary.CredJoinCol);
            var credCourseCol = Sanitise(string.IsNullOrWhiteSpace(summary.CredCourseCol) ? "_030" : summary.CredCourseCol);
            var crseCourseCol = Sanitise(string.IsNullOrWhiteSpace(summary.CrseCourseCol) ? "_030" : summary.CrseCourseCol);
            var crseNameCol   = Sanitise(string.IsNullOrWhiteSpace(summary.CrseNameCol) ? "_058" : summary.CrseNameCol);

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = BuildRule18PrepSql(studTable, bridgeTable, crseTable,
                nsfasCol, nsfasVal, foundCol, foundVal, distCol, distVal,
                credJoinCol, credCourseCol, crseCourseCol, crseNameCol);
            cmd.CommandTimeout = 600;
            await cmd.ExecuteNonQueryAsync();
        }

        private static List<string> Rule18ExportHeaders() =>
            new()
            {
                "Extract_Number", "Control_Type", "Student_Number", "Student_Qualification_Code",
                "NSFAS_Status", "Attendance_Mode", "Qualification_Fulfilled_Indicator",
                "CREG_Qualification_Code", "CREG_Course_Code", "CRSE_Course_Code",
                "Foundation_Course_Indicator", "CRSE_058", "Validation_Result"
            };

        private static void Rule18WriteSummarySheet(XLWorkbook wb, Rule18ValidationSummary summary)
        {
            var ws = wb.Worksheets.Add("Summary");
            Rule18StyleTitle(ws, 1, "HEMIS RULE 18: NSFAS STUDENT VALIDATION", 2);
            var data = new[]
            {
                ("Database", summary.Database),
                ("STUD Table", summary.StudTable),
                ("Bridge Table", summary.BridgeTable),
                ("CRSE Table", summary.CrseTable),
                ("Validation Date", summary.Timestamp),
                ("Join Path", summary.TableLinkageText),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("NSFAS Population", summary.NsfasPopulationCount.ToString("N0")),
                ("Control Result Rows", summary.TotalValidated.ToString("N0")),
                ("Matching Rows", summary.PassCount.ToString("N0")),
                ("Non-Matching Rows", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
            };
            var row = 2;
            foreach (var (label, value) in data)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdr = ws.Cell(row, 1);
                    hdr.Value = label;
                    hdr.Style.Font.Bold = true;
                    hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdr.Style.Font.FontColor = XLColor.White;
                    ws.Range(row, 1, row, 2).Merge();
                }
                else if (label != "")
                {
                    ws.Cell(row, 1).Value = label;
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    ws.Cell(row, 2).Value = value;
                }
                row++;
            }
            ws.Column(1).Width = 30;
            ws.Column(2).Width = 70;
        }

        private static async Task Rule18WriteControlSheetAsync(
            XLWorkbook wb, SqlConnection conn,
            string sheetName, string title, string controlType, List<string> headers)
        {
            const int MaxDataRows = 1_048_573;

            var sql = $@"
SELECT
    ROW_NUMBER() OVER (ORDER BY Student_Number, Student_Qualification_Code, CREG_Course_Code) AS Extract_Number,
    Control_Type, Student_Number, Student_Qualification_Code, NSFAS_Status, Attendance_Mode,
    Qualification_Fulfilled_Indicator, CREG_Qualification_Code,
    CREG_Course_Code, CRSE_Course_Code, Foundation_Course_Indicator, CRSE_058, Validation_Result
FROM #Rule18_Validation
WHERE Control_Type = '{controlType}'
ORDER BY Student_Number, Student_Qualification_Code, CREG_Course_Code;";

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 600;

            var ws = Rule18NewControlSheet(wb, sheetName, title, headers);
            var dataRow = 3;
            var part = 1;

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (dataRow > MaxDataRows + 2)
                {
                    Rule18SetColWidths(ws);
                    part++;
                    ws = Rule18NewControlSheet(wb, Rule18OverflowSheetName(sheetName, part), $"{title} (Part {part})", headers);
                    dataRow = 3;
                }
                for (var i = 0; i < reader.FieldCount; i++)
                    ws.Cell(dataRow, i + 1).Value = reader.IsDBNull(i) ? "" : (reader.GetValue(i)?.ToString() ?? "");
                dataRow++;
            }
            Rule18SetColWidths(ws);
        }

        private static string Rule18OverflowSheetName(string baseName, int part)
        {
            var suffix = $" Pt {part}";
            var max = 31 - suffix.Length;
            return (baseName.Length > max ? baseName[..max] : baseName) + suffix;
        }

        private static IXLWorksheet Rule18NewControlSheet(XLWorkbook wb, string sheetName, string title, List<string> headers)
        {
            var ws = wb.Worksheets.Add(sheetName);
            Rule18StyleTitle(ws, 1, title, headers.Count);
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            return ws;
        }

        private static void Rule18StyleTitle(IXLWorksheet ws, int row, string title, int colSpan)
        {
            ws.Range(row, 1, row, colSpan).Merge();
            var cell = ws.Cell(row, 1);
            cell.Value = title;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 14;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        private static void Rule18SetColWidths(IXLWorksheet ws)
        {
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 28;
            ws.Column(3).Width = 16;
            ws.Column(4).Width = 28;
            ws.Column(5).Width = 16;
            ws.Column(6).Width = 16;
            ws.Column(7).Width = 14;
            ws.Column(8).Width = 14;
            ws.Column(9).Width = 16;
            ws.Column(10).Width = 16;
            ws.Column(11).Width = 16;
            ws.Column(12).Width = 14;
            ws.Column(13).Width = 14;
        }

        private static string Rule18CsvEscape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}


