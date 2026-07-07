using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule19Service : IRule19Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private static readonly string[] DefaultMdTypes = ["07", "27", "28", "49", "72", "73", "08", "30", "50", "74", "75"];
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule19Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<Rule19TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule19TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = FindFirst(tables, ["dbo_STUD", "STUD"], ["stud"]),
                    AutoQualTable = FindFirst(tables, ["dbo_QUAL", "QUAL"], ["qual"]),
                    AutoPqmTable = FindFirst(tables, ["PQM"], ["pqm"]),
                    AutoCredTable = FindFirst(tables, ["dbo_CRED", "CRED"], ["cred"]),
                    AutoCrseTable = FindFirst(tables, ["dbo_CRSE", "CRSE"], ["crse"])
                };
            }
            catch (Exception ex)
            {
                return new Rule19TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule19ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                var columns = await GetTableColumnsAsync(server, database, driver, tableName);
                return new Rule19ColumnSelectionResult
                {
                    Success = true,
                    Columns = columns,
                    AutoQualCodeColumn = FindFirst(columns, ["_001"], ["qual", "code"]),
                    AutoFulfilledColumn = FindFirst(columns, ["_025"], ["fulfill"]),
                    AutoQualTypeColumn = FindFirst(columns, ["_005"], ["type"]),
                    AutoQualNameColumn = FindFirst(columns, ["_003"], ["name", "qualification_name"]),
                    AutoPqmQualNameColumn = FindFirst(columns, ["Authorised_Qualification_Name"], ["authorised", "qualification"]),
                    AutoPqmQualTypeColumn = FindFirst(columns, ["HEQF_Qual_Type"], ["heqf", "qual_type"])
                };
            }
            catch (Exception ex)
            {
                return new Rule19ColumnSelectionResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule19FilterValueResult> GetFilterValuesAsync(string server, string database, string driver, string studTable, string fulfilledColumn)
        {
            try
            {
                ValidateObjectName(studTable);
                ValidateObjectName(fulfilledColumn);

                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var safeTable = Sanitise(studTable);
                var safeColumn = Sanitise(fulfilledColumn);

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = $@"
SELECT
    ISNULL(CAST([{safeColumn}] AS nvarchar(100)), '(NULL)') AS FilterValue,
    COUNT(*) AS RecordCount
FROM [{safeTable}]
WHERE [{safeColumn}] IS NOT NULL
  AND LTRIM(RTRIM(CAST([{safeColumn}] AS nvarchar(100)))) <> ''
GROUP BY [{safeColumn}]
ORDER BY COUNT(*) DESC, FilterValue ASC;";

                await using var reader = await cmd.ExecuteReaderAsync();
                var options = new List<Rule19FilterValueOption>();
                while (await reader.ReadAsync())
                {
                    var value = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    var count = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                    options.Add(new Rule19FilterValueOption
                    {
                        Value = value,
                        Count = count,
                        Label = $"{value} ({count:N0} records)"
                    });
                }

                return new Rule19FilterValueResult
                {
                    Success = true,
                    Options = options,
                    DefaultValue = options.FirstOrDefault()?.Value
                };
            }
            catch (Exception ex)
            {
                return new Rule19FilterValueResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule19VerifyResult> VerifyTablesAsync(Rule19VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);

                var summary = await AnalyseAsync(ToValidationRequest(request));
                return new Rule19VerifyResult
                {
                    Success = true,
                    StudRecordCount = summary.StudRecordCount,
                    QualRecordCount = summary.QualRecordCount,
                    CredRecordCount = summary.CredRecordCount,
                    CrseRecordCount = summary.CrseRecordCount,
                    EligiblePopulationCount = summary.MatchingCount,
                    PreviewRows = CreateBrowserPreview(summary).MatchingRows
                };
            }
            catch (Exception ex)
            {
                return new Rule19VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule19ValidationSummary> RunValidationAsync(Rule19ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);

                var summary = await AnalyseAsync(request);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName);
                    }
                    catch (Exception ex)
                    {
                        summary.Warning = $"Validation completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule19ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule19WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.StudColumn, '') AS QualCodeColumn,
    ISNULL(vr.DeceasedColumn, '') AS FulfilledColumn,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 19
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var deserializedSummary = DeserializeSummary(reader.IsDBNull(11) ? null : reader.GetString(11));
            if (includeSummary && deserializedSummary != null)
                ApplyBrowserPreview(deserializedSummary);
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule19WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StudTable = reader.IsDBNull(4) ? "dbo_STUD" : reader.GetString(4),
                QualTable = reader.IsDBNull(5) ? "dbo_QUAL" : reader.GetString(5),
                QualCodeColumn = reader.IsDBNull(6) ? "_001" : reader.GetString(6),
                FulfilledColumn = reader.IsDBNull(7) ? "_025" : reader.GetString(7),
                CurrentStatus = reader.IsDBNull(8) ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary = summary
            };

            if (deserializedSummary != null)
            {
                workspace.FulfilledValue = deserializedSummary.FulfilledValue;
                workspace.QualTypeColumn = deserializedSummary.QualTypeColumn;
                workspace.MdTypesText = string.IsNullOrWhiteSpace(deserializedSummary.MdTypesText)
                    ? string.Join(", ", deserializedSummary.MdTypes)
                    : deserializedSummary.MdTypesText;
                workspace.CurrentStatus = deserializedSummary.Status;
                workspace.QualNameColumn = deserializedSummary.QualNameColumn;
                workspace.PqmTable = deserializedSummary.PqmTable;
                workspace.PqmQualNameColumn = deserializedSummary.PqmQualNameColumn;
                workspace.PqmQualTypeColumn = deserializedSummary.PqmQualTypeColumn;
                workspace.CredTable = deserializedSummary.CredTable;
                workspace.CredJoinCol = deserializedSummary.CredJoinCol;
                workspace.CredCourseCol = deserializedSummary.CredCourseCol;
                workspace.CrseTable = deserializedSummary.CrseTable;
                workspace.CrseCourseCol = deserializedSummary.CrseCourseCol;
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

        public async Task<Rule19RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 19;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var sourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6)) ?? new Rule19ValidationSummary();
            if (includeFullResults)
            {
                summary = await ExpandSavedSummaryIfNeededAsync(summary, sourceServer);
                summary.DisplayedCount = summary.MatchingRows.Count;
                summary.IsPreviewOnly = false;
                summary.PreviewLimit = 0;
            }
            else
            {
                ApplyBrowserPreview(summary);
            }

            var viewModel = new Rule19RunReviewViewModel
            {
                RunId = reader.GetInt32(0),
                ClientId = reader.GetInt32(1),
                IsCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer = sourceServer,
                Summary = summary
            };

            await reader.CloseAsync();

            viewModel.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, viewModel.ClientId, currentUserId.Value) ?? ""
                : "";
            viewModel.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            viewModel.HasDataAnalystSignoff = viewModel.Signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            if (viewModel.Summary.SavedRunId.GetValueOrDefault() <= 0)
                viewModel.Summary.SavedRunId = viewModel.RunId;

            return viewModel;
        }

        public async Task<Rule19WorkspaceSaveResult> SaveWorkspaceAsync(Rule19ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule19WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run Rule 19 first so the workspace can be saved."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule19WorkspaceSaveResult
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
    StudColumn = @QualCodeColumn,
    DeceasedColumn = @FulfilledColumn,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID
  AND ClientID = @ClientID;";
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID", request.ClientId);
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@StudTable", request.StudTable);
                command.Parameters.AddWithValue("@QualTable", request.QualTable);
                command.Parameters.AddWithValue("@QualCodeColumn", request.QualCodeColumn);
                command.Parameters.AddWithValue("@FulfilledColumn", request.FulfilledColumn);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule19|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.StudTable}|{request.QualTable}|{request.QualCodeColumn}|{request.FulfilledColumn}|{request.FulfilledValue}|{request.QualTypeColumn}|{request.MdTypesText}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule19WorkspaceSaveResult
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
                return new Rule19WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule19WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule19WorkspaceSaveResult
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
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule19|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule19WorkspaceSaveResult
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
                return new Rule19WorkspaceSaveResult
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
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(engagementRole))
                throw new InvalidOperationException("Only assigned data analysts, managers, and directors can sign off a validation run.");

            if (!string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
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
            command.Parameters.AddWithValue("@SignoffRole", engagementRole!);
            command.Parameters.AddWithValue("@Comment", string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            await command.ExecuteNonQueryAsync();

            await UpdateRunStatusFromSignoffsAsync(connection, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule19ValidationRequest request)
        {
            ValidateRequest(request);

            var safeStudTable = Sanitise(request.StudTable);
            var safeQualTable = Sanitise(request.QualTable);
            var safeQualCodeColumn = Sanitise(request.QualCodeColumn);
            var safeFulfilledColumn = Sanitise(request.FulfilledColumn);
            var safeQualTypeColumn = Sanitise(request.QualTypeColumn);
            var safeQualNameColumn = Sanitise(string.IsNullOrWhiteSpace(request.QualNameColumn) ? "_003" : request.QualNameColumn);
            var fulfilledValue = EscapeSqlString(request.FulfilledValue);
            var mdTypes = ParseMdTypes(request.MdTypesText);
            var mdTypeSql = string.Join(", ", mdTypes.Select(value => $"'{EscapeSqlString(value)}'"));
            var hasPqm = !string.IsNullOrWhiteSpace(request.PqmTable) && !string.IsNullOrWhiteSpace(request.PqmQualNameColumn) && !string.IsNullOrWhiteSpace(request.PqmQualTypeColumn);
            var safePqmTable = hasPqm ? Sanitise(request.PqmTable) : "";
            var safePqmName = hasPqm ? Sanitise(request.PqmQualNameColumn) : "";
            var safePqmType = hasPqm ? Sanitise(request.PqmQualTypeColumn) : "";
            var hasCredCrse = !string.IsNullOrWhiteSpace(request.CredTable) && !string.IsNullOrWhiteSpace(request.CrseTable);
            var safeCredTable = hasCredCrse ? Sanitise(request.CredTable) : "";
            var safeCredJoinCol = hasCredCrse ? Sanitise(string.IsNullOrWhiteSpace(request.CredJoinCol) ? "_001" : request.CredJoinCol) : "_001";
            var safeCredCourseCol = hasCredCrse ? Sanitise(string.IsNullOrWhiteSpace(request.CredCourseCol) ? "_030" : request.CredCourseCol) : "_030";
            var safeCrseTable = hasCredCrse ? Sanitise(request.CrseTable) : "";
            var safeCrseCourseCol = hasCredCrse ? Sanitise(string.IsNullOrWhiteSpace(request.CrseCourseCol) ? "_030" : request.CrseCourseCol) : "_030";

            var credCrseJoinSql = hasCredCrse
                ? $@"
INNER JOIN [{safeCredTable}] CRED ON STUD.[{safeQualCodeColumn}] = CRED.[{safeCredJoinCol}]
INNER JOIN [{safeCrseTable}] CRSE ON CRED.[{safeCredCourseCol}] = CRSE.[{safeCrseCourseCol}]"
                : "";

            var pqmJoinSql = hasPqm
                ? $@"
LEFT JOIN [{safePqmTable}] PQM
    ON UPPER(LTRIM(RTRIM(CAST(QUAL.[{safeQualNameColumn}] AS nvarchar(500))))) = UPPER(LTRIM(RTRIM(CAST(PQM.[{safePqmName}] AS nvarchar(500)))))
    AND UPPER(LTRIM(RTRIM(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255))))) = UPPER(LTRIM(RTRIM(CAST(PQM.[{safePqmType}] AS nvarchar(255)))))"
                : "";

            var credCrseSelectSql = hasCredCrse
                ? $@"
    ISNULL(CAST(CRED.[{safeCredJoinCol}] AS nvarchar(255)), '') AS CRED_Qualification_Code,
    ISNULL(CAST(CRED.[{safeCredCourseCol}] AS nvarchar(255)), '') AS CRED_Course_Code,
    ISNULL(CAST(CRSE.[{safeCrseCourseCol}] AS nvarchar(255)), '') AS CRSE_Course_Code,
    ISNULL(CAST(CRSE.[_058] AS nvarchar(500)), '') AS Course_Name,
    ISNULL(CAST(CRSE.[_091] AS nvarchar(255)), '') AS Foundation_Course_Indicator,"
                : "";

            var pqmSelectSql = hasPqm
                ? $@"
    ISNULL(CAST(PQM.[{safePqmName}] AS nvarchar(500)), '') AS PQM_Qualification_Name,
    ISNULL(CAST(PQM.[{safePqmType}] AS nvarchar(255)), '') AS PQM_Qualification_Type,"
                : "";

            var credCrseLinkCheckSql = hasCredCrse
                ? $@"
    CASE WHEN ISNULL(CAST(STUD.[{safeQualCodeColumn}] AS nvarchar(255)),'') = ISNULL(CAST(CRED.[{safeCredJoinCol}] AS nvarchar(255)),'') THEN 'MATCH' ELSE 'NO MATCH' END AS STUD_CRED_Link_Check,
    CASE WHEN ISNULL(CAST(CRED.[{safeCredCourseCol}] AS nvarchar(255)),'') = ISNULL(CAST(CRSE.[{safeCrseCourseCol}] AS nvarchar(255)),'') THEN 'MATCH' ELSE 'NO MATCH' END AS CRED_CRSE_Link_Check,"
                : "";

            var pqmValidationSql = hasPqm
                ? $"CASE WHEN PQM.[{safePqmName}] IS NOT NULL AND LTRIM(RTRIM(CAST(PQM.[{safePqmName}] AS nvarchar(500)))) <> '' THEN 'PASS' ELSE 'FAIL' END AS PQM_Validation_Result"
                : "'PASS' AS PQM_Validation_Result";

            var countJoin = hasCredCrse
                ? $@"
INNER JOIN [{safeCredTable}] CRED ON STUD.[{safeQualCodeColumn}] = CRED.[{safeCredJoinCol}]
INNER JOIN [{safeCrseTable}] CRSE ON CRED.[{safeCredCourseCol}] = CRSE.[{safeCrseCourseCol}]"
                : "";

            var sql = $@"-- ============================================================================
-- HEMIS RULE 19: MASTERS AND PhD STUDENT POPULATION VALIDATION
-- ============================================================================
-- Linkage: [{safeStudTable}].[{safeQualCodeColumn}] = [{safeQualTable}].[{safeQualCodeColumn}]
{(hasCredCrse ? $"-- CRED  : [{safeStudTable}].[{safeQualCodeColumn}] = [{safeCredTable}].[{safeCredJoinCol}]" : "")}
{(hasCredCrse ? $"-- CRSE  : [{safeCredTable}].[{safeCredCourseCol}] = [{safeCrseTable}].[{safeCrseCourseCol}]" : "")}
-- Filter : [{safeStudTable}].[{safeFulfilledColumn}] = '{fulfilledValue}'
-- Types  : [{safeQualTable}].[{safeQualTypeColumn}] IN ({mdTypeSql})
-- Logic  : 100% population testing — returns all filtered rows with link checks and PQM validation
-- ============================================================================

-- STEP 1: POPULATION COUNTS
SELECT
    (SELECT COUNT(*) FROM [{safeStudTable}]) AS Stud_Record_Count,
    (SELECT COUNT(*) FROM [{safeQualTable}]) AS Qual_Record_Count,{(hasCredCrse ? $@"
    (SELECT COUNT(*) FROM [{safeCredTable}]) AS Cred_Record_Count,
    (SELECT COUNT(*) FROM [{safeCrseTable}]) AS Crse_Record_Count," : "")}
    (SELECT COUNT(*)
     FROM [{safeStudTable}] STUD
     INNER JOIN [{safeQualTable}] QUAL ON STUD.[{safeQualCodeColumn}] = QUAL.[{safeQualCodeColumn}]{countJoin}
     WHERE ISNULL(CAST(STUD.[{safeFulfilledColumn}] AS nvarchar(255)), '') = '{fulfilledValue}'
       AND ISNULL(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255)), '') IN ({mdTypeSql})
    ) AS Eligible_Population_Count;

-- STEP 2: TYPE BREAKDOWN
SELECT
    ISNULL(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255)), '') AS Qualification_Type,
    COUNT(*) AS Record_Count
FROM [{safeStudTable}] STUD
INNER JOIN [{safeQualTable}] QUAL ON STUD.[{safeQualCodeColumn}] = QUAL.[{safeQualCodeColumn}]{countJoin}
WHERE ISNULL(CAST(STUD.[{safeFulfilledColumn}] AS nvarchar(255)), '') = '{fulfilledValue}'
  AND ISNULL(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255)), '') IN ({mdTypeSql})
GROUP BY QUAL.[{safeQualTypeColumn}]
ORDER BY Record_Count DESC, Qualification_Type ASC;

-- STEP 3: FULL RULE 19 POPULATION WITH LINK CHECKS AND PQM VALIDATION
SELECT
    'Control_1' AS Control_Type,
    'Masters/PhD Students' AS Control_Description,
    ROW_NUMBER() OVER (ORDER BY STUD.[{safeQualCodeColumn}] ASC) AS Sample_Number,
    ISNULL(CAST(STUD.[{safeQualCodeColumn}] AS nvarchar(255)), '') AS Student_Qualification_Code,
    ISNULL(CAST(STUD.[_007] AS nvarchar(255)), '') AS Student_Number,
    ISNULL(CAST(STUD.[_008] AS nvarchar(255)), '') AS South_African_ID_Number,
    ISNULL(CAST(STUD.[_010] AS nvarchar(255)), '') AS Entrance_Category,
    ISNULL(CAST(STUD.[_011] AS nvarchar(255)), '') AS Date_Of_Birth,
    ISNULL(CAST(STUD.[_012] AS nvarchar(255)), '') AS Gender,
    ISNULL(CAST(STUD.[_013] AS nvarchar(255)), '') AS Race,
    ISNULL(CAST(STUD.[_014] AS nvarchar(255)), '') AS Nationality,
    ISNULL(CAST(STUD.[_019] AS nvarchar(255)), '') AS NSFAS_Status,
    ISNULL(CAST(STUD.[_021] AS nvarchar(255)), '') AS Previous_Years_Activity,
    ISNULL(CAST(STUD.[_024] AS nvarchar(255)), '') AS Attendance_Mode,
    ISNULL(CAST(STUD.[{safeFulfilledColumn}] AS nvarchar(255)), '') AS Qualification_Fulfilled_Indicator,
    ISNULL(CAST(STUD.[_066] AS nvarchar(500)), '') AS Student_Last_Name,
    ISNULL(CAST(STUD.[_067] AS nvarchar(500)), '') AS Student_First_Name,
    ISNULL(CAST(STUD.[_068] AS nvarchar(500)), '') AS Student_Middle_Name,
    ISNULL(CAST(STUD.[_073] AS nvarchar(255)), '') AS Percentage_Research_Curriculum,
    ISNULL(CAST(QUAL.[{safeQualCodeColumn}] AS nvarchar(255)), '') AS QUAL_Qualification_Code,
    ISNULL(CAST(QUAL.[{safeQualNameColumn}] AS nvarchar(500)), '') AS Qualification_Name,
    ISNULL(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255)), '') AS Qualification_Type,{credCrseSelectSql}{pqmSelectSql}
    CASE WHEN ISNULL(CAST(STUD.[{safeQualCodeColumn}] AS nvarchar(255)),'') = ISNULL(CAST(QUAL.[{safeQualCodeColumn}] AS nvarchar(255)),'') THEN 'MATCH' ELSE 'NO MATCH' END AS STUD_QUAL_Link_Check,{credCrseLinkCheckSql}
    CASE WHEN ISNULL(CAST(STUD.[{safeFulfilledColumn}] AS nvarchar(255)),'') = '{fulfilledValue}'
         AND ISNULL(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255)),'') IN ({mdTypeSql})
         THEN 'PASS' ELSE 'FAIL' END AS Rule19_Filter_Check,
    {pqmValidationSql}
FROM [{safeStudTable}] STUD
INNER JOIN [{safeQualTable}] QUAL ON STUD.[{safeQualCodeColumn}] = QUAL.[{safeQualCodeColumn}]{credCrseJoinSql}{pqmJoinSql}
WHERE ISNULL(CAST(STUD.[{safeFulfilledColumn}] AS nvarchar(255)), '') = '{fulfilledValue}'
  AND ISNULL(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255)), '') IN ({mdTypeSql})
ORDER BY STUD.[{safeQualCodeColumn}] ASC;";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule19ValidationSummary> AnalyseAsync(Rule19ValidationRequest request)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var safeStudTable = Sanitise(request.StudTable);
            var safeQualTable = Sanitise(request.QualTable);
            var safeQualCodeColumn = Sanitise(request.QualCodeColumn);
            var safeFulfilledColumn = Sanitise(request.FulfilledColumn);
            var safeQualTypeColumn = Sanitise(request.QualTypeColumn);
            var mdTypes = ParseMdTypes(request.MdTypesText);
            var configuredMdTypes = mdTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var hasCredCrse = !string.IsNullOrWhiteSpace(request.CredTable) &&
                              !string.IsNullOrWhiteSpace(request.CrseTable);
            var safeCredTable = hasCredCrse ? Sanitise(request.CredTable) : "";
            var safeCredJoinCol = hasCredCrse ? Sanitise(string.IsNullOrWhiteSpace(request.CredJoinCol) ? "_001" : request.CredJoinCol) : "_001";
            var safeCredCourseCol = hasCredCrse ? Sanitise(string.IsNullOrWhiteSpace(request.CredCourseCol) ? "_030" : request.CredCourseCol) : "_030";
            var safeCrseTable = hasCredCrse ? Sanitise(request.CrseTable) : "";
            var safeCrseCourseCol = hasCredCrse ? Sanitise(string.IsNullOrWhiteSpace(request.CrseCourseCol) ? "_030" : request.CrseCourseCol) : "_030";

            var studRecordCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{safeStudTable}];");
            var qualRecordCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{safeQualTable}];");
            var credRecordCount = hasCredCrse ? await CountAsync(conn, $"SELECT COUNT(*) FROM [{safeCredTable}];") : 0;
            var crseRecordCount = hasCredCrse ? await CountAsync(conn, $"SELECT COUNT(*) FROM [{safeCrseTable}];") : 0;

            await using var countCommand = conn.CreateConfiguredCommand();
            var filterPredicate = BuildEligiblePredicate(
                countCommand, safeQualCodeColumn, safeFulfilledColumn, safeQualTypeColumn,
                request.FulfilledValue, configuredMdTypes);

            var credCrseJoin = hasCredCrse
                ? $@"
INNER JOIN [{safeCredTable}] CRED ON STUD.[{safeQualCodeColumn}] = CRED.[{safeCredJoinCol}]
INNER JOIN [{safeCrseTable}] CRSE ON CRED.[{safeCredCourseCol}] = CRSE.[{safeCrseCourseCol}]"
                : "";
            countCommand.CommandText = $@"
SELECT COUNT(*)
FROM [{safeStudTable}] STUD
INNER JOIN [{safeQualTable}] QUAL ON STUD.[{safeQualCodeColumn}] = QUAL.[{safeQualCodeColumn}]{credCrseJoin}
WHERE {filterPredicate};";
            var eligibleCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            var breakdown = new List<Rule19BreakdownItemViewModel>();
            await using (var breakdownCommand = conn.CreateConfiguredCommand())
            {
                var breakdownPredicate = BuildEligiblePredicate(
                    breakdownCommand, safeQualCodeColumn, safeFulfilledColumn, safeQualTypeColumn,
                    request.FulfilledValue, configuredMdTypes);
                breakdownCommand.CommandText = $@"
SELECT
    ISNULL(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255)), '') AS QualificationType,
    COUNT(*) AS RecordCount
FROM [{safeStudTable}] STUD
INNER JOIN [{safeQualTable}] QUAL ON STUD.[{safeQualCodeColumn}] = QUAL.[{safeQualCodeColumn}]{credCrseJoin}
WHERE {breakdownPredicate}
GROUP BY QUAL.[{safeQualTypeColumn}]
ORDER BY COUNT(*) DESC, QualificationType ASC;";

                await using var breakdownReader = await breakdownCommand.ExecuteReaderAsync();
                while (await breakdownReader.ReadAsync())
                {
                    breakdown.Add(new Rule19BreakdownItemViewModel
                    {
                        Value = breakdownReader.IsDBNull(0) ? "(blank)" : breakdownReader.GetString(0),
                        Count = breakdownReader.IsDBNull(1) ? 0 : Convert.ToInt32(breakdownReader.GetValue(1))
                    });
                }
            }

            var rows = eligibleCount > 0
                ? await LoadPopulationRowsAsync(conn, request, configuredMdTypes,
                    safeStudTable, safeQualTable, safeQualCodeColumn, safeFulfilledColumn, safeQualTypeColumn,
                    hasCredCrse, safeCredTable, safeCredJoinCol, safeCredCourseCol, safeCrseTable, safeCrseCourseCol)
                : new List<Rule19ValidationRowRecord>();

            var hasPqm = !string.IsNullOrWhiteSpace(request.PqmTable) &&
                         !string.IsNullOrWhiteSpace(request.PqmQualNameColumn) &&
                         !string.IsNullOrWhiteSpace(request.PqmQualTypeColumn);
            var passCount = hasPqm ? rows.Count(r => r.ValidationResult == "PASS") : eligibleCount;
            var failCount = hasPqm ? rows.Count(r => r.ValidationResult == "FAIL") : 0;
            var status = hasPqm
                ? (passCount == eligibleCount ? "PASS" : (eligibleCount > 0 ? "FAIL" : "NO MATCHING DATA"))
                : (eligibleCount > 0 ? "COMPLETE" : "NO MATCHING DATA");
            var exceptionRate = hasPqm
                ? (eligibleCount > 0 ? Math.Round((decimal)failCount / eligibleCount * 100, 2) : 0m)
                : 0m;

            var linkageText = hasCredCrse
                ? $"{request.StudTable}.{request.QualCodeColumn} -> {request.QualTable}.{request.QualCodeColumn} | {request.StudTable}.{request.QualCodeColumn} -> {request.CredTable}.{request.CredJoinCol} | {request.CredTable}.{request.CredCourseCol} -> {request.CrseTable}.{request.CrseCourseCol}"
                : $"{request.StudTable}.{request.QualCodeColumn} -> {request.QualTable}.{request.QualCodeColumn}";

            return new Rule19ValidationSummary
            {
                Success = true,
                StudRecordCount = studRecordCount,
                QualRecordCount = qualRecordCount,
                CredRecordCount = credRecordCount,
                CrseRecordCount = crseRecordCount,
                TotalValidated = eligibleCount,
                MatchingCount = eligibleCount,
                DisplayedCount = rows.Count,
                PassCount = passCount,
                FailCount = failCount,
                ExceptionRate = exceptionRate,
                Status = status,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                StudTable = request.StudTable,
                QualTable = request.QualTable,
                QualCodeColumn = request.QualCodeColumn,
                FulfilledColumn = request.FulfilledColumn,
                FulfilledValue = request.FulfilledValue,
                QualTypeColumn = request.QualTypeColumn,
                MdTypesText = string.Join(", ", mdTypes),
                MdTypes = mdTypes,
                QualNameColumn = request.QualNameColumn,
                PqmTable = request.PqmTable,
                PqmQualNameColumn = request.PqmQualNameColumn,
                PqmQualTypeColumn = request.PqmQualTypeColumn,
                CredTable = request.CredTable,
                CredJoinCol = request.CredJoinCol,
                CredCourseCol = request.CredCourseCol,
                CrseTable = request.CrseTable,
                CrseCourseCol = request.CrseCourseCol,
                TableLinkageText = linkageText,
                ProcedureSteps = BuildProcedureSteps(request),
                ShowAllRecords = true,
                ClientId = request.ClientId,
                Breakdown = breakdown,
                MatchingRows = rows
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule19ValidationRequest request, Rule19ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 19);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 19);
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
    @ClientID, @UserID, 19, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @QualTable, @QualCodeColumn, @FulfilledColumn,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Masters and PhD Student Population Validation");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@QualTable", request.QualTable);
            command.Parameters.AddWithValue("@QualCodeColumn", request.QualCodeColumn);
            command.Parameters.AddWithValue("@FulfilledColumn", request.FulfilledColumn);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode("[]"));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)(userName ?? userEmail) ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET RecordHash = @RecordHash
WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"RunValidation|Rule19|{runId}|{request.ClientId}|{request.Server}|{request.Database}|{request.StudTable}|{request.QualTable}|{request.QualCodeColumn}|{request.FulfilledColumn}|{request.FulfilledValue}|{request.QualTypeColumn}|{request.MdTypesText}|{request.PqmTable}|{request.PqmQualNameColumn}|{request.PqmQualTypeColumn}|{summary.TotalValidated}|{summary.PassCount}|{summary.FailCount}|{DateTime.UtcNow:o}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private static Rule19ValidationRequest ToValidationRequest(Rule19VerifyRequest request) =>
            new()
            {
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                StudTable = request.StudTable,
                QualTable = request.QualTable,
                QualCodeColumn = request.QualCodeColumn,
                FulfilledColumn = request.FulfilledColumn,
                FulfilledValue = request.FulfilledValue,
                QualTypeColumn = request.QualTypeColumn,
                MdTypesText = request.MdTypesText,
                QualNameColumn = request.QualNameColumn,
                PqmTable = request.PqmTable,
                PqmQualNameColumn = request.PqmQualNameColumn,
                PqmQualTypeColumn = request.PqmQualTypeColumn,
                CredTable = request.CredTable,
                CredJoinCol = request.CredJoinCol,
                CredCourseCol = request.CredCourseCol,
                CrseTable = request.CrseTable,
                CrseCourseCol = request.CrseCourseCol,
                ShowAllRecords = true
            };

        private static List<string> BuildProcedureSteps(Rule19ValidationRequest request)
        {
            var steps = new List<string>
            {
                $"Load 100% of {request.StudTable} and {request.QualTable}.",
                $"Link {request.StudTable}.{request.QualCodeColumn} to {request.QualTable}.{request.QualCodeColumn}."
            };
            if (!string.IsNullOrWhiteSpace(request.CredTable) && !string.IsNullOrWhiteSpace(request.CrseTable))
            {
                steps.Add($"Link {request.StudTable}.{request.QualCodeColumn} to {request.CredTable}.{request.CredJoinCol}.");
                steps.Add($"Link {request.CredTable}.{request.CredCourseCol} to {request.CrseTable}.{request.CrseCourseCol}.");
            }
            steps.Add($"Keep rows where {request.StudTable}.{request.FulfilledColumn} = '{request.FulfilledValue}'.");
            steps.Add($"Keep rows where {request.QualTable}.{request.QualTypeColumn} is in the configured Masters/Doctoral list.");
            steps.Add("Return the full linked population with link checks and filter checks.");
            steps.Add(string.IsNullOrWhiteSpace(request.PqmTable)
                ? "No PQM table configured — PQM_Validation_Result defaults to PASS."
                : $"LEFT JOIN PQM: QUAL.{request.QualNameColumn} = PQM.{request.PqmQualNameColumn} AND QUAL.{request.QualTypeColumn} = PQM.{request.PqmQualTypeColumn} (PASS if matched).");
            return steps;
        }

        private static List<string> ParseMdTypes(string? text)
        {
            var values = (text ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return values.Count > 0 ? values : DefaultMdTypes.ToList();
        }

        private static string BuildEligiblePredicate(
            SqlCommand command,
            string safeQualCodeColumn,
            string safeFulfilledColumn,
            string safeQualTypeColumn,
            string fulfilledValue,
            IReadOnlyList<string> configuredMdTypes)
        {
            var fulfilledParam = "@FulfilledValue";
            command.Parameters.AddWithValue(fulfilledParam, fulfilledValue);

            var qualifyingTypePredicates = new List<string>();
            for (var i = 0; i < configuredMdTypes.Count; i++)
            {
                var paramName = $"@MdType{i}";
                command.Parameters.AddWithValue(paramName, configuredMdTypes[i]);
                qualifyingTypePredicates.Add($"ISNULL(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255)), '') = {paramName}");
            }

            var qualTypePredicate = qualifyingTypePredicates.Count > 0
                ? $"({string.Join(" OR ", qualifyingTypePredicates)})"
                : "1 = 0";

            return $@"ISNULL(CAST(STUD.[{safeFulfilledColumn}] AS nvarchar(255)), '') = {fulfilledParam}
  AND {qualTypePredicate}";
        }

        private async Task<List<Rule19ValidationRowRecord>> LoadPopulationRowsAsync(
            SqlConnection conn,
            Rule19ValidationRequest request,
            IReadOnlyList<string> configuredMdTypes,
            string safeStudTable, string safeQualTable, string safeQualCodeColumn,
            string safeFulfilledColumn, string safeQualTypeColumn,
            bool hasCredCrse,
            string safeCredTable, string safeCredJoinCol, string safeCredCourseCol,
            string safeCrseTable, string safeCrseCourseCol)
        {
            var safeQualNameColumn = Sanitise(string.IsNullOrWhiteSpace(request.QualNameColumn) ? "_003" : request.QualNameColumn);
            var hasPqm = !string.IsNullOrWhiteSpace(request.PqmTable) &&
                         !string.IsNullOrWhiteSpace(request.PqmQualNameColumn) &&
                         !string.IsNullOrWhiteSpace(request.PqmQualTypeColumn);
            var safePqmTable = hasPqm ? Sanitise(request.PqmTable) : "";
            var safePqmNameCol = hasPqm ? Sanitise(request.PqmQualNameColumn) : "";
            var safePqmTypeCol = hasPqm ? Sanitise(request.PqmQualTypeColumn) : "";

            await using var dataCommand = conn.CreateConfiguredCommand();
            var dataPredicate = BuildEligiblePredicate(
                dataCommand, safeQualCodeColumn, safeFulfilledColumn, safeQualTypeColumn,
                request.FulfilledValue, configuredMdTypes);

            var credCrseJoinSql = hasCredCrse
                ? $@"
INNER JOIN [{safeCredTable}] CRED ON STUD.[{safeQualCodeColumn}] = CRED.[{safeCredJoinCol}]
INNER JOIN [{safeCrseTable}] CRSE ON CRED.[{safeCredCourseCol}] = CRSE.[{safeCrseCourseCol}]"
                : "";

            var pqmJoinSql = hasPqm
                ? $@"
LEFT JOIN [{safePqmTable}] PQM
    ON UPPER(LTRIM(RTRIM(CAST(QUAL.[{safeQualNameColumn}] AS nvarchar(500))))) = UPPER(LTRIM(RTRIM(CAST(PQM.[{safePqmNameCol}] AS nvarchar(500)))))
    AND UPPER(LTRIM(RTRIM(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255))))) = UPPER(LTRIM(RTRIM(CAST(PQM.[{safePqmTypeCol}] AS nvarchar(255)))))"
                : "";

            var credCrseSelectSql = hasCredCrse
                ? $@"
    ISNULL(CAST(CRED.[{safeCredJoinCol}] AS nvarchar(255)), '') AS CRED_Qualification_Code,
    ISNULL(CAST(CRED.[{safeCredCourseCol}] AS nvarchar(255)), '') AS CRED_Course_Code,
    ISNULL(CAST(CRSE.[{safeCrseCourseCol}] AS nvarchar(255)), '') AS CRSE_Course_Code,
    ISNULL(CAST(CRSE.[_058] AS nvarchar(500)), '') AS Course_Name,
    ISNULL(CAST(CRSE.[_091] AS nvarchar(255)), '') AS Foundation_Course_Indicator,"
                : "";

            var pqmSelectSql = hasPqm
                ? $@"
    ISNULL(CAST(PQM.[{safePqmNameCol}] AS nvarchar(500)), '') AS PQM_Qualification_Name,
    ISNULL(CAST(PQM.[{safePqmTypeCol}] AS nvarchar(255)), '') AS PQM_Qualification_Type,"
                : "";

            var credCrseLinkCheckSql = hasCredCrse
                ? $@"
    CASE WHEN ISNULL(CAST(STUD.[{safeQualCodeColumn}] AS nvarchar(255)),'') = ISNULL(CAST(CRED.[{safeCredJoinCol}] AS nvarchar(255)),'') THEN 'MATCH' ELSE 'NO MATCH' END AS STUD_CRED_Link_Check,
    CASE WHEN ISNULL(CAST(CRED.[{safeCredCourseCol}] AS nvarchar(255)),'') = ISNULL(CAST(CRSE.[{safeCrseCourseCol}] AS nvarchar(255)),'') THEN 'MATCH' ELSE 'NO MATCH' END AS CRED_CRSE_Link_Check,"
                : "";

            var pqmValidationSql = hasPqm
                ? $"CASE WHEN PQM.[{safePqmNameCol}] IS NOT NULL AND LTRIM(RTRIM(CAST(PQM.[{safePqmNameCol}] AS nvarchar(500)))) <> '' THEN 'PASS' ELSE 'FAIL' END AS PQM_Validation_Result"
                : "'PASS' AS PQM_Validation_Result";

            dataCommand.CommandText = $@"
SELECT
    'Control_1' AS Control_Type,
    'Masters/PhD Students' AS Control_Description,
    ROW_NUMBER() OVER (ORDER BY STUD.[{safeQualCodeColumn}] ASC) AS Sample_Number,
    ISNULL(CAST(STUD.[{safeQualCodeColumn}] AS nvarchar(255)), '') AS Student_Qualification_Code,
    ISNULL(CAST(STUD.[_007] AS nvarchar(255)), '') AS Student_Number,
    ISNULL(CAST(STUD.[_008] AS nvarchar(255)), '') AS South_African_ID_Number,
    ISNULL(CAST(STUD.[_010] AS nvarchar(255)), '') AS Entrance_Category,
    ISNULL(CAST(STUD.[_011] AS nvarchar(255)), '') AS Date_Of_Birth,
    ISNULL(CAST(STUD.[_012] AS nvarchar(255)), '') AS Gender,
    ISNULL(CAST(STUD.[_013] AS nvarchar(255)), '') AS Race,
    ISNULL(CAST(STUD.[_014] AS nvarchar(255)), '') AS Nationality,
    ISNULL(CAST(STUD.[_019] AS nvarchar(255)), '') AS NSFAS_Status,
    ISNULL(CAST(STUD.[_021] AS nvarchar(255)), '') AS Previous_Years_Activity,
    ISNULL(CAST(STUD.[_024] AS nvarchar(255)), '') AS Attendance_Mode,
    ISNULL(CAST(STUD.[{safeFulfilledColumn}] AS nvarchar(255)), '') AS Qualification_Fulfilled_Indicator,
    ISNULL(CAST(STUD.[_066] AS nvarchar(500)), '') AS Student_Last_Name,
    ISNULL(CAST(STUD.[_067] AS nvarchar(500)), '') AS Student_First_Name,
    ISNULL(CAST(STUD.[_068] AS nvarchar(500)), '') AS Student_Middle_Name,
    ISNULL(CAST(STUD.[_073] AS nvarchar(255)), '') AS Percentage_Research_Curriculum,
    ISNULL(CAST(QUAL.[{safeQualCodeColumn}] AS nvarchar(255)), '') AS QUAL_Qualification_Code,
    ISNULL(CAST(QUAL.[{safeQualNameColumn}] AS nvarchar(500)), '') AS Qualification_Name,
    ISNULL(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255)), '') AS Qualification_Type,{credCrseSelectSql}{pqmSelectSql}
    CASE WHEN ISNULL(CAST(STUD.[{safeQualCodeColumn}] AS nvarchar(255)),'') = ISNULL(CAST(QUAL.[{safeQualCodeColumn}] AS nvarchar(255)),'') THEN 'MATCH' ELSE 'NO MATCH' END AS STUD_QUAL_Link_Check,{credCrseLinkCheckSql}
    CASE WHEN ISNULL(CAST(STUD.[{safeFulfilledColumn}] AS nvarchar(255)),'') = @FulfilledValue AND ({BuildMdTypeInlinePredicate(configuredMdTypes, safeQualTypeColumn)}) THEN 'PASS' ELSE 'FAIL' END AS Rule19_Filter_Check,
    {pqmValidationSql}
FROM [{safeStudTable}] STUD
INNER JOIN [{safeQualTable}] QUAL ON STUD.[{safeQualCodeColumn}] = QUAL.[{safeQualCodeColumn}]{credCrseJoinSql}{pqmJoinSql}
WHERE {dataPredicate}
ORDER BY STUD.[{safeQualCodeColumn}] ASC;";

            await using var reader = await dataCommand.ExecuteReaderAsync();
            var rows = new List<Rule19ValidationRowRecord>();
            var validationNumber = 0;
            while (await reader.ReadAsync())
            {
                validationNumber++;
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                displayValues.TryGetValue("Student_Qualification_Code", out var qualCodeValue);
                displayValues.TryGetValue("Qualification_Fulfilled_Indicator", out var fulfilledColumnValue);
                displayValues.TryGetValue("Qualification_Type", out var qualTypeValue);
                displayValues.TryGetValue("PQM_Validation_Result", out var validationResult);

                var pqmName = displayValues.TryGetValue("PQM_Qualification_Name", out var pn) ? pn : null;
                var pqmType = displayValues.TryGetValue("PQM_Qualification_Type", out var pt) ? pt : null;
                var pqmNameMatch = !string.IsNullOrWhiteSpace(pqmName);
                var pqmTypeMatch = !string.IsNullOrWhiteSpace(pqmType);

                rows.Add(new Rule19ValidationRowRecord
                {
                    ValidationNumber = validationNumber,
                    QualCodeValue = qualCodeValue ?? "",
                    FulfilledValue = fulfilledColumnValue ?? "",
                    QualTypeValue = qualTypeValue ?? "",
                    ValidationResult = validationResult ?? "PASS",
                    PqmQualName = pqmNameMatch ? pqmName : null,
                    PqmQualType = pqmTypeMatch ? pqmType : null,
                    PqmNameMatch = pqmNameMatch,
                    PqmTypeMatch = pqmTypeMatch,
                    DisplayValues = displayValues
                });
            }

            return rows;
        }

        private static string BuildMdTypeInlinePredicate(IReadOnlyList<string> mdTypes, string safeQualTypeColumn)
        {
            if (mdTypes.Count == 0)
                return "1=0";
            var parts = mdTypes.Select(t => $"ISNULL(CAST(QUAL.[{safeQualTypeColumn}] AS nvarchar(255)),'') = '{t.Replace("'", "''")}'");
            return string.Join(" OR ", parts);
        }

        private static Rule19ValidationSummary CreateBrowserPreview(Rule19ValidationSummary summary)
        {
            var previewRows = summary.MatchingRows.Take(BrowserPreviewRowLimit).ToList();
            var sourceRowCount = Math.Max(summary.MatchingCount, summary.MatchingRows.Count);
            var isPreviewOnly = sourceRowCount > previewRows.Count;
            var previewLimit = isPreviewOnly ? previewRows.Count : 0;

            return new Rule19ValidationSummary
            {
                Success = summary.Success,
                StudRecordCount = summary.StudRecordCount,
                QualRecordCount = summary.QualRecordCount,
                TotalValidated = summary.TotalValidated,
                MatchingCount = summary.MatchingCount,
                DisplayedCount = previewRows.Count,
                IsPreviewOnly = isPreviewOnly,
                PreviewLimit = previewLimit,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                StudTable = summary.StudTable,
                QualTable = summary.QualTable,
                QualCodeColumn = summary.QualCodeColumn,
                FulfilledColumn = summary.FulfilledColumn,
                FulfilledValue = summary.FulfilledValue,
                QualTypeColumn = summary.QualTypeColumn,
                MdTypesText = summary.MdTypesText,
                QualNameColumn = summary.QualNameColumn,
                MdTypes = summary.MdTypes.ToList(),
                TableLinkageText = summary.TableLinkageText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ShowAllRecords = summary.ShowAllRecords,
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                PqmTable = summary.PqmTable,
                PqmQualNameColumn = summary.PqmQualNameColumn,
                PqmQualTypeColumn = summary.PqmQualTypeColumn,
                CredTable = summary.CredTable,
                CredJoinCol = summary.CredJoinCol,
                CredCourseCol = summary.CredCourseCol,
                CrseTable = summary.CrseTable,
                CrseCourseCol = summary.CrseCourseCol,
                CredRecordCount = summary.CredRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                Breakdown = summary.Breakdown
                    .Select(item => new Rule19BreakdownItemViewModel
                    {
                        Value = item.Value,
                        Count = item.Count
                    })
                    .ToList(),
                MatchingRows = previewRows
                    .Select(row => new Rule19ValidationRowRecord
                    {
                        ValidationNumber = row.ValidationNumber,
                        QualCodeValue = row.QualCodeValue,
                        FulfilledValue = row.FulfilledValue,
                        QualTypeValue = row.QualTypeValue,
                        ValidationResult = row.ValidationResult,
                        PqmQualName = row.PqmQualName,
                        PqmQualType = row.PqmQualType,
                        PqmNameMatch = row.PqmNameMatch,
                        PqmTypeMatch = row.PqmTypeMatch,
                        DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
                    })
                    .ToList(),
                Warning = summary.Warning,
                Error = summary.Error
            };
        }

        private static void ApplyBrowserPreview(Rule19ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.MatchingRows = preview.MatchingRows;
        }

        private async Task<Rule19ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule19ValidationSummary summary, string? server)
        {
            var looksLikeStoredPreviewSample =
                summary.MatchingRows.Count > 0 &&
                summary.MatchingRows.Count <= BrowserPreviewRowLimit &&
                summary.MatchingCount > 0;

            if (!summary.IsPreviewOnly &&
                summary.MatchingRows.Count >= summary.MatchingCount &&
                !looksLikeStoredPreviewSample)
            {
                return summary;
            }

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(summary.Database) ||
                string.IsNullOrWhiteSpace(summary.StudTable) ||
                string.IsNullOrWhiteSpace(summary.QualTable) ||
                string.IsNullOrWhiteSpace(summary.QualCodeColumn) ||
                string.IsNullOrWhiteSpace(summary.FulfilledColumn) ||
                string.IsNullOrWhiteSpace(summary.QualTypeColumn))
            {
                return summary;
            }

            try
            {
                var expanded = await AnalyseAsync(new Rule19ValidationRequest
                {
                    ClientId = summary.ClientId,
                    RunId = summary.SavedRunId,
                    Server = server,
                    Database = summary.Database,
                    Driver = "ODBC Driver 17 for SQL Server",
                    StudTable = summary.StudTable,
                    QualTable = summary.QualTable,
                    QualCodeColumn = summary.QualCodeColumn,
                    FulfilledColumn = summary.FulfilledColumn,
                    FulfilledValue = summary.FulfilledValue,
                    QualTypeColumn = summary.QualTypeColumn,
                    MdTypesText = summary.MdTypesText,
                    QualNameColumn = summary.QualNameColumn,
                    PqmTable = summary.PqmTable,
                    PqmQualNameColumn = summary.PqmQualNameColumn,
                    PqmQualTypeColumn = summary.PqmQualTypeColumn,
                    CredTable = summary.CredTable,
                    CredJoinCol = summary.CredJoinCol,
                    CredCourseCol = summary.CredCourseCol,
                    CrseTable = summary.CrseTable,
                    CrseCourseCol = summary.CrseCourseCol
                });

                expanded.Timestamp = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                expanded.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                    ? "Saved Rule 19 results were expanded from the stored preview to the full result set."
                    : $"{summary.Warning} Full saved results were reloaded from the saved Rule 19 configuration.";

                return expanded;
            }
            catch
            {
                return summary;
            }
        }

        private static Rule19ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule19ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
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
            cmd.Parameters.AddWithValue("@TableName", tableName);

            await using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(0));

            return columns;
        }

        private static string FindFirst(IEnumerable<string> values, IEnumerable<string> preferredExact, IEnumerable<string> preferredContains)
        {
            var list = values.ToList();
            foreach (var exact in preferredExact)
            {
                var match = list.FirstOrDefault(value => value.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            foreach (var contains in preferredContains)
            {
                var match = list.FirstOrDefault(value => value.Contains(contains, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            return list.FirstOrDefault() ?? "";
        }

        private static void ValidateRequest(Rule19ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable) || string.IsNullOrWhiteSpace(request.QualTable))
                throw new InvalidOperationException("Both STUD and QUAL tables are required.");
            if (string.IsNullOrWhiteSpace(request.QualCodeColumn) ||
                string.IsNullOrWhiteSpace(request.FulfilledColumn) ||
                string.IsNullOrWhiteSpace(request.QualTypeColumn))
            {
                throw new InvalidOperationException("All Rule 19 columns are required.");
            }

            ValidateObjectName(request.StudTable);
            ValidateObjectName(request.QualTable);
            ValidateObjectName(request.QualCodeColumn);
            ValidateObjectName(request.FulfilledColumn);
            ValidateObjectName(request.QualTypeColumn);

            if (string.IsNullOrWhiteSpace(request.FulfilledValue))
                throw new InvalidOperationException("Select a fulfilled filter value.");
        }

        private static void ValidateRequest(Rule19VerifyRequest request) =>
            ValidateRequest(ToValidationRequest(request));

        private static void ValidateObjectName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Object name cannot be blank.");

            // SQL injection is handled by Sanitise (strips ], ', ;) + bracket quoting in all queries.
            // Only reject characters that can escape bracket quoting or terminate statements.
            if (name.Contains(']') || name.Contains('\'') || name.Contains(';') || name.Contains("--"))
                throw new InvalidOperationException($"Invalid object name: {name}");
        }

        private static string NormalizeComparableValue(string value) =>
            (value ?? "").Trim().ToUpperInvariant();

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

        private static string EscapeSqlString(string value) =>
            value.Replace("'", "''");

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


