using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule12Service : IRule12Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule12Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<Rule12TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule12TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoCregTable = FindFirst(tables, ["dbo_CREG", "CREG"], ["creg"]),
                    AutoQualTable = FindFirst(tables, ["dbo_QUAL", "QUAL"], ["qual"]),
                    AutoCresTable = FindFirst(tables, ["dbo_CRES", "CRES"], ["cres"])
                };
            }
            catch (Exception ex)
            {
                return new Rule12TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                ValidateObjectName(tableName);
                var tbl = Sanitise(tableName);
                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @T ORDER BY ORDINAL_POSITION;";
                cmd.Parameters.AddWithValue("@T", tbl);
                await using var reader = await cmd.ExecuteReaderAsync();
                var cols = new List<string>();
                while (await reader.ReadAsync()) if (!reader.IsDBNull(0)) cols.Add(reader.GetString(0));
                var auto = cols.Contains("_030") ? "_030" : cols.FirstOrDefault();
                return new ColumnListResult { Success = true, Columns = cols, AutoSelected = auto };
            }
            catch (Exception ex) { return new ColumnListResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule12VerifyResult> VerifyTablesAsync(Rule12VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request);

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var cregTable      = Sanitise(request.CregTable);
                var qualTable      = Sanitise(!string.IsNullOrWhiteSpace(request.QualTable) ? request.QualTable : "dbo_QUAL");
                var cresTable      = Sanitise(!string.IsNullOrWhiteSpace(request.CresTable) ? request.CresTable : "dbo_CRES");
                var cresStatusCol  = Sanitise(!string.IsNullOrWhiteSpace(request.CresStatusCol) ? request.CresStatusCol : "_031");
                var cresStatusFilter = !string.IsNullOrWhiteSpace(request.CresStatusFilter) ? Sanitise(request.CresStatusFilter.Trim()) : "A";

                var (cregCount, qualCount, cresActiveCount, totalActiveStudents, matchedQuals, missingQuals) =
                    await RunPrepAndGetCountsAsync(conn,
                        BuildRule12PrepSql(
                            cregTable, qualTable, cresTable,
                            Sanitise(!string.IsNullOrWhiteSpace(request.CregStudentCol) ? request.CregStudentCol : "_007"),
                            Sanitise(!string.IsNullOrWhiteSpace(request.CregQualCol) ? request.CregQualCol : "_001"),
                            Sanitise(!string.IsNullOrWhiteSpace(request.CregCourseCol) ? request.CregCourseCol : "_030"),
                            Sanitise(!string.IsNullOrWhiteSpace(request.QualJoinCol) ? request.QualJoinCol : "_001"),
                            Sanitise(!string.IsNullOrWhiteSpace(request.QualDescCol) ? request.QualDescCol : "_003"),
                            Sanitise(!string.IsNullOrWhiteSpace(request.CresCourseCol) ? request.CresCourseCol : "_030"),
                            cresStatusCol, cresStatusFilter,
                            string.IsNullOrWhiteSpace(request.CregExtra1Col) ? "" : Sanitise(request.CregExtra1Col),
                            string.IsNullOrWhiteSpace(request.CregExtra2Col) ? "" : Sanitise(request.CregExtra2Col),
                            string.IsNullOrWhiteSpace(request.CregFilterCol) ? "" : Sanitise(request.CregFilterCol),
                            request.CregFilterValues?.Trim() ?? "",
                            string.IsNullOrWhiteSpace(request.CregExtra3Col) ? "" : Sanitise(request.CregExtra3Col),
                            string.IsNullOrWhiteSpace(request.CresExtra1Col) ? "" : Sanitise(request.CresExtra1Col)),
                        cregTable, qualTable);

                return new Rule12VerifyResult
                {
                    Success             = true,
                    CregRecordCount     = cregCount,
                    QualRecordCount     = qualCount,
                    CresActiveCount     = cresActiveCount,
                    TotalActiveStudents = totalActiveStudents,
                    MatchedQualCount    = matchedQuals,
                    MissingQualCount    = missingQuals
                };
            }
            catch (Exception ex)
            {
                return new Rule12VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule12ValidationSummary> RunValidationAsync(Rule12ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request);

                var summary = await AnalyseAsync(request, includeAllReviewRows: true);

                if (summary.Success && request.ClientId > 0)
                {
                    summary.SavedRunId = null;
                    var runId = await SaveValidationRunAsync(CloneValidationRequest(request), summary, userEmail, userName, markWorkspaceSaved: false);
                    summary.SavedRunId = runId;

                    if (!string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.ClearPending(12, request.ClientId, userEmail!);

                    summary.Warning = "Rule 12 analysis complete. Click Save Workspace to finalise for signoff.";
                }
                else if (summary.Success)
                {
                    summary.Warning = "Rule 12 analysis complete. Select a client to save the workspace.";
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule12ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule12ValidationSummary> GetExportSummaryAsync(Rule12ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public Task<Rule12ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail)
        {
            var pending = _pendingValidationCache.GetPending<Rule12ValidationRequest, Rule12ValidationSummary>(12, clientId, reviewerEmail);
            if (pending == null)
                return Task.FromResult<Rule12ValidationSummary?>(null);

            var preview = CloneSummary(pending.Summary);
            preview.SavedRunId = null;
            preview.Warning = "This Rule 12 validation is still pending. Click Save Workspace to write it to the system database.";
            ApplyBrowserPreview(preview);
            return Task.FromResult<Rule12ValidationSummary?>(preview);
        }

        public Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail)
            => Task.FromResult(_pendingValidationCache.HasPending(12, clientId, reviewerEmail));

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule12WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.StudTable, '') AS CregTable,
    ISNULL(vr.DeceasedTable, '') AS CrseTable,
    ISNULL(vr.StudColumn, '') AS LegacyCrseTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON,
    CASE WHEN vr.WorkspaceSavedAt IS NOT NULL
          OR EXISTS (SELECT 1 FROM dbo.ReviewSignoffs rs WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst')
         THEN 1 ELSE 0 END AS IsWorkspaceSaved
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 12
ORDER BY vr.IsCurrent DESC, vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var runId = reader.GetInt32(0);
            var workspaceClientId = reader.GetInt32(1);
            var server = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var database = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var cregTable = reader.IsDBNull(4) ? "dbo_CREG" : reader.GetString(4);
            var crseTable = reader.IsDBNull(5) ? (reader.IsDBNull(6) ? "dbo_CRSE" : reader.GetString(6)) : reader.GetString(5);
            var currentStatus = reader.IsDBNull(7) ? "" : reader.GetString(7);
            var lastEditedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8);
            DateTime? lastEditedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9);
            var encodedSummary = reader.IsDBNull(10) ? null : reader.GetString(10);
            var isWorkspaceSaved = !reader.IsDBNull(11) && reader.GetInt32(11) == 1;

            await reader.CloseAsync();

            Rule12ValidationSummary? deserializedSummary = null;
            Rule12WorkspaceSummaryMetadata? summaryMetadata = null;

            if (!string.IsNullOrWhiteSpace(encodedSummary))
            {
                if (includeSummary)
                {
                    deserializedSummary = DeserializeSummary(encodedSummary);
                    if (deserializedSummary != null)
                    {
                        deserializedSummary = await ExpandSavedSummaryIfNeededAsync(deserializedSummary, server);
                        ApplyBrowserPreview(deserializedSummary);
                    }
                    else
                    {
                        summaryMetadata = DeserializeWorkspaceSummaryMetadata(encodedSummary);
                    }
                }
                else
                {
                    summaryMetadata = DeserializeWorkspaceSummaryMetadata(encodedSummary);
                }
            }

            var summary = includeSummary ? deserializedSummary : null;
            var summaryQualTable = deserializedSummary?.QualTable ?? summaryMetadata?.QualTable;
            var summaryCresTable = deserializedSummary?.CresTable ?? summaryMetadata?.CresTable;
            var summaryCregStudentCol = deserializedSummary?.CregStudentCol ?? summaryMetadata?.CregStudentCol;
            var summaryCregQualCol = deserializedSummary?.CregQualCol ?? summaryMetadata?.CregQualCol;
            var summaryCregCourseCol = deserializedSummary?.CregCourseCol ?? summaryMetadata?.CregCourseCol;
            var summaryQualJoinCol = deserializedSummary?.QualJoinCol ?? summaryMetadata?.QualJoinCol;
            var summaryQualDescCol = deserializedSummary?.QualDescCol ?? summaryMetadata?.QualDescCol;
            var summaryCresCourseCol = deserializedSummary?.CresCourseCol ?? summaryMetadata?.CresCourseCol;
            var summaryCresStatusCol     = deserializedSummary?.CresStatusCol    ?? summaryMetadata?.CresStatusCol;
            var summaryCresStatusFilter  = deserializedSummary?.CresStatusFilter ?? summaryMetadata?.CresStatusFilter;
            var summaryCregExtra1Col     = deserializedSummary?.CregExtra1Col    ?? summaryMetadata?.CregExtra1Col;
            var summaryCregExtra2Col     = deserializedSummary?.CregExtra2Col    ?? summaryMetadata?.CregExtra2Col;
            var summaryCregFilterCol     = deserializedSummary?.CregFilterCol    ?? summaryMetadata?.CregFilterCol;
            var summaryCregFilterValues  = deserializedSummary?.CregFilterValues ?? summaryMetadata?.CregFilterValues;
            var summaryCregExtra3Col     = deserializedSummary?.CregExtra3Col    ?? summaryMetadata?.CregExtra3Col;
            var summaryCresExtra1Col     = deserializedSummary?.CresExtra1Col    ?? summaryMetadata?.CresExtra1Col;

            var workspace = new Rule12WorkspaceStateViewModel
            {
                ClientId         = workspaceClientId,
                RunId            = runId,
                Server           = server,
                Database         = database,
                CregTable        = cregTable,
                QualTable        = summaryQualTable     ?? "dbo_QUAL",
                CresTable        = summaryCresTable     ?? "dbo_CRES",
                CregStudentCol   = summaryCregStudentCol ?? "_007",
                CregQualCol      = summaryCregQualCol   ?? "_001",
                CregCourseCol    = summaryCregCourseCol ?? "_030",
                QualJoinCol      = summaryQualJoinCol   ?? "_001",
                QualDescCol      = summaryQualDescCol   ?? "_003",
                CresCourseCol    = summaryCresCourseCol ?? "_030",
                CresStatusCol    = summaryCresStatusCol  ?? "_031",
                CresStatusFilter = summaryCresStatusFilter ?? "A",
                CregExtra1Col    = summaryCregExtra1Col  ?? "_064",
                CregExtra2Col    = summaryCregExtra2Col  ?? "_032",
                CregFilterCol    = summaryCregFilterCol  ?? "_051",
                CregFilterValues = summaryCregFilterValues ?? "",
                CregExtra3Col    = summaryCregExtra3Col   ?? "_018",
                CresExtra1Col    = summaryCresExtra1Col   ?? "_058",
                CurrentStatus    = currentStatus,
                LastEditedByUserName = lastEditedByUserName,
                LastEditedAt     = lastEditedAt,
                Summary          = summary
            };

            if (summary != null)
                workspace.CurrentStatus = summary.Status;

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
            workspace.IsWorkspaceSaved = isWorkspaceSaved;

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            return workspace;
        }

        public async Task<Rule12RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 12;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var savedRunId = reader.GetInt32(0);
            var clientId = reader.GetInt32(1);
            var isCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2);
            var engagementName = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var maconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var sourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var encodedSummary = reader.IsDBNull(6) ? null : reader.GetString(6);
            var summary = DeserializeSummary(encodedSummary);
            if (summary == null)
                return null;

            summary.ClientId = clientId;
            if (summary.SavedRunId.GetValueOrDefault() <= 0)
                summary.SavedRunId = runId;

            await reader.CloseAsync();

            summary = await ExpandSavedSummaryIfNeededAsync(summary, sourceServer);

            if (includeFullResults)
            {
                summary.DisplayedCount = summary.ReviewRows.Count;
                summary.IsPreviewOnly = false;
                summary.PreviewLimit = 0;
            }
            else
            {
                ApplyBrowserPreview(summary);
            }

            var review = new Rule12RunReviewViewModel
            {
                RunId = savedRunId,
                ClientId = clientId,
                IsCurrentRun = isCurrentRun,
                EngagementName = engagementName,
                MaconomyNumber = maconomyNumber,
                SourceServer = sourceServer,
                Summary = summary
            };

            review.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";
            review.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            review.HasDataAnalystSignoff = review.Signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            review.IsWorkspaceSaved = await IsWorkspaceSavedAsync(connection, runId);

            return review;
        }

        public async Task<Rule12WorkspaceSaveResult> SaveWorkspaceAsync(Rule12ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                ValidateRequest(request);

                if (!request.RunId.HasValue || request.RunId.Value <= 0)
                {
                    return new Rule12WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run Rule 12 validation first before saving the workspace."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule12WorkspaceSaveResult
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
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule12|{request.RunId.Value}|{request.ClientId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(12, request.ClientId, reviewerEmail);

                var currentWorkspace = new Rule12WorkspaceStateViewModel
                {
                    ClientId = request.ClientId,
                    RunId = request.RunId,
                    Server = request.Server ?? "",
                    Database = request.Database ?? "",
                    Driver = string.IsNullOrWhiteSpace(request.Driver) ? "ODBC Driver 17 for SQL Server" : request.Driver,
                    CregTable = string.IsNullOrWhiteSpace(request.CregTable) ? "dbo_CREG" : request.CregTable,
                    QualTable = string.IsNullOrWhiteSpace(request.QualTable) ? "dbo_QUAL" : request.QualTable,
                    CresTable = string.IsNullOrWhiteSpace(request.CresTable) ? "dbo_CRES" : request.CresTable,
                    CregStudentCol = string.IsNullOrWhiteSpace(request.CregStudentCol) ? "_007" : request.CregStudentCol,
                    CregQualCol = string.IsNullOrWhiteSpace(request.CregQualCol) ? "_001" : request.CregQualCol,
                    CregCourseCol = string.IsNullOrWhiteSpace(request.CregCourseCol) ? "_030" : request.CregCourseCol,
                    QualJoinCol = string.IsNullOrWhiteSpace(request.QualJoinCol) ? "_001" : request.QualJoinCol,
                    QualDescCol = string.IsNullOrWhiteSpace(request.QualDescCol) ? "_003" : request.QualDescCol,
                    CresCourseCol = string.IsNullOrWhiteSpace(request.CresCourseCol) ? "_030" : request.CresCourseCol,
                    CresStatusCol = string.IsNullOrWhiteSpace(request.CresStatusCol) ? "_031" : request.CresStatusCol,
                    CresStatusFilter = string.IsNullOrWhiteSpace(request.CresStatusFilter) ? "A" : request.CresStatusFilter,
                    CregExtra1Col = string.IsNullOrWhiteSpace(request.CregExtra1Col) ? "_064" : request.CregExtra1Col,
                    CregExtra2Col = string.IsNullOrWhiteSpace(request.CregExtra2Col) ? "_032" : request.CregExtra2Col,
                    CregFilterCol = string.IsNullOrWhiteSpace(request.CregFilterCol) ? "_051" : request.CregFilterCol,
                    CregFilterValues = request.CregFilterValues ?? "",
                    CregExtra3Col = string.IsNullOrWhiteSpace(request.CregExtra3Col) ? "_018" : request.CregExtra3Col,
                    CresExtra1Col = string.IsNullOrWhiteSpace(request.CresExtra1Col) ? "_058" : request.CresExtra1Col,
                    CurrentStatus = "Needs Review",
                    LastEditedByUserName = reviewerName ?? reviewerEmail,
                    LastEditedAt = DateTime.UtcNow,
                    IsWorkspaceSaved = true,
                    HasDataAnalystSignoff = false,
                    CurrentUserHasSignedOff = false,
                    CurrentUserSignoffComment = ""
                };
                return new Rule12WorkspaceSaveResult
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
            catch (Exception ex)
            {
                return new Rule12WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule12WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule12WorkspaceSaveResult
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
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule12|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(12, clientId.Value, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule12WorkspaceSaveResult
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
                return new Rule12WorkspaceSaveResult
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
                throw new InvalidOperationException("The selected Rule 12 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off a Rule 12 run.");

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
                throw new InvalidOperationException("The selected Rule 12 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule12ValidationRequest request)
        {
            ValidateRequest(request);

            var cregTable      = Sanitise(request.CregTable);
            var qualTable      = Sanitise(!string.IsNullOrWhiteSpace(request.QualTable) ? request.QualTable : "dbo_QUAL");
            var cresTable      = Sanitise(!string.IsNullOrWhiteSpace(request.CresTable) ? request.CresTable : "dbo_CRES");
            var cregStudentCol = Sanitise(!string.IsNullOrWhiteSpace(request.CregStudentCol) ? request.CregStudentCol : "_007");
            var cregQualCol    = Sanitise(!string.IsNullOrWhiteSpace(request.CregQualCol) ? request.CregQualCol : "_001");
            var cregCourseCol  = Sanitise(!string.IsNullOrWhiteSpace(request.CregCourseCol) ? request.CregCourseCol : "_030");
            var qualJoinCol    = Sanitise(!string.IsNullOrWhiteSpace(request.QualJoinCol) ? request.QualJoinCol : "_001");
            var qualDescCol    = Sanitise(!string.IsNullOrWhiteSpace(request.QualDescCol) ? request.QualDescCol : "_003");
            var cresCourseCol  = Sanitise(!string.IsNullOrWhiteSpace(request.CresCourseCol) ? request.CresCourseCol : "_030");
            var cresStatusCol  = Sanitise(!string.IsNullOrWhiteSpace(request.CresStatusCol) ? request.CresStatusCol : "_031");
            var cresStatusFilter  = !string.IsNullOrWhiteSpace(request.CresStatusFilter) ? request.CresStatusFilter.Trim() : "A";
            var cregExtra1Col     = string.IsNullOrWhiteSpace(request.CregExtra1Col) ? "" : Sanitise(request.CregExtra1Col);
            var cregExtra2Col     = string.IsNullOrWhiteSpace(request.CregExtra2Col) ? "" : Sanitise(request.CregExtra2Col);
            var cregFilterCol     = string.IsNullOrWhiteSpace(request.CregFilterCol) ? "" : Sanitise(request.CregFilterCol);
            var cregFilterValues  = request.CregFilterValues?.Trim() ?? "";
            var cregExtra3Col     = string.IsNullOrWhiteSpace(request.CregExtra3Col) ? "" : Sanitise(request.CregExtra3Col);
            var cresExtra1Col     = string.IsNullOrWhiteSpace(request.CresExtra1Col) ? "" : Sanitise(request.CresExtra1Col);

            var extraCols = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(cregExtra1Col)) extraCols.Append($"\n    a.CREG__EXTRA1 AS CREG__{cregExtra1Col.TrimStart('_')},");
            if (!string.IsNullOrWhiteSpace(cregExtra2Col)) extraCols.Append($"\n    a.CREG__EXTRA2 AS CREG__{cregExtra2Col.TrimStart('_')},");
            if (!string.IsNullOrWhiteSpace(cregFilterCol)) extraCols.Append($"\n    a.CREG__FILTER AS CREG__{cregFilterCol.TrimStart('_')},");
            if (!string.IsNullOrWhiteSpace(cregExtra3Col)) extraCols.Append($"\n    a.CREG__EXTRA3 AS CREG__{cregExtra3Col.TrimStart('_')},");
            if (!string.IsNullOrWhiteSpace(cresExtra1Col)) extraCols.Append($"\n    a.CRES__EXTRA1 AS CRES__{cresExtra1Col.TrimStart('_')},");

            var sql = $@"-- HEMIS RULE 12: ACTIVE STUDENTS
-- Tables: [{cregTable}], [{qualTable}], [{cresTable}]
-- Join 1: [{cregTable}].[{cregQualCol}] = [{qualTable}].[{qualJoinCol}]
-- Join 2: [{cregTable}].[{cregCourseCol}] = [{cresTable}].[{cresCourseCol}]
-- Filter: [{cresTable}].[{cresStatusCol}] = '{cresStatusFilter}'
-- Extra CREG cols: {(string.IsNullOrWhiteSpace(cregExtra1Col) ? "none" : cregExtra1Col)}, {(string.IsNullOrWhiteSpace(cregExtra2Col) ? "none" : cregExtra2Col)}, filter={cregFilterCol} ({cregFilterValues}), {(string.IsNullOrWhiteSpace(cregExtra3Col) ? "none" : cregExtra3Col)}
-- Extra CRES col: {(string.IsNullOrWhiteSpace(cresExtra1Col) ? "none" : cresExtra1Col)}
-- PASS when the active student qualification exists in [{qualTable}]

{BuildRule12PrepSql(cregTable, qualTable, cresTable, cregStudentCol, cregQualCol, cregCourseCol, qualJoinCol, qualDescCol, cresCourseCol, cresStatusCol, cresStatusFilter, cregExtra1Col, cregExtra2Col, cregFilterCol, cregFilterValues, cregExtra3Col, cresExtra1Col)}
SELECT
    'Control_1' AS Control_Type,
    'CONTROL 1: [{cregTable}].[{cregQualCol}] = [{qualTable}].[{qualJoinCol}] WHERE [{cresTable}].[{cresStatusCol}] = ''{cresStatusFilter}''' AS Control_Label,
    a.CREG__007,
    a.CREG__001,
    a.CREG__030,{extraCols}
    q.QUAL__001,
    q.QUAL__003,
    a.CRES__030,
    a.CRES__031,
    CASE WHEN q.QUAL__001 IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Validation_Result
FROM #Rule12Active a
LEFT JOIN #Rule12Quals q ON q.QUAL__001 = a.CREG__001
ORDER BY a.CREG__007, a.CREG__001;

SELECT
    (SELECT COUNT(1) FROM #Rule12Active) AS Active_Students,
    (SELECT COUNT(1) FROM #Rule12Active a LEFT JOIN #Rule12Quals q ON q.QUAL__001 = a.CREG__001 WHERE q.QUAL__001 IS NOT NULL) AS Matched_Quals,
    (SELECT COUNT(1) FROM #Rule12Active a LEFT JOIN #Rule12Quals q ON q.QUAL__001 = a.CREG__001 WHERE q.QUAL__001 IS NULL) AS Missing_Quals;";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule12ValidationSummary> AnalyseAsync(Rule12ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var cregTable        = Sanitise(request.CregTable);
            var qualTable        = Sanitise(!string.IsNullOrWhiteSpace(request.QualTable) ? request.QualTable : "dbo_QUAL");
            var cresTable        = Sanitise(!string.IsNullOrWhiteSpace(request.CresTable) ? request.CresTable : "dbo_CRES");
            var cregStudentCol   = Sanitise(!string.IsNullOrWhiteSpace(request.CregStudentCol) ? request.CregStudentCol : "_007");
            var cregQualCol      = Sanitise(!string.IsNullOrWhiteSpace(request.CregQualCol) ? request.CregQualCol : "_001");
            var cregCourseCol    = Sanitise(!string.IsNullOrWhiteSpace(request.CregCourseCol) ? request.CregCourseCol : "_030");
            var qualJoinCol      = Sanitise(!string.IsNullOrWhiteSpace(request.QualJoinCol) ? request.QualJoinCol : "_001");
            var qualDescCol      = Sanitise(!string.IsNullOrWhiteSpace(request.QualDescCol) ? request.QualDescCol : "_003");
            var cresCourseCol    = Sanitise(!string.IsNullOrWhiteSpace(request.CresCourseCol) ? request.CresCourseCol : "_030");
            var cresStatusCol    = Sanitise(!string.IsNullOrWhiteSpace(request.CresStatusCol) ? request.CresStatusCol : "_031");
            var cresStatusFilter = !string.IsNullOrWhiteSpace(request.CresStatusFilter) ? request.CresStatusFilter.Trim() : "A";
            var cregExtra1Col    = string.IsNullOrWhiteSpace(request.CregExtra1Col) ? "" : Sanitise(request.CregExtra1Col);
            var cregExtra2Col    = string.IsNullOrWhiteSpace(request.CregExtra2Col) ? "" : Sanitise(request.CregExtra2Col);
            var cregFilterCol    = string.IsNullOrWhiteSpace(request.CregFilterCol) ? "" : Sanitise(request.CregFilterCol);
            var cregFilterValues = request.CregFilterValues?.Trim() ?? "";
            var cregExtra3Col    = string.IsNullOrWhiteSpace(request.CregExtra3Col) ? "" : Sanitise(request.CregExtra3Col);
            var cresExtra1Col    = string.IsNullOrWhiteSpace(request.CresExtra1Col) ? "" : Sanitise(request.CresExtra1Col);

            var (cregCount, qualCount, cresActiveCount, totalActiveStudents, matchedQuals, missingQuals) =
                await RunPrepAndGetCountsAsync(conn,
                    BuildRule12PrepSql(cregTable, qualTable, cresTable, cregStudentCol, cregQualCol, cregCourseCol,
                        qualJoinCol, qualDescCol, cresCourseCol, cresStatusCol, cresStatusFilter,
                        cregExtra1Col, cregExtra2Col, cregFilterCol, cregFilterValues, cregExtra3Col, cresExtra1Col),
                    cregTable, qualTable);

            var reviewRows = await LoadControlRowsFromPrepAsync(conn, cregTable, qualTable, cresTable, includeAllReviewRows ? null : BrowserPreviewRowLimit, cregQualCol, qualJoinCol, cresStatusCol, cresStatusFilter);
            reviewRows = NormalizeReviewRows(reviewRows);

            var controlSummaries = BuildControlSummaries(totalActiveStudents, matchedQuals, cregTable, qualTable, cresTable, cregQualCol, qualJoinCol, cresStatusCol, cresStatusFilter);
            var totalValidated = controlSummaries.Sum(x => x.TotalCount);
            var passCount = controlSummaries.Sum(x => x.PassCount);
            var failCount = controlSummaries.Sum(x => x.FailCount);
            var isPreviewOnly = !includeAllReviewRows && totalValidated > reviewRows.Count;

            return new Rule12ValidationSummary
            {
                Success = true,
                CregRecordCount  = cregCount,
                QualRecordCount  = qualCount,
                CresActiveCount  = cresActiveCount,
                TotalRequested   = totalValidated,
                TotalValidated   = totalValidated,
                DisplayedCount   = reviewRows.Count,
                IsPreviewOnly    = isPreviewOnly,
                PreviewLimit     = isPreviewOnly ? BrowserPreviewRowLimit : 0,
                PassCount        = passCount,
                FailCount        = failCount,
                ExceptionRate    = totalValidated == 0 ? 0m : Math.Round(failCount * 100m / totalValidated, 2),
                Status           = failCount == 0 ? "PASS" : "FAIL",
                Timestamp        = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database         = request.Database,
                CregTable        = request.CregTable,
                QualTable        = qualTable,
                CresTable        = cresTable,
                CregStudentCol   = cregStudentCol,
                CregQualCol      = cregQualCol,
                CregCourseCol    = cregCourseCol,
                QualJoinCol      = qualJoinCol,
                QualDescCol      = qualDescCol,
                CresCourseCol    = cresCourseCol,
                CresStatusCol    = cresStatusCol,
                CresStatusFilter = cresStatusFilter,
                CregExtra1Col    = cregExtra1Col,
                CregExtra2Col    = cregExtra2Col,
                CregFilterCol    = cregFilterCol,
                CregFilterValues = cregFilterValues,
                CregExtra3Col    = cregExtra3Col,
                CresExtra1Col    = cresExtra1Col,
                TableLinkageText = $"{request.CregTable}.{cregQualCol} = {qualTable}.{qualJoinCol} | {request.CregTable}.{cregCourseCol} = {cresTable}.{cresCourseCol} WHERE {cresTable}.{cresStatusCol} = '{cresStatusFilter}'",
                RuleModeText     = $"Active students from {request.CregTable} (CRES.{cresStatusCol}='{cresStatusFilter}') qualification codes tested against {qualTable}.{qualJoinCol}",
                ProcedureSteps   = BuildProcedureSteps(request.CregTable, qualTable, cresTable, cresStatusCol, cresStatusFilter),
                ClientId         = request.ClientId,
                ControlSummaries = controlSummaries,
                ReviewRows       = reviewRows,
                Warning = includeAllReviewRows
                    ? "Rule 12 completed with the full active student population result set."
                    : "Counts reflect the full active student population result set. Browser review rows are limited for performance."
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule12ValidationRequest request, Rule12ValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 12);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 12);
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
    @ClientID, @UserID, 12, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @BridgeTable, @CrseTable, NULL,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, @WorkspaceSavedAt, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Course Selection from dbo_CREG");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.CregTable);
            command.Parameters.AddWithValue("@BridgeTable", request.CresTable);
            command.Parameters.AddWithValue("@CrseTable", request.CresTable);
            // Persist only the fail rows in ResultsJSON — storing all PASS rows for large datasets
            // (hundreds of thousands of records) would make the DB blob unmanageably large.
            var persistedSummary = CloneSummary(summary);
            persistedSummary.SavedRunId = summary.SavedRunId;
            persistedSummary.ReviewRows = failRows.ToList();
            persistedSummary.DisplayedCount = failRows.Count;
            persistedSummary.IsPreviewOnly = false;
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
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule12|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            persistedSummary.SavedRunId = runId;
            await UpdateStoredSummaryAsync(connection, runId, persistedSummary);

            return runId;
        }

        private static async Task RunPrepSqlAsync(SqlConnection connection, string prepSql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = prepSql;
            await command.ExecuteNonQueryAsync();
        }

        private static string BuildRule12CountAfterPrepSql(string cregTable, string qualTable) =>
            $@"SELECT
    (SELECT COUNT(1) FROM [{cregTable}]) AS CregCount,
    (SELECT COUNT(1) FROM [{qualTable}]) AS QualCount,
    (SELECT COUNT(1) FROM #Rule12CresActive) AS CresActiveCount,
    (SELECT COUNT(1) FROM #Rule12Active) AS TotalActiveStudents,
    SUM(CASE WHEN q.QUAL__001 IS NOT NULL THEN 1 ELSE 0 END) AS MatchedQuals,
    SUM(CASE WHEN q.QUAL__001 IS NULL     THEN 1 ELSE 0 END) AS MissingQuals
FROM #Rule12Active a
LEFT JOIN #Rule12Quals q ON q.QUAL__001 = a.CREG__001;";

        private static async Task<(int cregCount, int qualCount, int cresActiveCount, int totalActiveStudents, int matchedQuals, int missingQuals)>
            RunPrepAndGetCountsAsync(SqlConnection conn, string prepSql, string cregTable, string qualTable)
        {
            await using var command = conn.CreateConfiguredCommand();
            command.CommandText = "SET NOCOUNT ON;\n" + prepSql + "\n" + BuildRule12CountAfterPrepSql(cregTable, qualTable);
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                if (reader.FieldCount >= 6 && await reader.ReadAsync())
                    return (GetInt(reader, 0), GetInt(reader, 1), GetInt(reader, 2),
                            GetInt(reader, 3), GetInt(reader, 4), GetInt(reader, 5));
            }
            while (await reader.NextResultAsync());
            return (0, 0, 0, 0, 0, 0);
        }

        private static async Task<Dictionary<string, List<string>>> GetTableColumnsMultiAsync(
            SqlConnection conn, params string[] tableNames)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tableNames) result[t] = new List<string>();
            if (tableNames.Length == 0) return result;

            await using var cmd = conn.CreateConfiguredCommand();
            var placeholders = tableNames.Select((_, i) => $"@T{i}").ToArray();
            cmd.CommandText = $"SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME IN ({string.Join(",", placeholders)}) ORDER BY TABLE_NAME, ORDINAL_POSITION;";
            for (var i = 0; i < tableNames.Length; i++)
                cmd.Parameters.AddWithValue($"@T{i}", tableNames[i]);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tbl = reader.GetString(0);
                if (result.TryGetValue(tbl, out var cols) && !reader.IsDBNull(1))
                    cols.Add(reader.GetString(1));
            }
            return result;
        }

        private static string BuildRule12PrepSql(
            string cregTable, string qualTable, string cresTable,
            string cregStudentCol, string cregQualCol, string cregCourseCol,
            string qualJoinCol, string qualDescCol,
            string cresCourseCol, string cresStatusCol, string cresStatusFilter,
            string cregExtra1Col = "", string cregExtra2Col = "",
            string cregFilterCol = "", string cregFilterValues = "",
            string cregExtra3Col = "", string cresExtra1Col = "")
        {
            var extra1Sql   = string.IsNullOrWhiteSpace(cregExtra1Col)
                ? "CAST(NULL AS nvarchar(255))"
                : $"CONVERT(nvarchar(255), cr.[{cregExtra1Col}])";
            var extra2Sql   = string.IsNullOrWhiteSpace(cregExtra2Col)
                ? "CAST(NULL AS nvarchar(255))"
                : $"CONVERT(nvarchar(255), cr.[{cregExtra2Col}])";
            var filterSql   = string.IsNullOrWhiteSpace(cregFilterCol)
                ? "CAST(NULL AS nvarchar(255))"
                : $"UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{cregFilterCol}]))))";
            var extra3Sql   = string.IsNullOrWhiteSpace(cregExtra3Col)
                ? "CAST(NULL AS nvarchar(255))"
                : $"CONVERT(nvarchar(255), cr.[{cregExtra3Col}])";
            var filterWhere = BuildFilterWhereClause(cregFilterCol, cregFilterValues);
            var cresExtra1SelectInto = string.IsNullOrWhiteSpace(cresExtra1Col)
                ? "CAST(NULL AS nvarchar(255)) AS CresExtra1"
                : $"CONVERT(nvarchar(255), cres.[{cresExtra1Col}]) AS CresExtra1";
            var cresExtra1ResultSql = string.IsNullOrWhiteSpace(cresExtra1Col)
                ? "CAST(NULL AS nvarchar(255))"
                : "ca.CresExtra1";

            return $@"
DROP TABLE IF EXISTS #Rule12CresActive;
DROP TABLE IF EXISTS #Rule12Active;
DROP TABLE IF EXISTS #Rule12Quals;

SELECT UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), cres.[{cresCourseCol}])))) AS CourseCode,
    {cresExtra1SelectInto}
INTO #Rule12CresActive
FROM [{cresTable}] cres
WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), cres.[{cresStatusCol}])))) = '{cresStatusFilter}';

CREATE INDEX IX_Rule12CresActive ON #Rule12CresActive (CourseCode);

SELECT
    CONVERT(nvarchar(255), cr.[{cregStudentCol}])                              AS CREG__007,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{cregQualCol}]))))            AS CREG__001,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{cregCourseCol}]))))          AS CREG__030,
    {extra1Sql}                                                                 AS CREG__EXTRA1,
    {extra2Sql}                                                                 AS CREG__EXTRA2,
    {filterSql}                                                                 AS CREG__FILTER,
    {extra3Sql}                                                                 AS CREG__EXTRA3,
    ca.CourseCode                                                               AS CRES__030,
    '{cresStatusFilter}'                                                        AS CRES__031,
    {cresExtra1ResultSql}                                                       AS CRES__EXTRA1
INTO #Rule12Active
FROM [{cregTable}] cr
INNER JOIN #Rule12CresActive ca
    ON ca.CourseCode = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{cregCourseCol}]))))
WHERE cr.[{cregQualCol}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{cregQualCol}]))) <> ''{filterWhere};

CREATE INDEX IX_Rule12Active ON #Rule12Active (CREG__001);

SELECT DISTINCT
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), q.[{qualJoinCol}])))) AS QUAL__001,
    CONVERT(nvarchar(255), q.[{qualDescCol}])                      AS QUAL__003
INTO #Rule12Quals
FROM [{qualTable}] q
WHERE q.[{qualJoinCol}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), q.[{qualJoinCol}]))) <> '';

CREATE INDEX IX_Rule12Quals ON #Rule12Quals (QUAL__001);";
        }

        private static string BuildFilterWhereClause(string filterCol, string filterValues)
        {
            if (string.IsNullOrWhiteSpace(filterCol) || string.IsNullOrWhiteSpace(filterValues))
                return "";

            var values = filterValues
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => Sanitise(v).ToUpperInvariant())
                .Where(v => v.Length > 0)
                .Select(v => $"'{v}'")
                .ToList();

            return values.Count == 0
                ? ""
                : $"\n  AND UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{filterCol}])))) IN ({string.Join(", ", values)})";
        }

        private static string BuildCountFromPrepSql() => @"
SELECT
    (SELECT COUNT(1) FROM #Rule12Active) AS TotalActiveStudents,
    SUM(CASE WHEN q.QUAL__001 IS NOT NULL THEN 1 ELSE 0 END) AS MatchedQuals,
    SUM(CASE WHEN q.QUAL__001 IS NULL     THEN 1 ELSE 0 END) AS MissingQuals
FROM #Rule12Active a
LEFT JOIN #Rule12Quals q ON q.QUAL__001 = a.CREG__001;";

        private static string BuildReviewFromPrepSql(
            int? maxRows,
            string cregTable, string qualTable, string cresTable,
            string cregQualCol, string qualJoinCol, string cresStatusCol, string cresStatusFilter)
        {
            var topClause = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value}" : string.Empty;
            return $@"
SELECT {topClause}
    1 AS Control_Sort,
    'Control_1' AS Control_Type,
    'CONTROL 1: [{cregTable}].[{cregQualCol}] = [{qualTable}].[{qualJoinCol}] WHERE [{cresTable}].[{cresStatusCol}] = ''{cresStatusFilter}''' AS Control_Label,
    CASE WHEN q.QUAL__001 IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Validation_Result,
    CASE
        WHEN q.QUAL__001 IS NOT NULL
            THEN 'Active student qualification exists in {qualTable}.'
        ELSE 'Active student qualification does not exist in {qualTable}.'
    END AS Validation_Explanation,
    a.CREG__007,
    a.CREG__001,
    a.CREG__030,
    a.CREG__EXTRA1,
    a.CREG__EXTRA2,
    a.CREG__FILTER,
    a.CREG__EXTRA3,
    q.QUAL__001,
    q.QUAL__003,
    a.CRES__030,
    a.CRES__031,
    a.CRES__EXTRA1
FROM #Rule12Active a
LEFT JOIN #Rule12Quals q ON q.QUAL__001 = a.CREG__001
ORDER BY a.CREG__007, a.CREG__001;";
        }

        private async Task<List<Rule12ValidationRowRecord>> LoadControlRowsFromPrepAsync(
            SqlConnection connection,
            string cregTable, string qualTable, string cresTable,
            int? maxRows,
            string cregQualCol, string qualJoinCol, string cresStatusCol, string cresStatusFilter)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = BuildReviewFromPrepSql(maxRows, cregTable, qualTable, cresTable, cregQualCol, qualJoinCol, cresStatusCol, cresStatusFilter);

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule12ValidationRowRecord>();
            while (await reader.ReadAsync())
            {
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                rows.Add(new Rule12ValidationRowRecord
                {
                    ValidationNumber = rows.Count + 1,
                    ControlType = ReadValue(displayValues, "Control_Type"),
                    ControlLabel = ReadValue(displayValues, "Control_Label"),
                    ValidationResult = ReadValue(displayValues, "Validation_Result"),
                    ValidationExplanation = ReadValue(displayValues, "Validation_Explanation"),
                    DisplayValues = displayValues
                });

                EnrichRule12DisplayValues(rows[^1]);
            }

            return rows;
        }

        private static string BuildPopulationCountSql(
            string cregTable, string qualTable, string cresTable,
            string cregStudentCol = "_007", string cregQualCol = "_001", string cregCourseCol = "_030",
            string qualJoinCol = "_001", string qualDescCol = "_003",
            string cresCourseCol = "_030", string cresStatusCol = "_031", string cresStatusFilter = "A") => $@"
{BuildRule12SourceCtes(cregTable, qualTable, cresTable, cregStudentCol, cregQualCol, cregCourseCol, qualJoinCol, qualDescCol, cresCourseCol, cresStatusCol, cresStatusFilter)}
SELECT
    COUNT(1) AS TotalActiveStudents,
    SUM(CASE WHEN QUAL__001 IS NOT NULL THEN 1 ELSE 0 END) AS MatchedQuals,
    SUM(CASE WHEN QUAL__001 IS NULL     THEN 1 ELSE 0 END) AS MissingQuals
FROM ValidationResults;";

        private async Task<List<Rule12ValidationRowRecord>> LoadControlRowsAsync(
            SqlConnection connection,
            string cregTable, string qualTable, string cresTable,
            int? maxRows,
            string cregStudentCol = "_007", string cregQualCol = "_001", string cregCourseCol = "_030",
            string qualJoinCol = "_001", string qualDescCol = "_003",
            string cresCourseCol = "_030", string cresStatusCol = "_031", string cresStatusFilter = "A")
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = BuildAllControlsSql(cregTable, qualTable, cresTable, maxRows, cregStudentCol, cregQualCol, cregCourseCol, qualJoinCol, qualDescCol, cresCourseCol, cresStatusCol, cresStatusFilter);

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule12ValidationRowRecord>();
            while (await reader.ReadAsync())
            {
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                rows.Add(new Rule12ValidationRowRecord
                {
                    ValidationNumber = rows.Count + 1,
                    ControlType = ReadValue(displayValues, "Control_Type"),
                    ControlLabel = ReadValue(displayValues, "Control_Label"),
                    ValidationResult = ReadValue(displayValues, "Validation_Result"),
                    ValidationExplanation = ReadValue(displayValues, "Validation_Explanation"),
                    DisplayValues = displayValues
                });

                EnrichRule12DisplayValues(rows[^1]);
            }

            return rows;
        }

        private static string BuildAllControlsSql(
            string cregTable, string qualTable, string cresTable,
            int? maxRows,
            string cregStudentCol = "_007", string cregQualCol = "_001", string cregCourseCol = "_030",
            string qualJoinCol = "_001", string qualDescCol = "_003",
            string cresCourseCol = "_030", string cresStatusCol = "_031", string cresStatusFilter = "A",
            string cresExtra1Col = "")
        {
            var topClause = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value}" : string.Empty;
            return $@"
{BuildRule12SourceCtes(cregTable, qualTable, cresTable, cregStudentCol, cregQualCol, cregCourseCol, qualJoinCol, qualDescCol, cresCourseCol, cresStatusCol, cresStatusFilter, cresExtra1Col)}
SELECT {topClause}
    1 AS Control_Sort,
    'Control_1' AS Control_Type,
    'CONTROL 1: [{cregTable}].[{cregQualCol}] = [{qualTable}].[{qualJoinCol}] WHERE [{cresTable}].[{cresStatusCol}] = ''{cresStatusFilter}''' AS Control_Label,
    CASE WHEN QUAL__001 IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Validation_Result,
    CASE
        WHEN QUAL__001 IS NOT NULL
            THEN 'Active student qualification exists in {qualTable}.'
        ELSE 'Active student qualification does not exist in {qualTable}.'
    END AS Validation_Explanation,
    CREG__007,
    CREG__001,
    CREG__030,
    QUAL__001,
    QUAL__003,
    CRES__030,
    CRES__031,
    CRES__EXTRA1
FROM ValidationResults
ORDER BY CREG__007, CREG__001;";
        }

        private static string BuildRule12SourceCtes(
            string cregTable, string qualTable, string cresTable,
            string cregStudentCol = "_007", string cregQualCol = "_001", string cregCourseCol = "_030",
            string qualJoinCol = "_001", string qualDescCol = "_003",
            string cresCourseCol = "_030", string cresStatusCol = "_031", string cresStatusFilter = "A",
            string cresExtra1Col = "")
        {
            var cresExtra1Select = string.IsNullOrWhiteSpace(cresExtra1Col)
                ? "CAST(NULL AS nvarchar(255)) AS CRES__EXTRA1"
                : $"CONVERT(nvarchar(255), CRES.[{cresExtra1Col}]) AS CRES__EXTRA1";
            return $@"
WITH ActiveStudents AS
(
    SELECT
        CONVERT(nvarchar(255), CREG.[{cregStudentCol}])                              AS CREG__007,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CREG.[{cregQualCol}]))))            AS CREG__001,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CREG.[{cregCourseCol}]))))          AS CREG__030,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CRES.[{cresCourseCol}]))))          AS CRES__030,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CRES.[{cresStatusCol}]))))          AS CRES__031,
        {cresExtra1Select}
    FROM [{cregTable}] CREG
    INNER JOIN [{cresTable}] CRES
        ON  UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CRES.[{cresCourseCol}]))))
          = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CREG.[{cregCourseCol}]))))
    WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CRES.[{cresStatusCol}])))) = '{cresStatusFilter}'
      AND CREG.[{cregQualCol}] IS NOT NULL
      AND LTRIM(RTRIM(CONVERT(nvarchar(255), CREG.[{cregQualCol}]))) <> ''
),
ReferenceQuals AS
(
    SELECT DISTINCT
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[{qualJoinCol}]))))  AS QUAL__001,
        CONVERT(nvarchar(255), QUAL.[{qualDescCol}])                       AS QUAL__003
    FROM [{qualTable}] QUAL
    WHERE QUAL.[{qualJoinCol}] IS NOT NULL
      AND LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[{qualJoinCol}]))) <> ''
),
ValidationResults AS
(
    SELECT
        Stud.CREG__007,
        Stud.CREG__001,
        Stud.CREG__030,
        Stud.CRES__030,
        Stud.CRES__031,
        Stud.CRES__EXTRA1,
        Ref.QUAL__001,
        Ref.QUAL__003
    FROM ActiveStudents Stud
    LEFT JOIN ReferenceQuals Ref ON Ref.QUAL__001 = Stud.CREG__001
)";
        }

        private static List<Rule12ControlSummaryItemViewModel> BuildControlSummaries(
            int totalActiveStudents, int matchedQuals,
            string cregTable, string qualTable, string cresTable,
            string cregQualCol, string qualJoinCol, string cresStatusCol, string cresStatusFilter)
        {
            return new List<Rule12ControlSummaryItemViewModel>
            {
                BuildControlSummary(
                    "Control_1",
                    "Control 1",
                    $"{cregTable}.{cregQualCol} = {qualTable}.{qualJoinCol} WHERE {cresTable}.{cresStatusCol} = '{cresStatusFilter}'",
                    totalActiveStudents,
                    matchedQuals)
            };
        }

        private static Rule12ControlSummaryItemViewModel BuildControlSummary(
            string controlType,
            string controlLabel,
            string criteriaText,
            int totalCount,
            int passCount)
        {
            var failCount = Math.Max(totalCount - passCount, 0);
            return new Rule12ControlSummaryItemViewModel
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

        private static List<Rule12ValidationRowRecord> NormalizeReviewRows(IEnumerable<Rule12ValidationRowRecord>? rows)
        {
            var normalized = (rows ?? Enumerable.Empty<Rule12ValidationRowRecord>())
                .Select((row, index) =>
                {
                    row.ValidationNumber = index + 1;
                    return row;
                })
                .ToList();

            return normalized;
        }

        private async Task<Rule12ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule12ValidationSummary summary, string? server)
        {
            var hasAllRows = !summary.IsPreviewOnly && summary.ReviewRows.Count >= summary.TotalValidated;
            if (hasAllRows)
            {
                return summary;
            }

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(summary.Database) ||
                string.IsNullOrWhiteSpace(summary.CregTable) ||
                string.IsNullOrWhiteSpace(summary.CresTable))
            {
                return summary;
            }

            try
            {
                var expanded = await AnalyseAsync(
                    new Rule12ValidationRequest
                    {
                        ClientId         = summary.ClientId,
                        RunId            = summary.SavedRunId,
                        Server           = server,
                        Database         = summary.Database,
                        Driver           = "ODBC Driver 17 for SQL Server",
                        CregTable        = summary.CregTable,
                        QualTable        = !string.IsNullOrWhiteSpace(summary.QualTable) ? summary.QualTable : "dbo_QUAL",
                        CresTable        = !string.IsNullOrWhiteSpace(summary.CresTable) ? summary.CresTable : "dbo_CRES",
                        CregStudentCol   = !string.IsNullOrWhiteSpace(summary.CregStudentCol) ? summary.CregStudentCol : "_007",
                        CregQualCol      = !string.IsNullOrWhiteSpace(summary.CregQualCol) ? summary.CregQualCol : "_001",
                        CregCourseCol    = !string.IsNullOrWhiteSpace(summary.CregCourseCol) ? summary.CregCourseCol : "_030",
                        QualJoinCol      = !string.IsNullOrWhiteSpace(summary.QualJoinCol) ? summary.QualJoinCol : "_001",
                        QualDescCol      = !string.IsNullOrWhiteSpace(summary.QualDescCol) ? summary.QualDescCol : "_003",
                        CresCourseCol    = !string.IsNullOrWhiteSpace(summary.CresCourseCol) ? summary.CresCourseCol : "_030",
                        CresStatusCol    = !string.IsNullOrWhiteSpace(summary.CresStatusCol) ? summary.CresStatusCol : "_031",
                        CresStatusFilter = !string.IsNullOrWhiteSpace(summary.CresStatusFilter) ? summary.CresStatusFilter : "A",
                        CregExtra1Col    = !string.IsNullOrWhiteSpace(summary.CregExtra1Col) ? summary.CregExtra1Col : "_064",
                        CregExtra2Col    = !string.IsNullOrWhiteSpace(summary.CregExtra2Col) ? summary.CregExtra2Col : "_032",
                        CregFilterCol    = !string.IsNullOrWhiteSpace(summary.CregFilterCol) ? summary.CregFilterCol : "_051",
                        CregFilterValues = summary.CregFilterValues ?? "",
                        CregExtra3Col    = !string.IsNullOrWhiteSpace(summary.CregExtra3Col) ? summary.CregExtra3Col : "_018",
                        CresExtra1Col    = !string.IsNullOrWhiteSpace(summary.CresExtra1Col) ? summary.CresExtra1Col : "_058"
                    },
                    includeAllReviewRows: true);

                expanded.Timestamp = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                expanded.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                    ? "Saved Rule 12 results were expanded from the stored browser preview to the full result set."
                    : $"{summary.Warning} Full saved results were reloaded from the saved Rule 12 configuration.";

                return expanded;
            }
            catch
            {
                return summary;
            }
        }

        private async Task<Rule12ValidationSummary> ExpandAndPersistSavedSummaryIfNeededAsync(SqlConnection connection, int runId, Rule12ValidationSummary summary, string? server)
        {
            var expanded = await ExpandSavedSummaryIfNeededAsync(summary, server);
            if (!ReferenceEquals(expanded, summary))
            {
                expanded.SavedRunId = runId;
                await UpdateStoredSummaryAsync(connection, runId, expanded);
            }

            return expanded;
        }

        private static Rule12ValidationSummary CloneSummary(Rule12ValidationSummary summary)
        {
            return new Rule12ValidationSummary
            {
                Success          = summary.Success,
                CregRecordCount  = summary.CregRecordCount,
                QualRecordCount  = summary.QualRecordCount,
                CresActiveCount  = summary.CresActiveCount,
                TotalRequested   = summary.TotalRequested,
                TotalValidated   = summary.TotalValidated,
                DisplayedCount   = summary.DisplayedCount,
                IsPreviewOnly    = summary.IsPreviewOnly,
                PreviewLimit     = summary.PreviewLimit,
                PassCount        = summary.PassCount,
                FailCount        = summary.FailCount,
                ExceptionRate    = summary.ExceptionRate,
                Status           = summary.Status,
                Timestamp        = summary.Timestamp,
                Database         = summary.Database,
                CregTable        = summary.CregTable,
                QualTable        = summary.QualTable,
                CresTable        = summary.CresTable,
                CregStudentCol   = summary.CregStudentCol,
                CregQualCol      = summary.CregQualCol,
                CregCourseCol    = summary.CregCourseCol,
                QualJoinCol      = summary.QualJoinCol,
                QualDescCol      = summary.QualDescCol,
                CresCourseCol    = summary.CresCourseCol,
                CresStatusCol    = summary.CresStatusCol,
                CresStatusFilter = summary.CresStatusFilter,
                CregExtra1Col    = summary.CregExtra1Col,
                CregExtra2Col    = summary.CregExtra2Col,
                CregFilterCol    = summary.CregFilterCol,
                CregFilterValues = summary.CregFilterValues,
                CregExtra3Col    = summary.CregExtra3Col,
                CresExtra1Col    = summary.CresExtra1Col,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText     = summary.RuleModeText,
                ProcedureSteps   = summary.ProcedureSteps.ToList(),
                ClientId         = summary.ClientId,
                SavedRunId       = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule12ControlSummaryItemViewModel
                    {
                        ControlType    = item.ControlType,
                        ControlLabel   = item.ControlLabel,
                        CriteriaText   = item.CriteriaText,
                        RequestedCount = item.RequestedCount,
                        AvailableCount = item.AvailableCount,
                        AchievedCount  = item.AchievedCount,
                        TotalCount     = item.TotalCount,
                        PassCount      = item.PassCount,
                        FailCount      = item.FailCount,
                        Status         = item.Status
                    })
                    .ToList(),
                ReviewRows = summary.ReviewRows.Select(CloneReviewRow).ToList(),
                Warning    = summary.Warning,
                Error      = summary.Error
            };
        }

        private static Rule12ValidationRowRecord CloneReviewRow(Rule12ValidationRowRecord row)
        {
            return new Rule12ValidationRowRecord
            {
                ValidationNumber = row.ValidationNumber,
                ControlType = row.ControlType,
                ControlLabel = row.ControlLabel,
                ValidationResult = row.ValidationResult,
                ValidationExplanation = row.ValidationExplanation,
                DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static Rule12ValidationSummary CreateBrowserPreview(Rule12ValidationSummary summary)
        {
            var controlGroups = summary.ReviewRows
                .GroupBy(row => row.ControlType, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => GetControlSort(group.Key))
                .ToList();

            var controlCount = Math.Max(controlGroups.Count, 1);
            var baseLimit = Math.Max(BrowserPreviewRowLimit / controlCount, 1);
            var remainder = Math.Max(BrowserPreviewRowLimit - (baseLimit * controlGroups.Count), 0);
            var previewRows = controlGroups
                .SelectMany((group, index) => TakePreviewRowsForControl(
                    group.OrderBy(row => row.ValidationNumber).ToList(),
                    baseLimit + (index < remainder ? 1 : 0)))
                .Take(BrowserPreviewRowLimit)
                .ToList();

            return new Rule12ValidationSummary
            {
                Success          = summary.Success,
                CregRecordCount  = summary.CregRecordCount,
                QualRecordCount  = summary.QualRecordCount,
                CresActiveCount  = summary.CresActiveCount,
                TotalRequested   = summary.TotalRequested,
                TotalValidated   = summary.TotalValidated,
                DisplayedCount   = previewRows.Count,
                IsPreviewOnly    = summary.TotalValidated > previewRows.Count,
                PreviewLimit     = summary.TotalValidated > previewRows.Count ? previewRows.Count : 0,
                PassCount        = summary.PassCount,
                FailCount        = summary.FailCount,
                ExceptionRate    = summary.ExceptionRate,
                Status           = summary.Status,
                Timestamp        = summary.Timestamp,
                Database         = summary.Database,
                CregTable        = summary.CregTable,
                QualTable        = summary.QualTable,
                CresTable        = summary.CresTable,
                CregStudentCol   = summary.CregStudentCol,
                CregQualCol      = summary.CregQualCol,
                CregCourseCol    = summary.CregCourseCol,
                QualJoinCol      = summary.QualJoinCol,
                QualDescCol      = summary.QualDescCol,
                CresCourseCol    = summary.CresCourseCol,
                CresStatusCol    = summary.CresStatusCol,
                CresStatusFilter = summary.CresStatusFilter,
                CregExtra1Col    = summary.CregExtra1Col,
                CregExtra2Col    = summary.CregExtra2Col,
                CregFilterCol    = summary.CregFilterCol,
                CregFilterValues = summary.CregFilterValues,
                CregExtra3Col    = summary.CregExtra3Col,
                CresExtra1Col    = summary.CresExtra1Col,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText     = summary.RuleModeText,
                ProcedureSteps   = summary.ProcedureSteps.ToList(),
                ClientId         = summary.ClientId,
                SavedRunId       = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule12ControlSummaryItemViewModel
                    {
                        ControlType    = item.ControlType,
                        ControlLabel   = item.ControlLabel,
                        CriteriaText   = item.CriteriaText,
                        RequestedCount = item.RequestedCount,
                        AvailableCount = item.AvailableCount,
                        AchievedCount  = item.AchievedCount,
                        TotalCount     = item.TotalCount,
                        PassCount      = item.PassCount,
                        FailCount      = item.FailCount,
                        Status         = item.Status
                    })
                    .ToList(),
                ReviewRows = previewRows,
                Warning    = summary.Warning,
                Error      = summary.Error
            };
        }

        private static List<Rule12ValidationRowRecord> TakePreviewRowsForControl(
            IReadOnlyList<Rule12ValidationRowRecord> rows,
            int limit)
        {
            if (limit <= 0 || rows.Count == 0)
                return new List<Rule12ValidationRowRecord>();

            var failRows = rows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var passRows = rows
                .Where(row => !string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var selected = new List<Rule12ValidationRowRecord>(limit);
            var failTake = failRows.Count == 0
                ? 0
                : Math.Min(failRows.Count, Math.Max(1, limit / 2));

            selected.AddRange(failRows.Take(failTake));

            var passTake = Math.Min(passRows.Count, limit - selected.Count);
            selected.AddRange(passRows.Take(passTake));

            if (selected.Count < limit)
                selected.AddRange(failRows.Skip(failTake).Take(limit - selected.Count));

            if (selected.Count < limit)
                selected.AddRange(passRows.Skip(passTake).Take(limit - selected.Count));

            return selected
                .OrderBy(row => row.ValidationNumber)
                .ToList();
        }

        private static void ApplyBrowserPreview(Rule12ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.ReviewRows = preview.ReviewRows;
        }

        private static Rule12ValidationRequest CloneValidationRequest(Rule12ValidationRequest request)
        {
            return new Rule12ValidationRequest
            {
                ClientId         = request.ClientId,
                RunId            = request.RunId,
                Server           = request.Server,
                Database         = request.Database,
                Driver           = request.Driver,
                CregTable        = request.CregTable,
                QualTable        = request.QualTable,
                CresTable        = request.CresTable,
                CregStudentCol   = request.CregStudentCol,
                CregQualCol      = request.CregQualCol,
                CregCourseCol    = request.CregCourseCol,
                QualJoinCol      = request.QualJoinCol,
                QualDescCol      = request.QualDescCol,
                CresCourseCol    = request.CresCourseCol,
                CresStatusCol    = request.CresStatusCol,
                CresStatusFilter = request.CresStatusFilter,
                CregExtra1Col    = request.CregExtra1Col,
                CregExtra2Col    = request.CregExtra2Col,
                CregFilterCol    = request.CregFilterCol,
                CregFilterValues = request.CregFilterValues,
                CregExtra3Col    = request.CregExtra3Col,
                CresExtra1Col    = request.CresExtra1Col
            };
        }

        private static bool RequestsMatchForPendingSave(Rule12ValidationRequest current, Rule12ValidationRequest pending)
        {
            return current.ClientId == pending.ClientId &&
                   string.Equals(current.Server?.Trim(), pending.Server?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Database?.Trim(), pending.Database?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Driver?.Trim(), pending.Driver?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CregTable?.Trim(), pending.CregTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.QualTable?.Trim(), pending.QualTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CresTable?.Trim(), pending.CresTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CresStatusFilter?.Trim(), pending.CresStatusFilter?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static int GetControlSort(string? controlType) => controlType switch
        {
            "Control_1" => 1,
            _ => 99
        };

        private static List<string> BuildProcedureSteps(string cregTable, string qualTable, string cresTable, string cresStatusCol, string cresStatusFilter) =>
            new()
            {
                $"From {cregTable}, select all students who have an active registration in {cresTable} (where {cresStatusCol} = '{cresStatusFilter}').",
                $"Join {cregTable} to {cresTable} on the course code column (_030) to identify active students.",
                $"For each active student, retrieve the qualification code (_001) from {cregTable}.",
                $"Test whether the qualification code exists in {qualTable}._001.",
                $"Mark rows PASS when the qualification exists in {qualTable} and FAIL when it does not.",
                "Return the full active student population with qualification validation results."
            };

        private async Task EnsureColumnsExistAsync(Rule12ValidationRequest request)
        {
            var qualTable      = !string.IsNullOrWhiteSpace(request.QualTable) ? request.QualTable : "dbo_QUAL";
            var cresTable      = !string.IsNullOrWhiteSpace(request.CresTable) ? request.CresTable : "dbo_CRES";
            var cregStudentCol = !string.IsNullOrWhiteSpace(request.CregStudentCol) ? request.CregStudentCol : "_007";
            var cregQualCol    = !string.IsNullOrWhiteSpace(request.CregQualCol) ? request.CregQualCol : "_001";
            var cregCourseCol  = !string.IsNullOrWhiteSpace(request.CregCourseCol) ? request.CregCourseCol : "_030";
            var qualJoinCol    = !string.IsNullOrWhiteSpace(request.QualJoinCol) ? request.QualJoinCol : "_001";
            var qualDescCol    = !string.IsNullOrWhiteSpace(request.QualDescCol) ? request.QualDescCol : "_003";
            var cresCourseCol  = !string.IsNullOrWhiteSpace(request.CresCourseCol) ? request.CresCourseCol : "_030";
            var cresStatusCol  = !string.IsNullOrWhiteSpace(request.CresStatusCol) ? request.CresStatusCol : "_031";

            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            var allCols = await GetTableColumnsMultiAsync(conn, request.CregTable, qualTable, cresTable);
            var cregColumns = allCols.GetValueOrDefault(request.CregTable) ?? new List<string>();
            var qualColumns = allCols.GetValueOrDefault(qualTable) ?? new List<string>();
            var cresColumns = allCols.GetValueOrDefault(cresTable) ?? new List<string>();

            var requiredCregCols = new List<string> { cregStudentCol, cregQualCol, cregCourseCol };
            if (!string.IsNullOrWhiteSpace(request.CregExtra1Col)) requiredCregCols.Add(Sanitise(request.CregExtra1Col));
            if (!string.IsNullOrWhiteSpace(request.CregExtra2Col)) requiredCregCols.Add(Sanitise(request.CregExtra2Col));
            if (!string.IsNullOrWhiteSpace(request.CregFilterCol)) requiredCregCols.Add(Sanitise(request.CregFilterCol));
            if (!string.IsNullOrWhiteSpace(request.CregExtra3Col)) requiredCregCols.Add(Sanitise(request.CregExtra3Col));
            EnsureHasColumns(request.CregTable, cregColumns, requiredCregCols.ToArray());
            EnsureHasColumns(qualTable, qualColumns, qualJoinCol, qualDescCol);
            var requiredCresCols = new List<string> { cresCourseCol, cresStatusCol };
            if (!string.IsNullOrWhiteSpace(request.CresExtra1Col)) requiredCresCols.Add(Sanitise(request.CresExtra1Col));
            EnsureHasColumns(cresTable, cresColumns, requiredCresCols.ToArray());
        }

        private async Task EnsureColumnsExistAsync(Rule12VerifyRequest request)
        {
            var qualTable      = !string.IsNullOrWhiteSpace(request.QualTable) ? request.QualTable : "dbo_QUAL";
            var cresTable      = !string.IsNullOrWhiteSpace(request.CresTable) ? request.CresTable : "dbo_CRES";
            var cregStudentCol = !string.IsNullOrWhiteSpace(request.CregStudentCol) ? request.CregStudentCol : "_007";
            var cregQualCol    = !string.IsNullOrWhiteSpace(request.CregQualCol) ? request.CregQualCol : "_001";
            var cregCourseCol  = !string.IsNullOrWhiteSpace(request.CregCourseCol) ? request.CregCourseCol : "_030";
            var qualJoinCol    = !string.IsNullOrWhiteSpace(request.QualJoinCol) ? request.QualJoinCol : "_001";
            var qualDescCol    = !string.IsNullOrWhiteSpace(request.QualDescCol) ? request.QualDescCol : "_003";
            var cresCourseCol  = !string.IsNullOrWhiteSpace(request.CresCourseCol) ? request.CresCourseCol : "_030";
            var cresStatusCol  = !string.IsNullOrWhiteSpace(request.CresStatusCol) ? request.CresStatusCol : "_031";

            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            var allCols = await GetTableColumnsMultiAsync(conn, request.CregTable, qualTable, cresTable);
            var cregColumns = allCols.GetValueOrDefault(request.CregTable) ?? new List<string>();
            var qualColumns = allCols.GetValueOrDefault(qualTable) ?? new List<string>();
            var cresColumns = allCols.GetValueOrDefault(cresTable) ?? new List<string>();

            var requiredCregColsV = new List<string> { cregStudentCol, cregQualCol, cregCourseCol };
            if (!string.IsNullOrWhiteSpace(request.CregExtra1Col)) requiredCregColsV.Add(Sanitise(request.CregExtra1Col));
            if (!string.IsNullOrWhiteSpace(request.CregExtra2Col)) requiredCregColsV.Add(Sanitise(request.CregExtra2Col));
            if (!string.IsNullOrWhiteSpace(request.CregFilterCol)) requiredCregColsV.Add(Sanitise(request.CregFilterCol));
            if (!string.IsNullOrWhiteSpace(request.CregExtra3Col)) requiredCregColsV.Add(Sanitise(request.CregExtra3Col));
            EnsureHasColumns(request.CregTable, cregColumns, requiredCregColsV.ToArray());
            EnsureHasColumns(qualTable, qualColumns, qualJoinCol, qualDescCol);
            var requiredCresCols = new List<string> { cresCourseCol, cresStatusCol };
            if (!string.IsNullOrWhiteSpace(request.CresExtra1Col)) requiredCresCols.Add(Sanitise(request.CresExtra1Col));
            EnsureHasColumns(cresTable, cresColumns, requiredCresCols.ToArray());
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

        private static void ValidateRequest(Rule12ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.CregTable))
                throw new InvalidOperationException("CREG table is required.");
            if (string.IsNullOrWhiteSpace(request.QualTable))
                throw new InvalidOperationException("Qualification (QUAL) table is required.");
            if (string.IsNullOrWhiteSpace(request.CresTable))
                throw new InvalidOperationException("CRES table is required.");

            ValidateObjectName(request.CregTable);
            ValidateObjectName(request.QualTable);
            ValidateObjectName(request.CresTable);
        }

        private static void ValidateRequest(Rule12VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.CregTable))
                throw new InvalidOperationException("CREG table is required.");
            if (string.IsNullOrWhiteSpace(request.QualTable))
                throw new InvalidOperationException("Qualification (QUAL) table is required.");
            if (string.IsNullOrWhiteSpace(request.CresTable))
                throw new InvalidOperationException("CRES table is required.");

            ValidateObjectName(request.CregTable);
            ValidateObjectName(request.QualTable);
            ValidateObjectName(request.CresTable);
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

        private static Rule12ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                if (string.IsNullOrWhiteSpace(decoded))
                    return null;
                return JsonConvert.DeserializeObject<Rule12ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

        private static Rule12WorkspaceSummaryMetadata? DeserializeWorkspaceSummaryMetadata(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                if (string.IsNullOrWhiteSpace(decoded))
                    return null;

                using var stringReader = new StringReader(decoded);
                using var reader = new JsonTextReader(stringReader) { DateParseHandling = DateParseHandling.None };

                if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                    return null;

                var metadata = new Rule12WorkspaceSummaryMetadata();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.EndObject)
                        break;

                    if (reader.TokenType != JsonToken.PropertyName)
                        continue;

                    var propertyName = reader.Value?.ToString();
                    if (!reader.Read())
                        break;

                    switch (propertyName)
                    {
                        case nameof(Rule12ValidationSummary.QualTable):
                        case "CrseTable":
                            metadata.QualTable = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CresTable):
                            metadata.CresTable = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CregStudentCol):
                            metadata.CregStudentCol = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CregQualCol):
                            metadata.CregQualCol = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CregCourseCol):
                            metadata.CregCourseCol = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.QualJoinCol):
                            metadata.QualJoinCol = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.QualDescCol):
                            metadata.QualDescCol = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CresCourseCol):
                            metadata.CresCourseCol = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CresStatusCol):
                            metadata.CresStatusCol = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CresStatusFilter):
                            metadata.CresStatusFilter = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CregExtra1Col):
                            metadata.CregExtra1Col = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CregExtra2Col):
                            metadata.CregExtra2Col = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CregFilterCol):
                            metadata.CregFilterCol = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CregFilterValues):
                            metadata.CregFilterValues = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CregExtra3Col):
                            metadata.CregExtra3Col = reader.Value?.ToString();
                            break;
                        case nameof(Rule12ValidationSummary.CresExtra1Col):
                            metadata.CresExtra1Col = reader.Value?.ToString();
                            break;
                        default:
                            if (reader.TokenType == JsonToken.StartArray || reader.TokenType == JsonToken.StartObject)
                                reader.Skip();
                            break;
                    }
                }

                return metadata;
            }
            catch
            {
                return null;
            }
        }

        private sealed class Rule12WorkspaceSummaryMetadata
        {
            public string? QualTable       { get; set; }
            public string? CresTable       { get; set; }
            public string? CregStudentCol  { get; set; }
            public string? CregQualCol     { get; set; }
            public string? CregCourseCol   { get; set; }
            public string? QualJoinCol     { get; set; }
            public string? QualDescCol     { get; set; }
            public string? CresCourseCol   { get; set; }
            public string? CresStatusCol   { get; set; }
            public string? CresStatusFilter { get; set; }
            public string? CregExtra1Col   { get; set; }
            public string? CregExtra2Col   { get; set; }
            public string? CregFilterCol   { get; set; }
            public string? CregFilterValues { get; set; }
            public string? CregExtra3Col   { get; set; }
            public string? CresExtra1Col   { get; set; }
        }

        private static async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private static async Task UpdateStoredSummaryAsync(SqlConnection connection, int runId, Rule12ValidationSummary summary)
        {
            await using var update = connection.CreateConfiguredCommand();
            update.CommandText = @"
UPDATE dbo.ValidationRuns
SET ResultsJSON = @ResultsJSON
WHERE RunID = @RunID;";
            update.Parameters.AddWithValue("@RunID", runId);
            update.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            await update.ExecuteNonQueryAsync();
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);

        private static void EnrichRule12DisplayValues(Rule12ValidationRowRecord row)
        {
            var values = row.DisplayValues;
            var validationResult = ReadValue(values, "Validation_Result");
            var isPass   = string.Equals(validationResult, "PASS", StringComparison.OrdinalIgnoreCase);
            var creg007  = FormatRule12ColumnValue(ReadValue(values, "CREG__007"));
            var creg001  = FormatRule12ColumnValue(ReadValue(values, "CREG__001"));
            var creg030  = FormatRule12ColumnValue(ReadValue(values, "CREG__030"));
            var qual001  = FormatRule12ColumnValue(ReadValue(values, "QUAL__001"));
            var qual003  = FormatRule12ColumnValue(ReadValue(values, "QUAL__003"));
            var cres030  = FormatRule12ColumnValue(ReadValue(values, "CRES__030"));
            var cres031  = FormatRule12ColumnValue(ReadValue(values, "CRES__031"));

            const string criteriaText = "Active student: CRES._031 = 'A' | CREG._001 = QUAL._001";
            var validationExplanation = isPass
                ? $"Active student '{creg007}' qualification '{creg001}' exists in QUAL as '{qual001}'."
                : $"Active student '{creg007}' qualification '{creg001}' does not exist in QUAL.";
            var qualCriteriaMessage = $"Active student (CRES._031='{cres031}'): qualification code '{creg001}'.";
            var credLinkMessage = isPass
                ? $"Matched QUAL._001 = '{qual001}' ({qual003})."
                : "No matching QUAL._001 value was found.";
            var registrationMessage = isPass
                ? "Qualification exists in QUAL."
                : "Qualification not found in QUAL.";
            var finalResultMessage = isPass
                ? "Passed: active student qualification exists in QUAL."
                : "Failed: active student qualification does not exist in QUAL.";

            values["FINAL_RULE_TEXT"]        = criteriaText;
            values["Validation_Explanation"] = validationExplanation;
            values["CRSE_CRITERIA_MESSAGE"]  = qualCriteriaMessage;
            values["CRSE_SELECTION_MESSAGE"] = credLinkMessage;
            values["CREG_LINK_MESSAGE"]      = registrationMessage;
            values["FINAL_RESULT_MESSAGE"]   = finalResultMessage;
            row.ValidationExplanation = validationExplanation;
        }

        private static string FormatRule12ColumnValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "[blank]" : value.Trim();

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

        private static string ReadValue(IReadOnlyDictionary<string, string?> values, string key) =>
            values.TryGetValue(key, out var value) ? value ?? "" : "";

    }
}




