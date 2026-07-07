using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule17Service : IRule17Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule17Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<TableListResult> GetTablesAsync(string server, string database, string driver)
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

                var autoStudTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_STUD", StringComparison.OrdinalIgnoreCase))
                    ?? tables.FirstOrDefault();
                var autoQualTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_QUAL", StringComparison.OrdinalIgnoreCase))
                    ?? tables.FirstOrDefault(t => t.Contains("QUAL", StringComparison.OrdinalIgnoreCase));

                return new TableListResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = autoStudTable,
                    AutoQualTable = autoQualTable
                };
            }
            catch (Exception ex)
            {
                return new TableListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule17ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                var columns = await GetTableColumnsAsync(server, database, driver, tableName);
                var autoFilterColumn = columns.FirstOrDefault(c =>
                        c.Equals("_025", StringComparison.OrdinalIgnoreCase))
                    ?? columns.FirstOrDefault();

                return new Rule17ColumnSelectionResult
                {
                    Success = true,
                    Columns = columns,
                    AutoFilterColumn = autoFilterColumn,
                    AutoBreakdownColumn = null
                };
            }
            catch (Exception ex)
            {
                return new Rule17ColumnSelectionResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule17FilterValueResult> GetFilterValuesAsync(string server, string database, string driver, string tableName, string filterColumn)
        {
            try
            {
                ValidateObjectName(tableName);
                ValidateObjectName(filterColumn);

                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var safeTable = Sanitise(tableName);
                var safeColumn = Sanitise(filterColumn);

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = $@"
SELECT TOP 20
    LTRIM(RTRIM(CAST([{safeColumn}] AS nvarchar(4000)))) AS FilterValue,
    COUNT(*) AS RecordCount
FROM [{safeTable}]
WHERE [{safeColumn}] IS NOT NULL
GROUP BY LTRIM(RTRIM(CAST([{safeColumn}] AS nvarchar(4000))))
ORDER BY COUNT(*) DESC, FilterValue ASC;";

                await using var reader = await cmd.ExecuteReaderAsync();
                var options = new List<Rule17FilterValueOption>();
                while (await reader.ReadAsync())
                {
                    var value = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    var count = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                    options.Add(new Rule17FilterValueOption
                    {
                        Value = value,
                        Count = count,
                        Label = $"{value} ({count:N0} records)"
                    });
                }

                return new Rule17FilterValueResult
                {
                    Success = true,
                    Options = options,
                    DefaultValue = options.FirstOrDefault()?.Value
                };
            }
            catch (Exception ex)
            {
                return new Rule17FilterValueResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule17VerifyResult> VerifyTableAsync(Rule17VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);

                var safeTable = Sanitise(request.TableName);
                var safeFilterColumn = Sanitise(request.FilterColumn);
                var parsedValues = ParseFilterValues(request.FilterValue);
                var normalizedValues = parsedValues
                    .Select(NormalizeComparableValue)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var totalRecords = await CountAsync(conn, $"SELECT COUNT(*) FROM [{safeTable}];");

                await using var countCommand = conn.CreateConfiguredCommand();
                var countPredicate = BuildFilterPredicate(countCommand, safeFilterColumn, parsedValues, normalizedValues);
                countCommand.CommandText = $@"
SELECT COUNT(*)
FROM [{safeTable}]
WHERE {countPredicate};";
                var matchingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

                await using var sampleCommand = conn.CreateConfiguredCommand();
                var samplePredicate = BuildFilterPredicate(sampleCommand, safeFilterColumn, parsedValues, normalizedValues);
                sampleCommand.CommandText = $@"
SELECT TOP 5 *
FROM [{safeTable}]
WHERE {samplePredicate}
ORDER BY NEWID();";

                await using var reader = await sampleCommand.ExecuteReaderAsync();
                var sampleRows = new List<Rule17SampleRowViewModel>();
                while (await reader.ReadAsync())
                {
                    var row = new Rule17SampleRowViewModel();
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        row.Values[reader.GetName(i)] = reader.IsDBNull(i)
                            ? null
                            : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                    }
                    sampleRows.Add(row);
                }

                return new Rule17VerifyResult
                {
                    Success = true,
                    TotalRecords = totalRecords,
                    MatchingCount = matchingCount,
                    SampleRows = sampleRows
                };
            }
            catch (Exception ex)
            {
                return new Rule17VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule17ValidationSummary> RunValidationAsync(Rule17ValidationRequest request, string? userEmail = null, string? userName = null)
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
                        summary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                ApplyBrowserPreview(summary);

                return summary;
            }
            catch (Exception ex)
            {
                return new Rule17ValidationSummary
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

        public async Task<Rule17WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.StudTable, '') AS TableName,
    ISNULL(vr.DeceasedTable, '') AS FilterColumn,
    ISNULL(vr.StudColumn, '') AS QualTable,
    ISNULL(vr.DeceasedColumn, '') AS FilterValue,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 17
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var deserializedSummary = DeserializeSummary(reader.IsDBNull(11) ? null : reader.GetString(11));
            if (includeSummary && deserializedSummary != null)
            {
                ApplyBrowserPreview(deserializedSummary);
            }
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule17WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                TableName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                FilterColumn = reader.IsDBNull(5) ? "" : reader.GetString(5),
                QualTable   = reader.IsDBNull(6) ? "dbo_QUAL" : reader.GetString(6),
                QualJoinCol = deserializedSummary?.QualJoinCol ?? "_001",
                QualNameCol = deserializedSummary?.QualNameCol ?? "_003",
                FilterValue = reader.IsDBNull(7) ? "F" : reader.GetString(7),
                CurrentStatus = reader.IsDBNull(8) ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                SampleSize = deserializedSummary?.SampleSize ?? 1,
                ShowAllRecords = deserializedSummary?.ShowAllRecords ?? true,
                Summary = summary
            };

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

            return workspace;
        }

        public async Task<Rule17RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON, vr.ExceptionsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 17;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            if (includeFullResults)
            {
                var fullRows = DeserializeRows(reader.IsDBNull(7) ? null : reader.GetString(7));
                if (fullRows.Count < summary.MatchingCount)
                {
                    var sourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5);
                    if (!string.IsNullOrWhiteSpace(sourceServer))
                    {
                        fullRows = await RegenerateFullRowsAsync(sourceServer, summary);
                    }
                }

                summary.MatchingRows = fullRows;
                summary.DisplayedCount = summary.MatchingRows.Count;
                summary.IsPreviewOnly = false;
                summary.PreviewLimit = 0;
            }
            else
            {
                ApplyBrowserPreview(summary);
            }

            var viewModel = new Rule17RunReviewViewModel
            {
                RunId = reader.GetInt32(0),
                ClientId = reader.GetInt32(1),
                IsCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Summary = summary
            };

            await reader.CloseAsync();

            viewModel.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, viewModel.ClientId, currentUserId.Value) ?? ""
                : "";
            viewModel.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            viewModel.HasDataAnalystSignoff = viewModel.Signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return viewModel;
        }

        public async Task<Rule17WorkspaceSaveResult> SaveWorkspaceAsync(Rule17ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule17WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run the filter first so the workspace can be saved."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule17WorkspaceSaveResult
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
    StudTable = @TableName,
    DeceasedTable = @FilterColumn,
    StudColumn = @QualTable,
    DeceasedColumn = @FilterValue,
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
                command.Parameters.AddWithValue("@TableName", request.TableName);
                command.Parameters.AddWithValue("@FilterColumn", request.FilterColumn);
                command.Parameters.AddWithValue("@QualTable", request.QualTable);
                command.Parameters.AddWithValue("@FilterValue", request.FilterValue);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule17|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.TableName}|{request.QualTable}|{request.FilterColumn}|{request.FilterValue}|{request.SampleSize}|{request.ShowAllRecords}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule17WorkspaceSaveResult
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
                return new Rule17WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule17WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule17WorkspaceSaveResult
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
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule17|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule17WorkspaceSaveResult
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
                return new Rule17WorkspaceSaveResult
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

        public Task<string> GenerateSqlAsync(Rule17ValidationRequest request)
        {
            ValidateSqlRequest(request);

            var safeTable    = Sanitise(request.TableName);
            var safeQual     = Sanitise(request.QualTable);
            var safeJoin     = Sanitise(request.QualJoinCol);
            var safeName     = Sanitise(request.QualNameCol);
            var safeFilter   = Sanitise(request.FilterColumn);
            var parsedValues = ParseFilterValues(request.FilterValue);
            var rawList      = string.Join(", ", parsedValues.Select(v => $"'{EscapeSqlString(v)}'"));
            var trimExpr     = $"LTRIM(RTRIM(ISNULL(CAST(S.[{safeFilter}] AS NVARCHAR(255)), '')))";
            var sqlPredicate = $"{trimExpr} IN ({rawList})";

            var sql = $@"-- ============================================================================
-- HEMIS RULE 17: GRADUATE STUDENTS FULFILLED QUALIFICATION VALIDATION
-- STUD Table : [{safeTable}]
-- QUAL Table : [{safeQual}]
-- Join       : S.[_001] = QUAL.[{safeJoin}]
-- QUAL Name  : QUAL.[{safeName}] AS Qualification_Name
-- Filter     : S.[{safeFilter}] IN ({string.Join(", ", parsedValues)})
-- ============================================================================

DROP TABLE IF EXISTS #Rule17_Graduates;

-- STEP 1: Extract full graduate population
SELECT
    ROW_NUMBER() OVER (
        ORDER BY S.[_007], S.[_001]
    ) AS Extract_Number,
    'Rule_17'                                    AS Rule_Number,
    'Control_1'                                  AS Control_Type,
    S.[_007]                                     AS Student_Number,
    S.[_008]                                     AS South_African_ID_Number,
    S.[_001]                                     AS Qualification_Code,
    QUAL.[{safeName}]                            AS Qualification_Name,
    S.[_010]                                     AS Entrance_Category,
    S.[_011]                                     AS Date_Of_Birth,
    S.[_012]                                     AS Gender,
    S.[_013]                                     AS Race,
    S.[_014]                                     AS Nationality,
    S.[_049]                                     AS Citizen_Resident_Status,
    S.[_019]                                     AS NSFAS_Status,
    S.[_021]                                     AS Previous_Years_Activity,
    S.[_024]                                     AS Attendance_Mode,
    S.[_025]                                     AS Qualification_Fulfilled_Indicator,
    S.[_066]                                     AS Student_Last_Name,
    S.[_067]                                     AS Student_First_Name,
    S.[_068]                                     AS Student_Middle_Name,
    'PASS'                                       AS Validation_Result
INTO #Rule17_Graduates
FROM [{safeTable}] S
INNER JOIN [{safeQual}] QUAL ON S.[_001] = QUAL.[{safeJoin}]
WHERE {sqlPredicate};

-- STEP 2: Return extracted records
SELECT *
FROM #Rule17_Graduates
ORDER BY Extract_Number;

-- STEP 3: Summary
SELECT
    Rule_Number,
    Control_Type,
    COUNT(*) AS Total_Records
FROM #Rule17_Graduates
GROUP BY Rule_Number, Control_Type;

-- STEP 4: Breakdown of Qualification Fulfilled Indicator
SELECT
    Qualification_Fulfilled_Indicator,
    COUNT(*) AS Record_Count
FROM #Rule17_Graduates
GROUP BY Qualification_Fulfilled_Indicator
ORDER BY Record_Count DESC;

DROP TABLE IF EXISTS #Rule17_Graduates;";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule17ValidationSummary> AnalyseAsync(Rule17ValidationRequest request)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var safeTable      = Sanitise(request.TableName);
            var safeQualTable  = Sanitise(request.QualTable);
            var safeQualJoin   = Sanitise(request.QualJoinCol);
            var safeQualName   = Sanitise(request.QualNameCol);
            var safeFilterCol  = Sanitise(request.FilterColumn);
            var parsedValues   = ParseFilterValues(request.FilterValue);
            var normalizedValues = parsedValues
                .Select(NormalizeComparableValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await using var countCommand = conn.CreateConfiguredCommand();
            var countPredicate = BuildFilterPredicate(countCommand, safeFilterCol, parsedValues, normalizedValues, "S");
            countCommand.CommandText = $@"
SELECT COUNT(*)
FROM [{safeTable}] S
INNER JOIN [{safeQualTable}] QUAL ON S.[_001] = QUAL.[{safeQualJoin}]
WHERE {countPredicate};";
            var filteredCount  = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
            var passCount      = filteredCount;

            var breakdown = new List<Rule17BreakdownItemViewModel>();
            await using (var bdCmd = conn.CreateConfiguredCommand())
            {
                bdCmd.CommandText = $@"
SELECT
    LTRIM(RTRIM(CAST(S.[{safeFilterCol}] AS nvarchar(255)))) AS BreakdownValue,
    COUNT(*) AS RecordCount
FROM [{safeTable}] S
INNER JOIN [{safeQualTable}] QUAL ON S.[_001] = QUAL.[{safeQualJoin}]
WHERE {BuildFilterPredicate(bdCmd, safeFilterCol, parsedValues, normalizedValues, "S", "BD")}
GROUP BY LTRIM(RTRIM(CAST(S.[{safeFilterCol}] AS nvarchar(255))))
ORDER BY COUNT(*) DESC, BreakdownValue ASC;";
                await using var bdReader = await bdCmd.ExecuteReaderAsync();
                while (await bdReader.ReadAsync())
                    breakdown.Add(new Rule17BreakdownItemViewModel
                    {
                        Value = bdReader.IsDBNull(0) ? "(blank)" : bdReader.GetString(0),
                        Count = bdReader.IsDBNull(1) ? 0 : Convert.ToInt32(bdReader.GetValue(1))
                    });
            }

            var matchingRows = filteredCount > 0
                ? await LoadFilteredRowsAsync(conn, safeTable, safeQualTable, safeQualJoin, safeQualName, safeFilterCol, request.FilterColumn, parsedValues, normalizedValues, BrowserPreviewRowLimit)
                : new List<Rule17ValidationRowRecord>();

            var displayedCount = matchingRows.Count;

            return new Rule17ValidationSummary
            {
                Success      = true,
                TotalValidated = filteredCount,
                MatchingCount  = passCount,
                DisplayedCount = displayedCount,
                PassCount    = passCount,
                FailCount    = 0,
                ExceptionRate = filteredCount > 0 ? 100m : 0m,
                Status       = filteredCount > 0 ? "COMPLETE" : "NO MATCHING DATA",
                Timestamp    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database     = request.Database,
                TableName    = request.TableName,
                QualTable    = request.QualTable,
                QualJoinCol  = request.QualJoinCol,
                QualNameCol  = request.QualNameCol,
                FilterColumn = request.FilterColumn,
                FilterValue  = string.Join(", ", parsedValues),
                BreakdownColumn = request.FilterColumn,
                SampleSize   = Math.Max(request.SampleSize, 1),
                ShowAllRecords = true,
                Sampled      = false,
                IsPreviewOnly = filteredCount > displayedCount,
                PreviewLimit  = filteredCount > displayedCount ? displayedCount : 0,
                ClientId     = request.ClientId,
                Breakdown    = breakdown,
                MatchingRows = matchingRows
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule17ValidationRequest request, Rule17ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 17);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 17);
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
    @ClientID, @UserID, 17, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @TableName, @FilterColumn, @QualTable, @FilterValue,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Graduate Students Fulfilled Qualification Validation");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@TableName", request.TableName);
            command.Parameters.AddWithValue("@FilterColumn", request.FilterColumn);
            command.Parameters.AddWithValue("@QualTable", request.QualTable);
            command.Parameters.AddWithValue("@FilterValue", request.FilterValue.Trim());
            var persistedSummary = CreateBrowserPreview(summary);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary.MatchingRows)));
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
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule17|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
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

        private static void ValidateRequest(Rule17ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.TableName))
                throw new InvalidOperationException("Source table is required.");
            if (string.IsNullOrWhiteSpace(request.QualTable))
                throw new InvalidOperationException("QUAL table is required.");
            if (string.IsNullOrWhiteSpace(request.QualJoinCol))
                request.QualJoinCol = "_001";
            if (string.IsNullOrWhiteSpace(request.QualNameCol))
                request.QualNameCol = "_003";
            if (string.IsNullOrWhiteSpace(request.FilterColumn))
                throw new InvalidOperationException("Filter column is required.");
            if (string.IsNullOrWhiteSpace(request.FilterValue))
                throw new InvalidOperationException("Filter value is required.");

            ValidateObjectName(request.TableName);
            ValidateObjectName(request.QualTable);
            ValidateObjectName(request.QualJoinCol);
            ValidateObjectName(request.QualNameCol);
            ValidateObjectName(request.FilterColumn);
            if (!string.IsNullOrWhiteSpace(request.BreakdownColumn))
                ValidateObjectName(request.BreakdownColumn);

            if (request.SampleSize <= 0)
                request.SampleSize = 1;

            request.ShowAllRecords = true;

            if (ParseFilterValues(request.FilterValue).Count == 0)
                throw new InvalidOperationException("Enter at least one filter value.");
        }

        private static void ValidateSqlRequest(Rule17ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.TableName))
                throw new InvalidOperationException("Source table is required.");
            if (string.IsNullOrWhiteSpace(request.QualTable))
                throw new InvalidOperationException("QUAL table is required.");
            if (string.IsNullOrWhiteSpace(request.QualJoinCol))
                request.QualJoinCol = "_001";
            if (string.IsNullOrWhiteSpace(request.QualNameCol))
                request.QualNameCol = "_003";
            if (string.IsNullOrWhiteSpace(request.FilterColumn))
                throw new InvalidOperationException("Filter column is required.");
            if (string.IsNullOrWhiteSpace(request.FilterValue))
                throw new InvalidOperationException("Filter value is required.");

            ValidateObjectName(request.TableName);
            ValidateObjectName(request.QualTable);
            ValidateObjectName(request.QualJoinCol);
            ValidateObjectName(request.QualNameCol);
            ValidateObjectName(request.FilterColumn);
            if (!string.IsNullOrWhiteSpace(request.BreakdownColumn))
                ValidateObjectName(request.BreakdownColumn);

            if (ParseFilterValues(request.FilterValue).Count == 0)
                throw new InvalidOperationException("Enter at least one filter value.");
        }

        private static void ValidateRequest(Rule17VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.TableName))
                throw new InvalidOperationException("Source table is required.");
            if (string.IsNullOrWhiteSpace(request.FilterColumn))
                throw new InvalidOperationException("Filter column is required.");
            if (string.IsNullOrWhiteSpace(request.FilterValue))
                throw new InvalidOperationException("Filter value is required.");

            ValidateObjectName(request.TableName);
            ValidateObjectName(request.FilterColumn);
            if (ParseFilterValues(request.FilterValue).Count == 0)
                throw new InvalidOperationException("Enter at least one filter value.");
        }

        private static List<string> ParseFilterValues(string? value) =>
            NumericFilterValueHelper.ParseValues(value);

        private static string NormalizeComparableValue(string? value) =>
            NumericFilterValueHelper.NormalizeNumericLikeValue(value);

        private static string BuildFilterPredicate(SqlCommand command, string safeFilterColumn, IReadOnlyList<string> rawValues, IReadOnlyList<string> normalizedValues, string tableAlias = "", string paramPrefix = "")
        {
            var colRef = string.IsNullOrEmpty(tableAlias) ? $"[{safeFilterColumn}]" : $"{tableAlias}.[{safeFilterColumn}]";
            var trimmedExpression = $"LTRIM(RTRIM(CAST({colRef} AS nvarchar(4000))))";
            var normalizedExpression = BuildNormalizedSqlExpression(trimmedExpression);

            var rawParameterNames = new List<string>();
            for (var i = 0; i < rawValues.Count; i++)
            {
                var parameterName = $"@{paramPrefix}RawValue{i}";
                command.Parameters.AddWithValue(parameterName, rawValues[i]);
                rawParameterNames.Add(parameterName);
            }

            var normalizedParameterNames = new List<string>();
            for (var i = 0; i < normalizedValues.Count; i++)
            {
                var parameterName = $"@{paramPrefix}NormalizedValue{i}";
                command.Parameters.AddWithValue(parameterName, normalizedValues[i]);
                normalizedParameterNames.Add(parameterName);
            }

            return $"({trimmedExpression} IN ({string.Join(", ", rawParameterNames)}) OR {normalizedExpression} IN ({string.Join(", ", normalizedParameterNames)}))";
        }

        private static string BuildNormalizedSqlExpression(string trimmedExpression) =>
            NumericFilterValueHelper.BuildNormalizedSqlExpression(trimmedExpression);

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

        private static string? FindFirst(IEnumerable<string> columns, string[] exactMatches, string[] containsMatches)
        {
            foreach (var exact in exactMatches)
            {
                var match = columns.FirstOrDefault(c => c.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            foreach (var fragment in containsMatches)
            {
                var match = columns.FirstOrDefault(c => c.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return columns.FirstOrDefault();
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

        private async Task<Rule17ValidationSummary?> GetValidationSummaryAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ResultsJSON FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value
                ? null
                : DeserializeSummary(Convert.ToString(value));
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

        private static Rule17ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule17ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

        private static List<Rule17ValidationRowRecord> DeserializeRows(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<Rule17ValidationRowRecord>();

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<List<Rule17ValidationRowRecord>>(decoded) ?? new List<Rule17ValidationRowRecord>();
            }
            catch
            {
                return new List<Rule17ValidationRowRecord>();
            }
        }

        private static Rule17ValidationSummary CreateBrowserPreview(Rule17ValidationSummary summary)
        {
            var previewRows = summary.MatchingRows
                .Where(row => string.Equals(row.BreakdownValue, "PASS", StringComparison.OrdinalIgnoreCase))
                .Take(BrowserPreviewRowLimit)
                .ToList();

            if (previewRows.Count == 0)
                previewRows = summary.MatchingRows.Take(BrowserPreviewRowLimit).ToList();

            var sourceRowCount = Math.Max(summary.MatchingCount, summary.MatchingRows.Count);
            var isPreviewOnly = summary.IsPreviewOnly || sourceRowCount > previewRows.Count;
            var previewLimit = isPreviewOnly ? previewRows.Count : 0;

            return new Rule17ValidationSummary
            {
                Success = summary.Success,
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
                Database    = summary.Database,
                TableName   = summary.TableName,
                QualTable   = summary.QualTable,
                QualJoinCol = summary.QualJoinCol,
                QualNameCol = summary.QualNameCol,
                FilterColumn = summary.FilterColumn,
                FilterValue = summary.FilterValue,
                BreakdownColumn = summary.BreakdownColumn,
                SampleSize = summary.SampleSize,
                ShowAllRecords = summary.ShowAllRecords,
                Sampled = summary.Sampled,
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                Breakdown = summary.Breakdown
                    .Select(item => new Rule17BreakdownItemViewModel
                    {
                        Value = item.Value,
                        Count = item.Count
                    })
                    .ToList(),
                MatchingRows = previewRows,
                Warning = summary.Warning,
                Error = summary.Error
            };
        }

        private async Task<List<Rule17ValidationRowRecord>> RegenerateFullRowsAsync(string server, Rule17ValidationSummary summary)
        {
            var parsedValues = ParseFilterValues(summary.FilterValue);
            var normalizedValues = parsedValues
                .Select(NormalizeComparableValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var connStr = BuildConnectionString(server, summary.Database, "ODBC Driver 17 for SQL Server");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            return await LoadFilteredRowsAsync(
                conn,
                Sanitise(summary.TableName),
                Sanitise(!string.IsNullOrWhiteSpace(summary.QualTable) ? summary.QualTable : "dbo_QUAL"),
                Sanitise(!string.IsNullOrWhiteSpace(summary.QualJoinCol) ? summary.QualJoinCol : "_001"),
                Sanitise(!string.IsNullOrWhiteSpace(summary.QualNameCol) ? summary.QualNameCol : "_003"),
                Sanitise(summary.FilterColumn),
                summary.FilterColumn,
                parsedValues,
                normalizedValues,
                null);
        }

        private async Task<List<Rule17ValidationRowRecord>> LoadFilteredRowsAsync(
            SqlConnection conn,
            string safeTable,
            string safeQualTable,
            string safeQualJoin,
            string safeQualName,
            string safeFilterColumn,
            string requestedFilterColumn,
            List<string> parsedValues,
            List<string> normalizedValues,
            int? maxRows)
        {
            await using var dataCommand = conn.CreateConfiguredCommand();
            var dataPredicate = BuildFilterPredicate(dataCommand, safeFilterColumn, parsedValues, normalizedValues, "S");
            var topClause = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value} " : "";
            dataCommand.CommandText = $@"
SELECT {topClause}
    ROW_NUMBER() OVER (ORDER BY S.[_007], S.[_001])         AS Extract_Number,
    'Rule_17'                                               AS Rule_Number,
    'Control_1'                                             AS Control_Type,
    CAST(S.[_007]  AS nvarchar(255))                       AS Student_Number,
    CAST(S.[_008]  AS nvarchar(255))                       AS South_African_ID_Number,
    CAST(S.[_001]  AS nvarchar(255))                       AS Qualification_Code,
    CAST(QUAL.[{safeQualName}] AS nvarchar(255))           AS Qualification_Name,
    CAST(S.[_010]  AS nvarchar(255))                       AS Entrance_Category,
    CAST(S.[_011]  AS nvarchar(255))                       AS Date_Of_Birth,
    CAST(S.[_012]  AS nvarchar(255))                       AS Gender,
    CAST(S.[_013]  AS nvarchar(255))                       AS Race,
    CAST(S.[_014]  AS nvarchar(255))                       AS Nationality,
    CAST(S.[_049]  AS nvarchar(255))                       AS Citizen_Resident_Status,
    CAST(S.[_019]  AS nvarchar(255))                       AS NSFAS_Status,
    CAST(S.[_021]  AS nvarchar(255))                       AS Previous_Years_Activity,
    CAST(S.[_024]  AS nvarchar(255))                       AS Attendance_Mode,
    CAST(S.[_025]  AS nvarchar(255))                       AS Qualification_Fulfilled_Indicator,
    CAST(S.[_066]  AS nvarchar(255))                       AS Student_Last_Name,
    CAST(S.[_067]  AS nvarchar(255))                       AS Student_First_Name,
    CAST(S.[_068]  AS nvarchar(255))                       AS Student_Middle_Name,
    'PASS'                                                  AS Validation_Result
FROM [{safeTable}] S
INNER JOIN [{safeQualTable}] QUAL ON S.[_001] = QUAL.[{safeQualJoin}]
WHERE {dataPredicate}
ORDER BY S.[_007], S.[_001];";

            await using var reader = await dataCommand.ExecuteReaderAsync();
            var rows = new List<Rule17ValidationRowRecord>();
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

                displayValues.TryGetValue(requestedFilterColumn, out var filterValue);
                displayValues.TryGetValue("Validation_Result", out var validationResult);

                rows.Add(new Rule17ValidationRowRecord
                {
                    ValidationNumber = validationNumber,
                    FilterValue = filterValue ?? "",
                    BreakdownValue = validationResult ?? "",
                    DisplayValues = displayValues
                });
            }

            return rows;
        }

        private static void ApplyBrowserPreview(Rule17ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.MatchingRows = preview.MatchingRows;
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


