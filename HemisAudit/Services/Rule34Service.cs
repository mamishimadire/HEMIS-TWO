using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule34Service : IRule34Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private static readonly (string FirstDay, string LastDay, string CensusDate)[] AutoColumnPrioritySets =
        {
            ("First_Day_Class", "Last_Day_Class", "Midpoint_CENSUS_DATE"),
            ("FirstDayClass", "LastDayClass", "CENSUS_DATE"),
            ("First_Class_Date", "Last_Class_Date", "CensusDate")
        };

        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;
        private readonly IHttpClientFactory _httpClientFactory;

        public Rule34Service(IConfiguration configuration, IHttpClientFactory httpClientFactory, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _pendingValidationCache = pendingValidationCache;
        }

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, "master", driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT name FROM sys.databases WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name", conn)
                    .WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                var dbs = new List<string>();
                while (await reader.ReadAsync())
                    dbs.Add(reader.GetString(0));
                return new DatabaseListResult { Success = true, Databases = dbs };
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
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME", conn)
                    .WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));

                var autoTable = tables.FirstOrDefault(t =>
                    t.Equals("CENSUS_DATES", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("dbo_CENSUS_DATES", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Census_Dates", StringComparison.OrdinalIgnoreCase));

                return new TableListResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = autoTable
                };
            }
            catch (Exception ex)
            {
                return new TableListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule34ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                var safeTable = Sanitise(tableName);
                var connStr = BuildConnectionString(server, database, driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(@"
SELECT COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @TableName
ORDER BY ORDINAL_POSITION;", conn)
                    .WithLargeDataTimeout();
                cmd.Parameters.AddWithValue("@TableName", safeTable);
                using var reader = await cmd.ExecuteReaderAsync();

                var columns = new List<string>();
                var dateColumns = new List<string>();
                while (await reader.ReadAsync())
                {
                    var column = reader.GetString(0);
                    var dataType = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    columns.Add(column);

                    if (dataType.Contains("date", StringComparison.OrdinalIgnoreCase) ||
                        dataType.Contains("time", StringComparison.OrdinalIgnoreCase))
                    {
                        dateColumns.Add(column);
                    }
                }

                var autoFirst = FindFirst(columns, dateColumns,
                    AutoColumnPrioritySets.Select(s => s.FirstDay).ToArray(),
                    new[] { "first_day", "firstday", "first_class", "firstdayclass" });
                var autoLast = FindFirst(columns, dateColumns,
                    AutoColumnPrioritySets.Select(s => s.LastDay).ToArray(),
                    new[] { "last_day", "lastday", "last_class", "lastdayclass" });
                var autoCensus = FindFirst(columns, dateColumns,
                    AutoColumnPrioritySets.Select(s => s.CensusDate).ToArray(),
                    new[] { "current_census", "midpoint_census", "census_date", "censusdate", "midpoint" });

                var autoBlock = columns.FirstOrDefault(c =>
                    c.Equals("Block", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("BLOCK_CODE", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("BLOCK_CODE_2", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("BlockCode", StringComparison.OrdinalIgnoreCase) ||
                    c.Contains("block", StringComparison.OrdinalIgnoreCase));

                return new Rule34ColumnSelectionResult
                {
                    Success = true,
                    Columns = columns,
                    AutoFirstDayColumn = autoFirst,
                    AutoLastDayColumn = autoLast,
                    AutoCensusDateColumn = autoCensus,
                    AutoBlockColumn = autoBlock
                };
            }
            catch (Exception ex)
            {
                return new Rule34ColumnSelectionResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule34VerifyResult> VerifyTableAsync(Rule34VerifyRequest request)
        {
            try
            {
                var table = Sanitise(request.TableName);
                var clientTable = Sanitise(request.ClientTableName);
                var firstDay = Sanitise(request.FirstDayColumn);
                var lastDay = Sanitise(request.LastDayColumn);
                var census = Sanitise(request.CensusDateColumn);
                var clientJoin = Sanitise(request.ClientJoinColumn);
                var useClientComparison = UsesClientComparison(request.ClientTableName, request.ClientJoinColumn, request.BlockColumn);

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var blockCol = !string.IsNullOrWhiteSpace(request.BlockColumn) ? Sanitise(request.BlockColumn) : "";

                var totalCommand = new SqlCommand($"SELECT COUNT(*) FROM [{table}];", conn)
                    .WithLargeDataTimeout();
                var totalRecords = Convert.ToInt32(await totalCommand.ExecuteScalarAsync());

                var cteSql = useClientComparison
                    ? $"WITH {BuildClientComparisonCte(clientTable, clientJoin, census)}\n"
                    : "";
                var joinSql = useClientComparison
                    ? $"\n{BuildClientComparisonJoin(blockCol, clientJoin)}"
                    : "";
                var censusSourceSql = useClientComparison
                    ? $"cmp.[{census}] AS CensusDateValue"
                    : $"src.[{census}] AS CensusDateValue";
                var blockSourceSql = !string.IsNullOrWhiteSpace(blockCol)
                    ? $",\n    src.[{blockCol}] AS BlockValue"
                    : "";

                var sampleSql = $@"{cteSql}SELECT TOP 5
    src.[{firstDay}] AS FirstDayValue,
    src.[{lastDay}] AS LastDayValue,
    {censusSourceSql}{blockSourceSql}
FROM [{table}] src{joinSql};";

                using var sampleCommand = new SqlCommand(sampleSql, conn)
                    .WithLargeDataTimeout();
                using var reader = await sampleCommand.ExecuteReaderAsync();
                var rows = new List<Rule34SampleRowViewModel>();
                while (await reader.ReadAsync())
                {
                    var row = new Rule34SampleRowViewModel();
                    row.Values[firstDay] = reader["FirstDayValue"] == DBNull.Value ? null : FormatValue(reader["FirstDayValue"]);
                    row.Values[lastDay] = reader["LastDayValue"] == DBNull.Value ? null : FormatValue(reader["LastDayValue"]);
                    row.Values[census] = reader["CensusDateValue"] == DBNull.Value ? null : FormatValue(reader["CensusDateValue"]);
                    if (!string.IsNullOrWhiteSpace(blockCol))
                        row.Values[blockCol] = reader["BlockValue"] == DBNull.Value ? null : FormatValue(reader["BlockValue"]);
                    rows.Add(row);
                }

                return new Rule34VerifyResult
                {
                    Success = true,
                    TotalRecords = totalRecords,
                    SampleRows = rows
                };
            }
            catch (Exception ex)
            {
                return new Rule34VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule34HolidayLoadResult> LoadHolidaysAsync(int startYear, int endYear)
        {
            if (startYear <= 0 || endYear <= 0 || startYear > endYear)
            {
                return new Rule34HolidayLoadResult
                {
                    Success = false,
                    Error = "Start year must be less than or equal to end year."
                };
            }

            try
            {
                var holidays = await FetchHolidaysAsync(startYear, endYear);
                return new Rule34HolidayLoadResult
                {
                    Success = true,
                    StartYear = startYear,
                    EndYear = endYear,
                    TotalCount = holidays.Count,
                    Holidays = holidays
                        .OrderBy(h => h.Date, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
            }
            catch (Exception ex)
            {
                return new Rule34HolidayLoadResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule34ValidationSummary> RunValidationAsync(Rule34ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);

                var safeTable = Sanitise(request.TableName);
                var safeClientTable = Sanitise(request.ClientTableName);
                var safeFirst = Sanitise(request.FirstDayColumn);
                var safeLast = Sanitise(request.LastDayColumn);
                var safeCensus = Sanitise(request.CensusDateColumn);
                var safeClientJoin = Sanitise(request.ClientJoinColumn);
                var safeBlock = !string.IsNullOrWhiteSpace(request.BlockColumn) ? Sanitise(request.BlockColumn) : "";
                var useClientComparison = UsesClientComparison(request);

                var holidays = await FetchHolidaysAsync(request.StartYear, request.EndYear);
                var holidayLookup = holidays
                    .ToDictionary(h => DateOnly.ParseExact(h.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), h => h.Name);

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var optionalColumns = await GetOptionalCurrentDayColumnsAsync(conn, safeTable);
                var selectedColumns = new List<string>
                {
                    $"src.[{safeFirst}] AS FirstDayValue",
                    $"src.[{safeLast}] AS LastDayValue",
                    useClientComparison
                        ? $"cmp.[{safeCensus}] AS CensusDateValue"
                        : $"src.[{safeCensus}] AS CensusDateValue"
                };

                if (!string.IsNullOrWhiteSpace(optionalColumns.CurrentDaysColumn))
                    selectedColumns.Add($"src.[{optionalColumns.CurrentDaysColumn}] AS CurrentDaysValue");

                if (!string.IsNullOrWhiteSpace(optionalColumns.CurrentDaysHalfColumn))
                    selectedColumns.Add($"src.[{optionalColumns.CurrentDaysHalfColumn}] AS CurrentDaysHalfValue");

                if (!string.IsNullOrWhiteSpace(request.BlockColumn))
                    selectedColumns.Add($"src.[{safeBlock}] AS BlockValue");

                var whereClause = BuildBlockExcludeWhere(request.BlockColumn, request.BlockExcludeValues, "src");

                // Count excluded rows before opening the main reader to avoid "open DataReader" conflict
                var excludedRowCount = await GetExcludedRowCountAsync(conn, safeTable, request.BlockColumn, request.BlockExcludeValues);

                var cteSql = useClientComparison
                    ? $"WITH {BuildClientComparisonCte(safeClientTable, safeClientJoin, safeCensus)}\n"
                    : "";
                var joinSql = useClientComparison
                    ? $"\n{BuildClientComparisonJoin(safeBlock, safeClientJoin)}"
                    : "";

                var sql = $@"{cteSql}SELECT
    ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Validation_Number,
    {string.Join(",\n    ", selectedColumns)}
FROM [{safeTable}] src{joinSql}{(string.IsNullOrWhiteSpace(whereClause) ? "" : $"\n{whereClause}")};";

                var rows = new List<Rule34ValidationRowRecord>();
                using (var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout())
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var validationNumber = Convert.ToInt32(reader.GetInt64(0));
                        var firstDay = ParseNullableDate(reader[1]);
                        var lastDay = ParseNullableDate(reader[2]);
                        var censusDate = ParseNullableDate(reader[3]);
                        var storedCurrentDays = TryGetValue(reader, "CurrentDaysValue", ParseNullableInt);
                        var storedCurrentDaysHalf = TryGetValue(reader, "CurrentDaysHalfValue", ParseNullableDecimal);
                        var wholeDays = storedCurrentDays ?? ComputeNotebookDaySpan(firstDay, lastDay);
                        var halfDays = storedCurrentDaysHalf ?? ComputeNotebookHalfDaySpan(wholeDays);
                        var useSqlDayValues = storedCurrentDays.HasValue && storedCurrentDaysHalf.HasValue;
                        var computedDate = useSqlDayValues
                            ? ComputePreparedCensusDateFromSqlDayValues(firstDay, wholeDays, halfDays)
                            : ComputePreparedCensusDate(firstDay, halfDays);
                        var actualCensusDate = ComputeActualCensusDate(computedDate, holidayLookup);
                        var dayStatus = GetDayStatus(computedDate, actualCensusDate, holidayLookup);
                        var comparisonResult = !actualCensusDate.HasValue || !censusDate.HasValue ||
                                               actualCensusDate.Value.Date != censusDate.Value.Date;
                        var workingDayDiff = comparisonResult
                            ? CountWorkingDaysApart(censusDate, actualCensusDate, holidayLookup)
                            : 0;
                        var withinTolerance = comparisonResult && workingDayDiff <= 2;
                        var toleranceNote = withinTolerance
                            ? BuildToleranceNote(censusDate, actualCensusDate, workingDayDiff, holidayLookup)
                            : "";

                        var blockValue = !string.IsNullOrWhiteSpace(request.BlockColumn)
                            ? ReadStringColumn(reader, "BlockValue")
                            : "";

                        rows.Add(new Rule34ValidationRowRecord
                        {
                            ValidationNumber = validationNumber,
                            FirstDayValue = FormatDate(firstDay),
                            LastDayValue = FormatDate(lastDay),
                            CurrentDays = wholeDays,
                            CurrentDaysHalf = halfDays,
                            ComputedCensusDate = FormatDate(computedDate),
                            ActualCensusDate = FormatDate(actualCensusDate),
                            CensusDateValue = FormatDate(censusDate),
                            DayStatus = dayStatus,
                            ComparisonResult = comparisonResult,
                            DateMatch = !comparisonResult || withinTolerance,
                            ValidationStatus = !comparisonResult
                                ? "PASS (FALSE - MATCH)"
                                : withinTolerance
                                    ? "PASS (TOLERANCE)"
                                    : "FAIL (TRUE - MISMATCH)",
                            WorkingDayDiff = workingDayDiff,
                            WithinTolerance = withinTolerance,
                            ToleranceNote = toleranceNote,
                            BlockValue = blockValue ?? ""
                        });
                    }
                }

                var passCount = rows.Count(r => r.DateMatch);
                var failCount = rows.Count - passCount;
                var tolerancePassCount = rows.Count(r => r.WithinTolerance);
                var total = rows.Count;
                var holidayCount = rows.Count(r => r.DayStatus.StartsWith("SA Public Holiday", StringComparison.OrdinalIgnoreCase));
                var weekendCount = rows.Count(r =>
                    r.DayStatus.Contains("Saturday", StringComparison.OrdinalIgnoreCase) ||
                    r.DayStatus.Contains("Sunday", StringComparison.OrdinalIgnoreCase));

                var summary = new Rule34ValidationSummary
                {
                    Success = true,
                    TotalValidated = total,
                    PassCount = passCount,
                    FailCount = failCount,
                    ExceptionRate = total > 0 ? Math.Round((decimal)failCount / total * 100m, 2) : 0,
                    Status = failCount == 0 ? "PASS" : "FAIL",
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Database = request.Database,
                    TableName = request.TableName,
                    ClientTableName = useClientComparison ? request.ClientTableName : "",
                    FirstDayColumn = request.FirstDayColumn,
                    LastDayColumn = request.LastDayColumn,
                    CensusDateColumn = request.CensusDateColumn,
                    ClientJoinColumn = useClientComparison ? request.ClientJoinColumn : "",
                    StartYear = request.StartYear,
                    EndYear = request.EndYear,
                    HolidayYearRange = $"{request.StartYear}-{request.EndYear}",
                    HolidayCount = holidayCount,
                    WeekendCount = weekendCount,
                    TolerancePassCount = tolerancePassCount,
                    ClientId = request.ClientId,
                    BlockColumn = request.BlockColumn ?? "",
                    BlockExcludeValues = request.BlockExcludeValues ?? "",
                    ExcludedRowCount = excludedRowCount,
                    Holidays = holidays,
                    ValidationRows = rows,
                    Exceptions = rows.Where(r => !r.DateMatch).ToList(),
                    ToleranceExceptions = rows.Where(r => r.WithinTolerance).ToList()
                };

                if (request.ClientId > 0)
                {
                    await using var systemConnection = await OpenSystemConnectionAsync();
                    var systemUserId = await GetSystemUserIdByEmailAsync(systemConnection, userEmail);
                    if (!systemUserId.HasValue)
                        throw new InvalidOperationException("Current user is not available in the system database.");

                    await EnsureClientNotArchivedAsync(systemConnection, request.ClientId);

                    await ClearRuleSignoffsAsync(systemConnection, request.ClientId, 34);
                    await MarkPreviousRunsHistoricalAsync(systemConnection, request.ClientId, 34);

                    var runId = await InsertValidationRunAsync(systemConnection, request, summary, systemUserId.Value, userName);
                    summary.SavedRunId = runId;

                    await using var update = systemConnection.CreateConfiguredCommand();
                    update.CommandText = @"
UPDATE dbo.ValidationRuns
SET ResultsJSON = @ResultsJSON
WHERE RunID = @RunID;";
                    update.Parameters.AddWithValue("@RunID", runId);
                    update.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
                    await update.ExecuteNonQueryAsync();
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule34ValidationSummary
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

        public async Task<Rule34WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
        {
            await using var connection = await OpenSystemConnectionAsync();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = includeSummary ? @"
SELECT TOP 1
    vr.RunID,
    vr.ClientID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS TableName,
    ISNULL(vr.StudColumn, '') AS FirstDayColumn,
    ISNULL(vr.DeceasedColumn, '') AS LastDayColumn,
    ISNULL(vr.DeceasedTable, '') AS CensusDateColumn,
    ISNULL(vr.Status, '') AS Status,
    ISNULL(vr.LastEditedByUserName, '') AS LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 34
ORDER BY vr.IsCurrent DESC, vr.RunTimestamp DESC, vr.RunID DESC;"
            : @"
SELECT TOP 1
    vr.RunID,
    vr.ClientID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS TableName,
    ISNULL(vr.StudColumn, '') AS FirstDayColumn,
    ISNULL(vr.DeceasedColumn, '') AS LastDayColumn,
    ISNULL(vr.DeceasedTable, '') AS CensusDateColumn,
    ISNULL(vr.Status, '') AS Status,
    ISNULL(vr.LastEditedByUserName, '') AS LastEditedByUserName,
    vr.LastEditedAt
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 34
ORDER BY vr.IsCurrent DESC, vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = includeSummary && !reader.IsDBNull(11)
                ? DeserializeSummary(reader.GetString(11))
                : null;
            if (summary != null)
            {
                ApplyBrowserPreview(summary);
            }
            var workspace = new Rule34WorkspaceStateViewModel
            {
                RunId = reader.GetInt32(0),
                ClientId = reader.GetInt32(1),
                Server = reader.GetString(2),
                Database = reader.GetString(3),
                Driver = "ODBC Driver 17 for SQL Server",
                TableName = reader.GetString(4),
                ClientTableName = summary?.ClientTableName ?? "",
                FirstDayColumn = reader.GetString(5),
                LastDayColumn = reader.GetString(6),
                CensusDateColumn = reader.GetString(7),
                ClientJoinColumn = summary?.ClientJoinColumn ?? "",
                StartYear = summary?.StartYear ?? DateTime.Now.Year,
                EndYear = summary?.EndYear ?? DateTime.Now.Year,
                BlockColumn = summary?.BlockColumn ?? "",
                BlockExcludeValues = !string.IsNullOrWhiteSpace(summary?.BlockExcludeValues) ? summary!.BlockExcludeValues : "5, 5F, 8F, 8G, R0, R1, R2",
                CurrentStatus = reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary = summary
            };

            await reader.CloseAsync();

            int? currentUserId = null;
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                    workspace.CurrentUserEngagementRole =
                        await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? "";
            }

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            var currentRoleSignoff = signoffs.FirstOrDefault(s =>
                HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff = currentRoleSignoff != null;
            workspace.CurrentUserSignoffComment = currentRoleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            if (string.IsNullOrWhiteSpace(workspace.CurrentStatus))
                workspace.CurrentStatus = workspace.Summary?.Status ?? "";

            return workspace;
        }

        public async Task<Rule34RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 34;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            var viewModel = new Rule34RunReviewViewModel
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

            int? currentUserId = null;
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                    viewModel.CurrentUserEngagementRole = await GetEngagementRoleAsync(connection, viewModel.ClientId, currentUserId.Value) ?? "";
            }

            viewModel.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            viewModel.HasDataAnalystSignoff = viewModel.Signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return viewModel;
        }

        public async Task<Rule34WorkspaceSaveResult> SaveWorkspaceAsync(Rule34ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule34WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run validation before saving the workspace."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule34WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "The saved workspace could not be found for this engagement."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, request.ClientId);

                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                {
                    return new Rule34WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Current user is not available in the system database."
                    };
                }

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash = await GetValidationRecordHashAsync(connection, request.RunId.Value);
                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer = @HemisServer,
    AuditDatabase = @AuditDatabase,
    StudTable = @StudTable,
    DeceasedTable = @DeceasedTable,
    StudColumn = @StudColumn,
    DeceasedColumn = @DeceasedColumn,
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
                command.Parameters.AddWithValue("@StudTable", request.TableName);
                command.Parameters.AddWithValue("@DeceasedTable", request.CensusDateColumn);
                command.Parameters.AddWithValue("@StudColumn", request.FirstDayColumn);
                command.Parameters.AddWithValue("@DeceasedColumn", request.LastDayColumn);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule34|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.TableName}|{request.ClientTableName}|{request.FirstDayColumn}|{request.LastDayColumn}|{request.CensusDateColumn}|{request.ClientJoinColumn}|{request.BlockColumn}|{request.StartYear}|{request.EndYear}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule34WorkspaceSaveResult
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
                return new Rule34WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule34WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                {
                    return new Rule34WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Current user is not available in the system database."
                    };
                }

                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule34WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash = await GetValidationRecordHashAsync(connection, runId);
                await using (var markEdit = connection.CreateConfiguredCommand())
                {
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
                    markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule34|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                    await markEdit.ExecuteNonQueryAsync();
                }

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule34WorkspaceSaveResult
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
                return new Rule34WorkspaceSaveResult
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

        public async Task<string> GenerateSqlAsync(Rule34ValidationRequest request)
        {
            ValidateRequest(request);

            var holidays = await FetchHolidaysAsync(request.StartYear, request.EndYear);
            var holidayValues = holidays.Any()
                ? string.Join(",\n", holidays.Select(h => $"    (CAST('{h.Date}' AS date), N'{EscapeSqlString(h.Name)}')"))
                : "    (CAST('1900-01-01' AS date), N'No Holiday Data')";

            var useClientComparison = UsesClientComparison(request);
            var table = Sanitise(request.TableName);
            var clientTable = Sanitise(request.ClientTableName);
            var firstDay = Sanitise(request.FirstDayColumn);
            var lastDay = Sanitise(request.LastDayColumn);
            var censusDate = Sanitise(request.CensusDateColumn);
            var clientJoin = Sanitise(request.ClientJoinColumn);
            var blockColumn = Sanitise(request.BlockColumn);
            var firstDaySql = BuildNotebookSqlDateParseExpression(firstDay, "src");
            var lastDaySql = BuildNotebookSqlDateParseExpression(lastDay, "src");
            var censusDateSql = useClientComparison
                ? BuildNotebookSqlDateParseExpression(censusDate, "cmp")
                : BuildNotebookSqlDateParseExpression(censusDate, "src");
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            var optionalColumns = await GetOptionalCurrentDayColumnsAsync(conn, table);
            var currentDaysSql = !string.IsNullOrWhiteSpace(optionalColumns.CurrentDaysColumn)
                ? BuildSqlIntParseExpression(optionalColumns.CurrentDaysColumn!, "src")
                : null;
            var currentDaysHalfSql = !string.IsNullOrWhiteSpace(optionalColumns.CurrentDaysHalfColumn)
                ? BuildSqlDecimalParseExpression(optionalColumns.CurrentDaysHalfColumn!, "src")
                : null;

            var blockWhere = BuildBlockExcludeWhere(request.BlockColumn, request.BlockExcludeValues, "src");
            var blockNote = string.IsNullOrWhiteSpace(blockWhere) ? "" :
                $"\n-- Block filter: rows where [{request.BlockColumn}] IN ({request.BlockExcludeValues}) are excluded";
            var comparisonNote = useClientComparison
                ? $"\n-- Comparison table: [{request.ClientTableName}]\n-- Join: source [{request.BlockColumn}] -> client [{request.ClientJoinColumn}]\n-- Client census date column: [{request.CensusDateColumn}]"
                : "";
            var comparisonPurpose = useClientComparison
                ? "client CURRENT_CENSUS date"
                : "stored census date";
            var comparisonResultLabel = useClientComparison
                ? "Client_CURRENT_CENSUS"
                : "Stored_CENSUS_DATE";
            var comparisonCte = useClientComparison
                ? $"{BuildClientComparisonCte(clientTable, clientJoin, censusDate)},\n"
                : "";
            var comparisonJoinSql = useClientComparison
                ? $"\n{BuildClientComparisonJoin(blockColumn, clientJoin)}"
                : "";

            return $@"-- ============================================================================
-- HEMIS 2025 - RULE 34: CENSUS DATE VALIDATION
-- ============================================================================
-- Purpose: Compare the adjusted actual census date against the {comparisonPurpose}.
-- NOTEBOOK FORMULA:
--   c_Days = (Last_Day_Class - First_Day_Class).dt.days
--   c_Days_2 = c_Days / 2
--   c_Census_Date_Prep = First_Day_Class + timedelta(days=c_Days_2)
--   c_ACTUAL_CENSUS_DATE = next working day when the prepared date falls on
--                          a weekend or South African public holiday
--   Comparison_Result = c_ACTUAL_CENSUS_DATE <> {comparisonResultLabel}
-- PASS: FALSE (dates match)
-- FAIL: TRUE (dates mismatch)
-- Dynamic holiday year range: {request.StartYear} - {request.EndYear}{blockNote}{comparisonNote}
-- ============================================================================

IF OBJECT_ID('tempdb..#Rule34Holidays') IS NOT NULL DROP TABLE #Rule34Holidays;
IF OBJECT_ID('tempdb..#Rule34Validation') IS NOT NULL DROP TABLE #Rule34Validation;

CREATE TABLE #Rule34Holidays
(
    HolidayDate date NOT NULL,
    HolidayName nvarchar(255) NOT NULL
);

INSERT INTO #Rule34Holidays (HolidayDate, HolidayName)
VALUES
{holidayValues};

WITH {comparisonCte}BaseData AS
(
    SELECT
        ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Validation_Number,
        {firstDaySql} AS First_Day_Class,
        {lastDaySql} AS Last_Day_Class,
        {censusDateSql} AS Midpoint_CENSUS_DATE,
        {currentDaysSql ?? "CAST(NULL AS int)"} AS Raw_c_Days,
        {currentDaysHalfSql ?? "CAST(NULL AS decimal(18,4))"} AS Raw_c_Days_2
    FROM [{table}] src{comparisonJoinSql}{(string.IsNullOrWhiteSpace(blockWhere) ? "" : $"\n{blockWhere}")}
),
Prepared AS
(
    SELECT
        Validation_Number,
        First_Day_Class,
        Last_Day_Class,
        Midpoint_CENSUS_DATE,
        Raw_c_Days,
        Raw_c_Days_2,
        CASE
            WHEN Raw_c_Days IS NOT NULL THEN Raw_c_Days
            WHEN First_Day_Class IS NULL OR Last_Day_Class IS NULL THEN NULL
            ELSE CAST(FLOOR(DATEDIFF_BIG(SECOND, First_Day_Class, Last_Day_Class) / 86400.0) AS int)
        END AS c_Days
    FROM BaseData
),
Calculated AS
(
    SELECT
        Validation_Number,
        First_Day_Class,
        Last_Day_Class,
        Midpoint_CENSUS_DATE,
        c_Days,
        CASE
            WHEN Raw_c_Days_2 IS NOT NULL THEN Raw_c_Days_2
            WHEN c_Days IS NULL THEN NULL
            ELSE CAST(c_Days AS decimal(18, 1)) / 2.0
        END AS c_Days_2,
        CASE
            WHEN First_Day_Class IS NULL OR c_Days IS NULL THEN NULL
            WHEN Raw_c_Days IS NOT NULL AND Raw_c_Days_2 IS NOT NULL
                THEN DATEADD(
                    DAY,
                    CASE
                        WHEN c_Days % 2 = 0 THEN CAST(Raw_c_Days_2 - 1 AS int)
                        ELSE CAST(Raw_c_Days_2 AS int)
                    END,
                    First_Day_Class
                )
            ELSE DATEADD(SECOND, CAST((CAST(c_Days AS decimal(18, 1)) / 2.0) * 86400 AS int), First_Day_Class)
        END AS c_Census_Date_Prep
    FROM Prepared
),
OffsetDays AS
(
    SELECT 0 AS OffsetValue
    UNION ALL SELECT 1
    UNION ALL SELECT 2
    UNION ALL SELECT 3
    UNION ALL SELECT 4
    UNION ALL SELECT 5
    UNION ALL SELECT 6
    UNION ALL SELECT 7
    UNION ALL SELECT 8
    UNION ALL SELECT 9
    UNION ALL SELECT 10
    UNION ALL SELECT 11
    UNION ALL SELECT 12
    UNION ALL SELECT 13
    UNION ALL SELECT 14
),
ActualDates AS
(
    SELECT
        c.Validation_Number,
        c.First_Day_Class,
        c.Last_Day_Class,
        c.Midpoint_CENSUS_DATE,
        c.c_Days,
        c.c_Days_2,
        c.c_Census_Date_Prep,
        actualDate.c_ACTUAL_CENSUS_DATE
    FROM Calculated c
    OUTER APPLY
    (
        SELECT TOP 1
            DATEADD(DAY, o.OffsetValue, CAST(c.c_Census_Date_Prep AS date)) AS c_ACTUAL_CENSUS_DATE
        FROM OffsetDays o
        WHERE c.c_Census_Date_Prep IS NOT NULL
          AND DATENAME(WEEKDAY, DATEADD(DAY, o.OffsetValue, CAST(c.c_Census_Date_Prep AS date))) NOT IN ('Saturday', 'Sunday')
          AND NOT EXISTS (
              SELECT 1
              FROM #Rule34Holidays h
              WHERE h.HolidayDate = DATEADD(DAY, o.OffsetValue, CAST(c.c_Census_Date_Prep AS date))
          )
        ORDER BY o.OffsetValue
    ) actualDate
)
SELECT
    Validation_Number,
    First_Day_Class,
    Last_Day_Class,
    c_Days AS Current_days,
    c_Days_2 AS Current_days_2,
    c_Census_Date_Prep,
    c_ACTUAL_CENSUS_DATE,
    Midpoint_CENSUS_DATE AS [{comparisonResultLabel}],
    CASE
        WHEN c_Census_Date_Prep IS NULL THEN 'NULL Date'
        WHEN EXISTS (
            SELECT 1
            FROM #Rule34Holidays h
            WHERE h.HolidayDate = CAST(c_Census_Date_Prep AS date)
        ) THEN 'SA Public Holiday: ' + (
            SELECT TOP 1 h.HolidayName
            FROM #Rule34Holidays h
            WHERE h.HolidayDate = CAST(c_Census_Date_Prep AS date)
        ) + CASE
            WHEN c_ACTUAL_CENSUS_DATE IS NOT NULL AND CAST(c_ACTUAL_CENSUS_DATE AS date) <> CAST(c_Census_Date_Prep AS date)
                THEN ' -> shifted to ' + CONVERT(varchar(10), c_ACTUAL_CENSUS_DATE, 23)
            ELSE ''
        END
        WHEN DATENAME(WEEKDAY, c_Census_Date_Prep) = 'Saturday' THEN 'Falls on Saturday' + CASE
            WHEN c_ACTUAL_CENSUS_DATE IS NOT NULL AND CAST(c_ACTUAL_CENSUS_DATE AS date) <> CAST(c_Census_Date_Prep AS date)
                THEN ' -> shifted to ' + CONVERT(varchar(10), c_ACTUAL_CENSUS_DATE, 23)
            ELSE ''
        END
        WHEN DATENAME(WEEKDAY, c_Census_Date_Prep) = 'Sunday' THEN 'Falls on Sunday' + CASE
            WHEN c_ACTUAL_CENSUS_DATE IS NOT NULL AND CAST(c_ACTUAL_CENSUS_DATE AS date) <> CAST(c_Census_Date_Prep AS date)
                THEN ' -> shifted to ' + CONVERT(varchar(10), c_ACTUAL_CENSUS_DATE, 23)
            ELSE ''
        END
        ELSE 'Weekday'
    END AS step4_Weekend_Note,
    CASE
        WHEN c_ACTUAL_CENSUS_DATE IS NULL OR Midpoint_CENSUS_DATE IS NULL THEN CAST(1 AS bit)
        WHEN CAST(c_ACTUAL_CENSUS_DATE AS date) <> CAST(Midpoint_CENSUS_DATE AS date) THEN CAST(1 AS bit)
        ELSE CAST(0 AS bit)
    END AS Comparison_Result,
    CASE
        WHEN c_ACTUAL_CENSUS_DATE IS NULL OR Midpoint_CENSUS_DATE IS NULL THEN 'FAIL (TRUE - MISMATCH)'
        WHEN CAST(c_ACTUAL_CENSUS_DATE AS date) <> CAST(Midpoint_CENSUS_DATE AS date) THEN 'FAIL (TRUE - MISMATCH)'
        ELSE 'PASS (FALSE - MATCH)'
    END AS Validation_Status
INTO #Rule34Validation
FROM ActualDates;

SELECT
    COUNT(*) AS Total_Validated,
    SUM(CASE WHEN Validation_Status = 'PASS (FALSE - MATCH)' THEN 1 ELSE 0 END) AS Pass_Count,
    SUM(CASE WHEN Validation_Status = 'FAIL (TRUE - MISMATCH)' THEN 1 ELSE 0 END) AS Fail_Count,
    CAST(SUM(CASE WHEN Validation_Status = 'FAIL (TRUE - MISMATCH)' THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*), 0) AS DECIMAL(10,2)) AS Exception_Rate_Percent,
    SUM(CASE WHEN step4_Weekend_Note LIKE 'SA Public Holiday:%' THEN 1 ELSE 0 END) AS Holiday_Count,
    SUM(CASE WHEN step4_Weekend_Note LIKE 'Falls on Saturday%' OR step4_Weekend_Note LIKE 'Falls on Sunday%' THEN 1 ELSE 0 END) AS Weekend_Count
FROM #Rule34Validation;

SELECT *
FROM #Rule34Validation
ORDER BY Validation_Number;

SELECT *
FROM #Rule34Validation
WHERE Validation_Status = 'FAIL (TRUE - MISMATCH)'
ORDER BY Validation_Number;

DROP TABLE #Rule34Validation;
DROP TABLE #Rule34Holidays;
-- ============================================================================
-- END OF RULE 34 CENSUS DATE VALIDATION
-- ============================================================================
";
        }

        private static string BuildBlockExcludeWhere(string? blockColumn, string? blockExcludeValues, string? tableAlias = null)
        {
            if (string.IsNullOrWhiteSpace(blockColumn) || string.IsNullOrWhiteSpace(blockExcludeValues))
                return "";

            var values = blockExcludeValues
                .Split(',')
                .Select(v => v.Trim().Replace("'", "''"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!values.Any()) return "";

            var inList = string.Join(", ", values.Select(v => $"'{v.ToUpperInvariant()}'"));
            return $"WHERE {BuildNormalizedTextSql(blockColumn, tableAlias)} NOT IN ({inList})";
        }

        private async Task<int> GetExcludedRowCountAsync(SqlConnection conn, string safeTable, string? blockColumn, string? blockExcludeValues)
        {
            if (string.IsNullOrWhiteSpace(blockColumn) || string.IsNullOrWhiteSpace(blockExcludeValues))
                return 0;

            var values = blockExcludeValues
                .Split(',')
                .Select(v => v.Trim().Replace("'", "''"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!values.Any()) return 0;

            var inList = string.Join(", ", values.Select(v => $"'{v.ToUpperInvariant()}'"));
            var sql = $"SELECT COUNT(*) FROM [{safeTable}] WHERE {BuildNormalizedTextSql(blockColumn)} IN ({inList});";
            using var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout();
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> InsertValidationRunAsync(
            SqlConnection connection,
            Rule34ValidationRequest request,
            Rule34ValidationSummary summary,
            int systemUserId,
            string? userName)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(ClientID, UserID, HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
 RuleNumber, RuleName, Status, RunTimestamp, TotalRecords, PassCount, FailCount, ExceptionRate,
 ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, IsCurrent)
VALUES
(@ClientID, @UserID, @HemisServer, @AuditDatabase, @StudTable, @DeceasedTable, @StudColumn, @DeceasedColumn,
 @RuleNumber, @RuleName, @Status, GETDATE(), @TotalRecords, @PassCount, @FailCount, @ExceptionRate,
 @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.TableName);
            command.Parameters.AddWithValue("@DeceasedTable", request.CensusDateColumn);
            command.Parameters.AddWithValue("@StudColumn", request.FirstDayColumn);
            command.Parameters.AddWithValue("@DeceasedColumn", request.LastDayColumn);
            command.Parameters.AddWithValue("@RuleNumber", 34);
            command.Parameters.AddWithValue("@RuleName", "Census Date Validation");
            command.Parameters.AddWithValue("@Status", summary.FailCount == 0 ? "Pass" : "Fail");
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary.Exceptions)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? DBNull.Value);
            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 34);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
            var value = await command.ExecuteScalarAsync();
            var runId = Convert.ToInt32(value);

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET RecordHash = @RecordHash
WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule34|{runId}|{request.ClientId}|{systemUserId}|{summary.Status}|{summary.TotalValidated}|{summary.PassCount}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{request.TableName}|{request.ClientTableName}|{request.BlockColumn}|{request.ClientJoinColumn}|{request.CensusDateColumn}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();
            return runId;
        }

        private async Task<List<Rule34HolidayItemViewModel>> FetchHolidaysAsync(int startYear, int endYear)
        {
            var all = new List<Rule34HolidayItemViewModel>();
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            for (var year = startYear; year <= endYear; year++)
            {
                var yearItems = new List<Rule34HolidayItemViewModel>();

                try
                {
                    var url = $"https://date.nager.at/api/v3/PublicHolidays/{year}/ZA";
                    using var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var payload = await response.Content.ReadAsStringAsync();
                        var apiRows = JsonConvert.DeserializeObject<List<NagerHolidayDto>>(payload) ?? new List<NagerHolidayDto>();
                        yearItems.AddRange(apiRows
                            .Where(r => !string.IsNullOrWhiteSpace(r.Date))
                            .Select(r => new Rule34HolidayItemViewModel
                            {
                                Date = r.Date!,
                                Name = !string.IsNullOrWhiteSpace(r.LocalName) ? r.LocalName! : (r.Name ?? ""),
                                Source = "Nager.Date API"
                            }));
                    }
                }
                catch
                {
                    // fall back to fixed holidays below
                }

                if (!yearItems.Any())
                {
                    foreach (var (month, day, name) in GetFallbackHolidayDefinitions())
                    {
                        yearItems.Add(new Rule34HolidayItemViewModel
                        {
                            Date = new DateOnly(year, month, day).ToString("yyyy-MM-dd"),
                            Name = name,
                            Source = "Fallback Fixed Dates"
                        });
                    }
                }

                all.AddRange(yearItems);
            }

            return all
                .GroupBy(h => h.Date, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(h => h.Date, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<(int Month, int Day, string Name)> GetFallbackHolidayDefinitions()
        {
            yield return (1, 1, "New Year's Day");
            yield return (3, 21, "Human Rights Day");
            yield return (4, 27, "Freedom Day");
            yield return (5, 1, "Workers' Day");
            yield return (6, 16, "Youth Day");
            yield return (8, 9, "National Women's Day");
            yield return (9, 24, "Heritage Day");
            yield return (12, 16, "Day of Reconciliation");
            yield return (12, 25, "Christmas Day");
            yield return (12, 26, "Day of Goodwill");
        }

        private static void ValidateRequest(Rule34ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.TableName))
                throw new InvalidOperationException("Table is required.");
            if (string.IsNullOrWhiteSpace(request.FirstDayColumn))
                throw new InvalidOperationException("First day column is required.");
            if (string.IsNullOrWhiteSpace(request.LastDayColumn))
                throw new InvalidOperationException("Last day column is required.");
            if (string.IsNullOrWhiteSpace(request.CensusDateColumn))
                throw new InvalidOperationException("Census date column is required.");
            if (!string.IsNullOrWhiteSpace(request.ClientTableName) ||
                !string.IsNullOrWhiteSpace(request.ClientJoinColumn))
            {
                if (string.IsNullOrWhiteSpace(request.ClientTableName))
                    throw new InvalidOperationException("Client census table is required.");
                if (string.IsNullOrWhiteSpace(request.ClientJoinColumn))
                    throw new InvalidOperationException("Client join column is required.");
                if (string.IsNullOrWhiteSpace(request.BlockColumn))
                    throw new InvalidOperationException("Source block/join column is required for the client census comparison.");
            }
            if (request.StartYear <= 0 || request.EndYear <= 0 || request.StartYear > request.EndYear)
                throw new InvalidOperationException("Select a valid holiday year range.");
        }

        private static DateTime? ParseNullableDate(object value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            if (value is DateTime dt)
                return dt;

            var raw = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var normalized = NormalizeNotebookDateText(raw);

            foreach (var format in NotebookDateFormats)
            {
                if (DateTime.TryParseExact(normalized, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                    return exact;

                if (DateTime.TryParseExact(normalized, format, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out exact))
                    return exact;
            }

            if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;

            if (DateTime.TryParse(normalized, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out parsed))
                return parsed;

            return null;
        }

        private static readonly string[] NotebookDateFormats =
        {
            "dd MMM yy",
            "d MMM yy",
            "dd MMM yyyy",
            "d MMM yyyy",
            "dd MMMM yy",
            "d MMMM yy",
            "dd MMMM yyyy",
            "d MMMM yyyy",
            "dd-MMM-yy",
            "d-MMM-yy",
            "dd-MMM-yyyy",
            "d-MMM-yyyy",
            "dd-MMMM-yy",
            "d-MMMM-yy",
            "dd-MMMM-yyyy",
            "d-MMMM-yyyy",
            "yyyy-MM-dd",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy/MM/dd",
            "MM/dd/yyyy",
            "dd/MM/yyyy"
        };

        private static string NormalizeNotebookDateText(string value)
        {
            var normalized = value.Trim();
            if (normalized.Length == 0)
                return normalized;

            normalized = normalized.Replace('\u00A0', ' ');
            normalized = Regex.Replace(normalized, @"\s+", " ");
            normalized = Regex.Replace(normalized, @"\bSept\b", "Sep", RegexOptions.IgnoreCase);
            return normalized;
        }

        private static string BuildSqlIdentifier(string columnName, string? tableAlias = null)
        {
            var safeColumn = Sanitise(columnName);
            return string.IsNullOrWhiteSpace(tableAlias)
                ? $"[{safeColumn}]"
                : $"{tableAlias}.[{safeColumn}]";
        }

        private static string BuildNormalizedTextSql(string columnName, string? tableAlias = null)
        {
            var columnRef = BuildSqlIdentifier(columnName, tableAlias);
            return $"UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), {columnRef}))))";
        }

        private static string BuildNotebookSqlDateParseExpression(string columnName, string? tableAlias = null)
        {
            var escapedColumn = BuildSqlIdentifier(columnName, tableAlias);
            var textValue = $"NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(255), {escapedColumn}))), '')";
            var normalizedText = $"REPLACE(REPLACE({textValue}, 'Sept', 'Sep'), '-', ' ')";

            return $@"COALESCE(
        TRY_CONVERT(datetime, {escapedColumn}),
        TRY_PARSE({normalizedText} AS datetime USING 'en-GB'),
        TRY_PARSE({normalizedText} AS datetime USING 'en-US')
    )";
        }

        private static string BuildSqlIntParseExpression(string columnName, string? tableAlias = null)
        {
            var escapedColumn = BuildSqlIdentifier(columnName, tableAlias);
            var textValue = $"NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(255), {escapedColumn}))), '')";

            return $@"COALESCE(
        TRY_CONVERT(int, {textValue}),
        TRY_PARSE({textValue} AS int USING 'en-ZA'),
        TRY_PARSE({textValue} AS int USING 'en-US')
    )";
        }

        private static string BuildSqlDecimalParseExpression(string columnName, string? tableAlias = null)
        {
            var escapedColumn = BuildSqlIdentifier(columnName, tableAlias);
            var textValue = $"NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(255), {escapedColumn}))), '')";
            var normalizedText = $"REPLACE({textValue}, ',', '.')";

            return $@"COALESCE(
        TRY_CONVERT(decimal(18, 4), {textValue}),
        TRY_CONVERT(decimal(18, 4), {normalizedText}),
        TRY_PARSE({textValue} AS decimal(18, 4) USING 'en-ZA'),
        TRY_PARSE({textValue} AS decimal(18, 4) USING 'en-US')
    )";
        }

        private static bool UsesClientComparison(Rule34ValidationRequest request) =>
            UsesClientComparison(request.ClientTableName, request.ClientJoinColumn, request.BlockColumn);

        private static bool UsesClientComparison(string? clientTableName, string? clientJoinColumn, string? sourceBlockColumn) =>
            !string.IsNullOrWhiteSpace(clientTableName) &&
            !string.IsNullOrWhiteSpace(clientJoinColumn) &&
            !string.IsNullOrWhiteSpace(sourceBlockColumn);

        private static string BuildClientComparisonCte(string clientTableName, string clientJoinColumn, string censusDateColumn)
        {
            var joinKeySql = BuildNormalizedTextSql(clientJoinColumn, "cmpBase");
            var parsedCensusDateSql = BuildNotebookSqlDateParseExpression(censusDateColumn, "cmpBase");

            return $@"ClientComparison AS
(
    SELECT
        cmpBase.*,
        ROW_NUMBER() OVER
        (
            PARTITION BY {joinKeySql}
            ORDER BY
                CASE
                    WHEN {joinKeySql} IS NULL OR {joinKeySql} = '' THEN 1
                    ELSE 0
                END,
                CASE
                    WHEN {parsedCensusDateSql} IS NULL THEN 1
                    ELSE 0
                END,
                {parsedCensusDateSql} DESC
        ) AS MatchRank
    FROM [{clientTableName}] cmpBase
)";
        }

        private static string BuildClientComparisonJoin(string sourceBlockColumn, string clientJoinColumn)
        {
            var sourceJoinSql = BuildNormalizedTextSql(sourceBlockColumn, "src");
            var clientJoinSql = BuildNormalizedTextSql(clientJoinColumn, "cmp");

            return $@"LEFT JOIN ClientComparison cmp
    ON cmp.MatchRank = 1
   AND {sourceJoinSql} IS NOT NULL
   AND {sourceJoinSql} <> ''
   AND {sourceJoinSql} = {clientJoinSql}";
        }

        private static int? ComputeNotebookDaySpan(DateTime? firstDay, DateTime? lastDay)
        {
            if (!firstDay.HasValue || !lastDay.HasValue)
                return null;

            return (int)Math.Floor((lastDay.Value - firstDay.Value).TotalDays);
        }

        private static decimal? ComputeNotebookHalfDaySpan(int? wholeDays) =>
            wholeDays.HasValue ? wholeDays.Value / 2m : null;

        private static DateTime? ComputePreparedCensusDate(DateTime? firstDay, decimal? halfDays)
        {
            if (!firstDay.HasValue || !halfDays.HasValue)
                return null;

            return firstDay.Value.AddDays((double)halfDays.Value);
        }

        private static DateTime? ComputePreparedCensusDateFromSqlDayValues(DateTime? firstDay, int? wholeDays, decimal? halfDays)
        {
            if (!firstDay.HasValue || !wholeDays.HasValue || !halfDays.HasValue)
                return null;

            var midpointOffset = wholeDays.Value % 2 == 0
                ? halfDays.Value - 1m
                : halfDays.Value;

            return firstDay.Value.AddDays((double)midpointOffset);
        }

        private static DateTime? ComputeActualCensusDate(DateTime? preparedDate, IReadOnlyDictionary<DateOnly, string> holidays)
        {
            if (!preparedDate.HasValue)
                return null;

            var candidate = preparedDate.Value.Date;
            for (var i = 0; i < 31; i++)
            {
                var day = DateOnly.FromDateTime(candidate);
                var isWeekend = candidate.DayOfWeek == DayOfWeek.Saturday || candidate.DayOfWeek == DayOfWeek.Sunday;
                if (!isWeekend && !holidays.ContainsKey(day))
                    return candidate;

                candidate = candidate.AddDays(1);
            }

            return candidate;
        }

        private static int CountWorkingDaysApart(DateTime? a, DateTime? b, IReadOnlyDictionary<DateOnly, string> holidays)
        {
            if (!a.HasValue || !b.HasValue || a.Value.Date == b.Value.Date) return 0;
            var start = a.Value.Date < b.Value.Date ? a.Value.Date : b.Value.Date;
            var end   = a.Value.Date > b.Value.Date ? a.Value.Date : b.Value.Date;
            var count = 0;
            for (var d = start.AddDays(1); d <= end; d = d.AddDays(1))
            {
                if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday
                    && !holidays.ContainsKey(DateOnly.FromDateTime(d)))
                    count++;
            }
            return count;
        }

        private static string BuildToleranceNote(DateTime? censusDate, DateTime? actualCensusDate, int workingDayDiff, IReadOnlyDictionary<DateOnly, string> holidays)
        {
            if (!censusDate.HasValue || !actualCensusDate.HasValue) return "";
            var start = censusDate.Value.Date < actualCensusDate.Value.Date ? censusDate.Value.Date : actualCensusDate.Value.Date;
            var end   = censusDate.Value.Date > actualCensusDate.Value.Date ? censusDate.Value.Date : actualCensusDate.Value.Date;
            var calDiff = (end - start).Days;
            var skipped = new List<string>();
            for (var d = start.AddDays(1); d < end; d = d.AddDays(1))
            {
                if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
                    skipped.Add($"{d:dd MMM} [{d.DayOfWeek}]");
                else if (holidays.TryGetValue(DateOnly.FromDateTime(d), out var name))
                    skipped.Add($"{d:dd MMM} [Holiday: {name}]");
            }
            var skippedText = skipped.Count > 0 ? $" ({string.Join(", ", skipped)} not counted)" : "";
            return $"{calDiff} calendar day(s) apart; {workingDayDiff} working day(s){skippedText} — within 2-day tolerance";
        }

        private static string GetDayStatus(DateTime? preparedDate, DateTime? actualDate, IReadOnlyDictionary<DateOnly, string> holidays)
        {
            if (!preparedDate.HasValue)
                return "NULL Date";

            var day = DateOnly.FromDateTime(preparedDate.Value);
            var shiftSuffix = actualDate.HasValue && actualDate.Value.Date != preparedDate.Value.Date
                ? $" -> shifted to {actualDate.Value:yyyy-MM-dd}"
                : "";

            if (holidays.TryGetValue(day, out var holidayName))
                return $"SA Public Holiday: {holidayName}{shiftSuffix}";

            return preparedDate.Value.DayOfWeek switch
            {
                DayOfWeek.Saturday => $"Falls on Saturday{shiftSuffix}",
                DayOfWeek.Sunday => $"Falls on Sunday{shiftSuffix}",
                _ => "Weekday"
            };
        }

        private static string FormatDate(DateTime? value) =>
            value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss") : "NULL";

        private static int? ParseNullableInt(object value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                return parsedInt;

            if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDecimal))
                return (int)Math.Round(parsedDecimal, MidpointRounding.AwayFromZero);

            return null;
        }

        private static decimal? ParseNullableDecimal(object value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                return parsed;

            return null;
        }

        private static string ReadStringColumn(SqlDataReader reader, string columnName)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                    return reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
            }
            return "";
        }

        private static T? TryGetValue<T>(SqlDataReader reader, string columnName, Func<object, T?> parser)
            where T : struct
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                    return parser(reader.GetValue(i));
            }

            return null;
        }

        private static string FormatValue(object value) =>
            value switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
            };

        private static string FindFirst(List<string> columns, List<string> dateColumns, string[] exactMatches, string[] containsMatches)
        {
            foreach (var exact in exactMatches)
            {
                var value = columns.FirstOrDefault(c => c.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            foreach (var fragment in containsMatches)
            {
                var value = dateColumns.FirstOrDefault(c => c.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                            ?? columns.FirstOrDefault(c => c.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return dateColumns.FirstOrDefault() ?? columns.FirstOrDefault() ?? "";
        }

        private static async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private async Task<(string? CurrentDaysColumn, string? CurrentDaysHalfColumn)> GetOptionalCurrentDayColumnsAsync(SqlConnection connection, string tableName)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @TableName
ORDER BY ORDINAL_POSITION;";
            command.Parameters.AddWithValue("@TableName", tableName);

            var columns = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                    columns.Add(reader.GetString(0));
            }

            return
            (
                FindOptionalColumn(columns,
                    new[] { "Current_days", "CurrentDays", "c_Days", "C_DAYS" },
                    new[] { "current_days", "currentdays", "c_days" }),
                FindOptionalColumn(columns,
                    new[] { "Current_days_2", "CurrentDays_2", "CurrentDays2", "c_Days_2", "C_DAYS_2" },
                    new[] { "current_days_2", "currentdays_2", "currentdays2", "c_days_2" })
            );
        }

        private static string? FindOptionalColumn(IEnumerable<string> columns, string[] exactMatches, string[] containsMatches)
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

            return null;
        }

        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";

        private async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var countCommand = connection.CreateConfiguredCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCommand.Parameters.AddWithValue("@RunID", runId);
            var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = @"
DELETE FROM dbo.ReviewSignoffs
WHERE RunID = @RunID;";
            deleteCommand.Parameters.AddWithValue("@RunID", runId);
            await deleteCommand.ExecuteNonQueryAsync();

            await using var updateCommand = connection.CreateConfiguredCommand();
            updateCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET Status = 'Needs Review'
WHERE RunID = @RunID;";
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
    CASE WHEN EXISTS (
        SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'DataAnalyst'
    ) THEN 1 ELSE 0 END AS HasDataAnalyst,
    CASE WHEN EXISTS (
        SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Manager'
    ) THEN 1 ELSE 0 END AS HasManager,
    CASE WHEN EXISTS (
        SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Director'
    ) THEN 1 ELSE 0 END AS HasDirector;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return false;

            var hasDataAnalyst = !reader.IsDBNull(0) && reader.GetInt32(0) == 1;
            var hasManager = !reader.IsDBNull(1) && reader.GetInt32(1) == 1;
            var hasDirector = !reader.IsDBNull(2) && reader.GetInt32(2) == 1;
            return hasDataAnalyst && hasManager && hasDirector;
        }

        private async Task SetRunStatusAsync(SqlConnection connection, int runId, string status)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ValidationRuns
SET Status = @Status
WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@Status", status);
            await command.ExecuteNonQueryAsync();
        }

        private async Task SetRunCurrentStateAsync(SqlConnection connection, int runId, bool isCurrent)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ValidationRuns
SET IsCurrent = @IsCurrent
WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@IsCurrent", isCurrent);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<int> ClearRuleSignoffsAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var countCommand = connection.CreateConfiguredCommand();
            countCommand.CommandText = @"
SELECT COUNT(1)
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns vr ON vr.RunID = rs.RunID
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = @RuleNumber;";
            countCommand.Parameters.AddWithValue("@ClientID", clientId);
            countCommand.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = @"
DELETE rs
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns vr ON vr.RunID = rs.RunID
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = @RuleNumber;";
            deleteCommand.Parameters.AddWithValue("@ClientID", clientId);
            deleteCommand.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await deleteCommand.ExecuteNonQueryAsync();

            return existingCount;
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
SELECT COUNT(1)
FROM dbo.ReviewSignoffs
WHERE RunID = @RunID
  AND SignoffRole = @SignoffRole;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private async Task<string?> GetValidationRecordHashAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 RecordHash
FROM dbo.ValidationRuns
WHERE RunID = @RunID;";
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

        private async Task<Rule34ValidationSummary?> GetValidationSummaryAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 ResultsJSON
FROM dbo.ValidationRuns
WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value
                ? null
                : DeserializeSummary(Convert.ToString(value));
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 Status
FROM dbo.Clients
WHERE ClientID = @ClientID;";
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

        private static Rule34ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule34ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyBrowserPreview(Rule34ValidationSummary summary)
        {
            summary.ValidationRows = summary.ValidationRows
                .Take(BrowserPreviewRowLimit)
                .ToList();
            summary.Exceptions = summary.Exceptions
                .Take(BrowserPreviewRowLimit)
                .ToList();
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static string EscapeSqlString(string value) =>
            value.Replace("'", "''");

        private sealed class NagerHolidayDto
        {
            [JsonProperty("date")]
            public string? Date { get; set; }

            [JsonProperty("localName")]
            public string? LocalName { get; set; }

            [JsonProperty("name")]
            public string? Name { get; set; }
        }
    }
}
