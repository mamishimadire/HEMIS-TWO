using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule11Service : IRule11Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private static readonly string[] DefaultQualTypeCodes = ["07", "27", "28", "49", "72", "73", "08", "30", "50", "74", "75"];
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule11Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";

        private static string NormName(string? v)
        {
            if (v == null) return "";
            return Regex.Replace(v.Trim().ToUpperInvariant(), @"\s+", " ");
        }

        private static string NormValue(string? v) =>
            v == null ? "" : v.Trim().ToUpperInvariant();

        private static string DigitsOnly(string? v) =>
            Regex.Replace(NormValue(v), @"\D", "");

        private static string TrimLeadingZeros(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var trimmed = value.TrimStart('0');
            return trimmed.Length == 0 ? "0" : trimmed;
        }

        private static bool HasSameLeadingDigits(string left, string right, int digits) =>
            left.Length >= digits &&
            right.Length >= digits &&
            string.Equals(left[..digits], right[..digits], StringComparison.Ordinal);

        private static string? GetCesmReviewMatchReason(string? qualCode, string? pqmCode)
        {
            var rawQualCode = DigitsOnly(qualCode);
            var rawPqmCode = DigitsOnly(pqmCode);
            if (rawQualCode.Length == 0 || rawPqmCode.Length == 0) return null;

            if (HasSameLeadingDigits(rawQualCode, rawPqmCode, 4))
                return "first 4 digits matched";

            var trimmedQualCode = TrimLeadingZeros(rawQualCode);
            var trimmedPqmCode = TrimLeadingZeros(rawPqmCode);

            if (HasSameLeadingDigits(trimmedQualCode, trimmedPqmCode, 4))
                return "first 4 digits matched after removing leading zeros";

            if (HasSameLeadingDigits(rawQualCode, rawPqmCode, 3))
                return "first 3 digits matched";

            if (HasSameLeadingDigits(trimmedQualCode, trimmedPqmCode, 3))
                return "first 3 digits matched after removing leading zeros";

            return null;
        }

        private static string ClassifyPopulationType(string? qualHeqfType, ISet<string> postgraduateTypeCodes) =>
            postgraduateTypeCodes.Contains(NormValue(qualHeqfType)) ? "Postgraduate" : "Undergraduate";

        private static bool NumericMatch(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return true;
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            if (decimal.TryParse(a.Trim(), out var da) && decimal.TryParse(b.Trim(), out var db))
                return da == db;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHeqfIndicated(string? accreditationRef, IReadOnlyList<string> codes)
        {
            if (string.IsNullOrWhiteSpace(accreditationRef)) return false;
            var upper = accreditationRef.Trim().ToUpperInvariant();
            return codes.Any(code => upper.Contains(code.Trim().ToUpperInvariant()));
        }

        private static List<string> ParseHeqfCodes(string csv) =>
            (csv ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(c => c.Length > 0)
                .ToList();

        private static bool IsMPrefixQualificationCode(string? qualCode) =>
            Regex.IsMatch(NormValue(qualCode), @"^M.{5}$", RegexOptions.CultureInvariant);

        private static (string PopulationType, string Note) ClassifyPopulationTypeFull(
            string? qualCode, string? qualHeqfType, ISet<string> postgraduateTypeCodes, bool useMPrefixPopulationSplit)
        {
            var notes = new List<string>();
            if (useMPrefixPopulationSplit && IsMPrefixQualificationCode(qualCode))
                notes.Add("qualification code matched M_____");
            if (postgraduateTypeCodes.Contains(NormValue(qualHeqfType)))
                notes.Add($"QUAL type {qualHeqfType} is in the configured postgraduate _005 list");
            if (notes.Count > 0)
                return ("Postgraduate", string.Join("; ", notes));
            return ("Undergraduate", "qualification did not match the configured postgraduate rules");
        }

        // ── Internal record types ─────────────────────────────────────────────

        private record QualRecord(
            string QualId,
            string QualName,
            string QualApproval,
            string QualHeqfType,
            string Qual002,
            string Qual053,
            string Qual054,
            string Qual081,
            string Qual082,
            string Qual083,
            string Qual084,
            string Qual085,
            string Qual086,
            string Qual087,
            string Qual088,
            string Qual089,
            string Qual090,
            string? CesmCode);
        private record PqmRow(string? Name, string? HeqfType, string? Code, string? Code1, string? MinTimeTotal, string? WIL, string? Accreditation, string? TotalSubsidy);

        // ── Core validation ───────────────────────────────────────────────────

        private static Rule11ValidationRow ValidateRecord(int rowNo, QualRecord q, List<PqmRow> pqm, ISet<string> postgraduateTypeCodes, IReadOnlyList<string> heqfCodes, bool useMPrefixPopulationSplit)
        {
            var hNorm    = NormName(q.QualName);
            var heqfNorm = NormValue(q.QualHeqfType);
            var codeNorm = NormValue(q.CesmCode);
            var (populationType, populationClassificationNote) = ClassifyPopulationTypeFull(q.QualId, q.QualHeqfType, postgraduateTypeCodes, useMPrefixPopulationSplit);

            Rule11ValidationRow BuildRow(PqmRow? bestPqm, bool nameMatch, bool heqfTypeMatch, bool cesmCodeMatch, bool needsReview, string baseResult, string baseReason)
            {
                var failed = new List<string>();
                bool c2 = heqfTypeMatch;
                bool c3 = false, c4 = false, c5 = false, c6 = false;
                string? expectedHeqf = null;

                if (bestPqm != null)
                {
                    c3 = NumericMatch(q.Qual053, bestPqm.MinTimeTotal);
                    c4 = NumericMatch(q.Qual054, bestPqm.WIL);
                    expectedHeqf = IsHeqfIndicated(bestPqm.Accreditation, heqfCodes) ? "Y" : "N";
                    c5 = string.Equals(NormValue(q.Qual084), expectedHeqf, StringComparison.OrdinalIgnoreCase);
                    c6 = NumericMatch(q.Qual090, bestPqm.TotalSubsidy);
                    if (!c2) failed.Add("C2");
                    if (!c3) failed.Add("C3");
                    if (!c4) failed.Add("C4");
                    if (!c5) failed.Add("C5");
                    if (!c6) failed.Add("C6");
                }

                var finalResult = baseResult == "FAIL" || failed.Count > 0 ? "FAIL" : baseResult;
                var finalReason = failed.Count > 0
                    ? $"{baseReason}. Additional controls failed: {string.Join(", ", failed)}"
                    : baseReason;

                return new Rule11ValidationRow
                {
                    ValidationNumber = rowNo,
                    QualId           = q.QualId,
                    QualName         = q.QualName,
                    QualApproval     = q.QualApproval,
                    QualHeqfType     = q.QualHeqfType,
                    Qual002          = q.Qual002,
                    Qual053          = q.Qual053,
                    Qual054          = q.Qual054,
                    Qual081          = q.Qual081,
                    Qual082          = q.Qual082,
                    Qual083          = q.Qual083,
                    Qual084          = q.Qual084,
                    Qual085          = q.Qual085,
                    Qual086          = q.Qual086,
                    Qual087          = q.Qual087,
                    Qual088          = q.Qual088,
                    Qual089          = q.Qual089,
                    Qual090          = q.Qual090,
                    PopulationType   = populationType,
                    PopulationClassificationNote = populationClassificationNote,
                    CesmCode         = q.CesmCode,
                    PqmName          = bestPqm?.Name?.Trim(),
                    PqmHeqfType      = bestPqm?.HeqfType?.Trim(),
                    PqmCode          = bestPqm?.Code?.Trim(),
                    PqmCesmCode1     = bestPqm?.Code1?.Trim(),
                    PqmMinTimeTotal  = bestPqm?.MinTimeTotal?.Trim(),
                    PqmWIL           = bestPqm?.WIL?.Trim(),
                    PqmAccreditation = bestPqm?.Accreditation?.Trim(),
                    PqmTotalSubsidy  = bestPqm?.TotalSubsidy?.Trim(),
                    NameMatch        = nameMatch,
                    HeqfTypeMatch    = heqfTypeMatch,
                    CesmCodeMatch    = cesmCodeMatch,
                    NeedsReview      = needsReview && finalResult == "PASS",
                    C2_TypeMatch     = c2,
                    C3_MinTimeMatch  = c3,
                    C4_WILMatch      = c4,
                    C5_HeqfMatch     = c5,
                    C5_ExpectedHeqf  = expectedHeqf,
                    C6_SubsidyMatch  = c6,
                    FailedControls   = failed,
                    ValidationResult = finalResult,
                    ExceptionReason  = finalReason
                };
            }

            var nameRows = pqm
                .Where(p => string.Equals(NormName(p.Name), hNorm, StringComparison.Ordinal))
                .ToList();

            if (nameRows.Count == 0)
            {
                return BuildRow(null, false, false, false, false, "FAIL",
                    "Qualification name not found in PQM (Authorised_Qualification_Name)");
            }

            // All three match on same PQM row
            var tripleMatch = nameRows
                .Where(p => string.Equals(NormValue(p.HeqfType), heqfNorm, StringComparison.Ordinal)
                         && string.Equals(NormValue(p.Code), codeNorm, StringComparison.Ordinal))
                .ToList();

            if (tripleMatch.Count > 0)
            {
                return BuildRow(tripleMatch[0], true, true, true, false, "PASS",
                    "All three criteria matched on the same PQM row: qualification name (_003), HEQF type (_005), and CESM code (_006)");
            }

            // Name + HEQF matched
            var heqfMatch = nameRows
                .Where(p => string.Equals(NormValue(p.HeqfType), heqfNorm, StringComparison.Ordinal))
                .ToList();

            if (heqfMatch.Count > 0)
            {
                var reviewMatch = heqfMatch
                    .Select(p => new { Row = p, Reason = GetCesmReviewMatchReason(q.CesmCode, p.Code) })
                    .FirstOrDefault(m => m.Reason != null);

                if (reviewMatch != null)
                {
                    return BuildRow(reviewMatch.Row, true, true, true, true, "PASS",
                        $"Pass - review required: Name and HEQF matched, and the CESM leading digits also matched ({reviewMatch.Reason}) even though the full code differs. CESM._006: '{q.CesmCode}' | PQM CESM_Code: '{reviewMatch.Row.Code?.Trim()}'");
                }

                var best = heqfMatch[0];
                var pqmCodeValues = string.Join(" | ", heqfMatch.Take(3).Select(p => p.Code?.Trim()).Where(v => v != null).Distinct());
                return BuildRow(best, true, true, false, false, "FAIL",
                    $"Name and HEQF matched but CESM code mismatch — CESM._006: '{q.CesmCode}' | PQM CESM_Code: '{pqmCodeValues}'");
            }

            // Name matched, HEQF did not
            var bestName = nameRows[0];
            var pqmHeqfValues = string.Join(" | ", nameRows.Take(3).Select(p => p.HeqfType?.Trim()).Where(v => v != null).Distinct());
            return BuildRow(bestName, true, false, false, false, "FAIL",
                $"Name matched but HEQF_Qual_Type mismatch — QUAL._005: '{q.QualHeqfType}' | PQM HEQF_Qual_Type: '{pqmHeqfValues}'");
        }

        private static List<Rule11ControlSummary> BuildRule11ControlSummaries(
            List<Rule11ValidationRow> rows,
            string qualTable, string qualMinTimeTotalCol, string pqmMinTimeTotalCol,
            string qualMinTimeWilCol, string pqmWilCol,
            string qualHeqfCol, string pqmAccreditationCol,
            string qualTotalSubsidyCol, string pqmTotalSubsidyCol)
        {
            var matched = rows.Where(r => r.NameMatch && r.HeqfTypeMatch).ToList();
            return new List<Rule11ControlSummary>
            {
                new() {
                    ControlId    = "C2",
                    ControlLabel = "Control 2 (5.1.2) — Qualification Type",
                    CriteriaText = $"{qualTable}._005 = PQM.HEQF_Qual_Type",
                    PassCount    = matched.Count(r => r.C2_TypeMatch),
                    FailCount    = matched.Count(r => !r.C2_TypeMatch) + rows.Count(r => !r.NameMatch),
                    Status       = matched.All(r => r.C2_TypeMatch) && rows.All(r => r.NameMatch) ? "PASS" : "FAIL"
                },
                new() {
                    ControlId    = "C3",
                    ControlLabel = "Control 3 (5.1.3) — Minimum Time: Total",
                    CriteriaText = $"{qualTable}.{qualMinTimeTotalCol} = PQM.{pqmMinTimeTotalCol}",
                    PassCount    = matched.Count(r => r.C3_MinTimeMatch),
                    FailCount    = matched.Count(r => !r.C3_MinTimeMatch) + rows.Count(r => !r.NameMatch),
                    Status       = matched.All(r => r.C3_MinTimeMatch) && rows.All(r => r.NameMatch) ? "PASS" : "FAIL"
                },
                new() {
                    ControlId    = "C4",
                    ControlLabel = "Control 4 (5.1.4) — Minimum Time: WIL/Experiential",
                    CriteriaText = $"{qualTable}.{qualMinTimeWilCol} = PQM.{pqmWilCol}",
                    PassCount    = matched.Count(r => r.C4_WILMatch),
                    FailCount    = matched.Count(r => !r.C4_WILMatch) + rows.Count(r => !r.NameMatch),
                    Status       = matched.All(r => r.C4_WILMatch) && rows.All(r => r.NameMatch) ? "PASS" : "FAIL"
                },
                new() {
                    ControlId    = "C5",
                    ControlLabel = "Control 5 (5.1.5) — HEQF/HEQSF Indicator",
                    CriteriaText = $"{qualTable}.{qualHeqfCol} (Y/N) agrees with PQM.{pqmAccreditationCol} indicator codes",
                    PassCount    = matched.Count(r => r.C5_HeqfMatch),
                    FailCount    = matched.Count(r => !r.C5_HeqfMatch) + rows.Count(r => !r.NameMatch),
                    Status       = matched.All(r => r.C5_HeqfMatch) && rows.All(r => r.NameMatch) ? "PASS" : "FAIL"
                },
                new() {
                    ControlId    = "C6",
                    ControlLabel = "Control 6 (5.1.6) — Total Subsidy Units",
                    CriteriaText = $"{qualTable}.{qualTotalSubsidyCol} = PQM.{pqmTotalSubsidyCol}",
                    PassCount    = matched.Count(r => r.C6_SubsidyMatch),
                    FailCount    = matched.Count(r => !r.C6_SubsidyMatch) + rows.Count(r => !r.NameMatch),
                    Status       = matched.All(r => r.C6_SubsidyMatch) && rows.All(r => r.NameMatch) ? "PASS" : "FAIL"
                }
            };
        }

        // ── Public API ────────────────────────────────────────────────────────

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                var connStr = $"Server={server};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT name FROM sys.databases " +
                    "WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name", conn)
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

        public async Task<Rule11TableListResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES " +
                    "WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME", conn)
                    .WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));

                return new Rule11TableListResult
                {
                    Success = true,
                    Tables = tables,
                    AutoQualTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_QUAL", StringComparison.OrdinalIgnoreCase)),
                    AutoCesmTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_CESM", StringComparison.OrdinalIgnoreCase)),
                    AutoPqmTable = tables.FirstOrDefault(t =>
                        t.Equals("PQM", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("PQM", StringComparison.OrdinalIgnoreCase))
                };
            }
            catch (Exception ex)
            {
                return new Rule11TableListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<ColumnListResult> GetColumnsAsync(
            string server, string database, string driver, string tableName, string tableRole)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                    "WHERE TABLE_NAME=@t ORDER BY ORDINAL_POSITION", conn)
                    .WithLargeDataTimeout();
                cmd.Parameters.AddWithValue("@t", tableName);
                using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(0));

                string? autoSelected = tableRole?.ToLowerInvariant() switch
                {
                    "qual_id"        => columns.FirstOrDefault(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_name"      => columns.FirstOrDefault(c => c.Equals("_003", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_approval"  => columns.FirstOrDefault(c => c.Equals("_004", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_heqf_type" => columns.FirstOrDefault(c => c.Equals("_005", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "cesm_id"        => columns.FirstOrDefault(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "cesm_code"      => columns.FirstOrDefault(c => c.Equals("CESM_Code", StringComparison.OrdinalIgnoreCase) || c.Equals("CESM_Code1", StringComparison.OrdinalIgnoreCase) || c.Equals("_006", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_name"       => columns.FirstOrDefault(c => c.Contains("Authorised", StringComparison.OrdinalIgnoreCase) || c.Contains("Qualification_Name", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_heqf_type"  => columns.FirstOrDefault(c => c.Contains("HEQF", StringComparison.OrdinalIgnoreCase) || c.Contains("Qual_Type", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_code"       => columns.FirstOrDefault(c => c.Equals("CESM_Code", StringComparison.OrdinalIgnoreCase) || c.Equals("CESM_Code1", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_min_time_total" => columns.FirstOrDefault(c => c.Equals("_053", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_min_time_wil"   => columns.FirstOrDefault(c => c.Equals("_054", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_heqf"           => columns.FirstOrDefault(c => c.Equals("_084", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_total_subsidy"  => columns.FirstOrDefault(c => c.Equals("_090", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_cesm_code1"     => columns.FirstOrDefault(c => c.Equals("CESM_CODE1", StringComparison.OrdinalIgnoreCase) || c.Equals("CESM_Code1", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_min_time_total" => columns.FirstOrDefault(c => c.Equals("Total2", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_wil"            => columns.FirstOrDefault(c => c.Equals("WIL_EL2", StringComparison.OrdinalIgnoreCase) || c.Contains("WIL", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_accreditation"  => columns.FirstOrDefault(c => c.Contains("Accreditation", StringComparison.OrdinalIgnoreCase) || c.Contains("CHE_HEQC", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_total_subsidy"  => columns.FirstOrDefault(c => c.Equals("Total2", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    _                => columns.FirstOrDefault()
                };

                return new ColumnListResult
                {
                    Success = true,
                    Columns = columns,
                    AutoSelected = autoSelected
                };
            }
            catch (Exception ex)
            {
                return new ColumnListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule11FilterValueResult> GetFilterValuesAsync(string server, string database, string driver, string qualTable, string approvalColumn)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var safeTable = Sanitise(qualTable);
                var safeColumn = Sanitise(approvalColumn);

                using var cmd = new SqlCommand($@"
SELECT
    ISNULL(CAST([{safeColumn}] AS nvarchar(100)), '(NULL)') AS FilterValue,
    COUNT(*) AS RecordCount
FROM [{safeTable}]
WHERE [{safeColumn}] IS NOT NULL
  AND LTRIM(RTRIM(CAST([{safeColumn}] AS nvarchar(100)))) <> ''
GROUP BY [{safeColumn}]
ORDER BY COUNT(*) DESC, FilterValue ASC;", conn)
                    .WithLargeDataTimeout();

                using var reader = await cmd.ExecuteReaderAsync();
                var options = new List<Rule11FilterValueOption>();
                while (await reader.ReadAsync())
                {
                    var value = reader.IsDBNull(0) ? "" : reader.GetValue(0)?.ToString() ?? "";
                    var count = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                    options.Add(new Rule11FilterValueOption
                    {
                        Value = value,
                        Count = count,
                        Label = $"{value} ({count:N0} records)"
                    });
                }

                return new Rule11FilterValueResult
                {
                    Success = true,
                    Options = options,
                    DefaultValue = options.FirstOrDefault()?.Value
                };
            }
            catch (Exception ex)
            {
                return new Rule11FilterValueResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule11VerifyResult> VerifyDataAsync(Rule11VerifyRequest request)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var qt  = Sanitise(request.QualTable);
                var ct  = Sanitise(request.CesmTable);
                var pt  = Sanitise(request.PqmTable);
                var qi  = Sanitise(request.QualIdCol);
                var ci  = Sanitise(request.CesmIdCol);
                var qa  = Sanitise(request.QualApprovalCol);
                var approvalValue = NormalizeFilterValue(request.QualApprovalFilterValue, "A");

                var sql = $@"
SELECT
    (SELECT COUNT(*) FROM [{qt}]) AS QUAL_Total,
    (SELECT COUNT(*) FROM [{ct}]) AS CESM_Total,
    (SELECT COUNT(*) FROM [{pt}]) AS PQM_Total,
    (SELECT COUNT(*) FROM [{qt}] q LEFT JOIN [{ct}] c ON q.[{qi}] = c.[{ci}]
      WHERE UPPER(LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(255))))) = @QualApprovalValue
    ) AS Merged_Total";

                using var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout();
                cmd.Parameters.AddWithValue("@QualApprovalValue", approvalValue);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new Rule11VerifyResult
                    {
                        Success     = true,
                        QualTotal   = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                        CesmTotal   = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                        PqmTotal    = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
                        MergedTotal = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3))
                    };
                }
                return new Rule11VerifyResult { Success = false, Error = "No data returned" };
            }
            catch (Exception ex)
            {
                return new Rule11VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule11ValidationSummary> RunValidationAsync(
            Rule11ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var qt  = Sanitise(request.QualTable);
                var ct  = Sanitise(request.CesmTable);
                var pt  = Sanitise(request.PqmTable);
                var qi  = Sanitise(request.QualIdCol);
                var qn  = Sanitise(request.QualNameCol);
                var qa  = Sanitise(request.QualApprovalCol);
                var qht = Sanitise(request.QualHeqfTypeCol);
                var ci  = Sanitise(request.CesmIdCol);
                var cc  = Sanitise(request.CesmCodeCol);
                var pn  = Sanitise(request.PqmNameCol);
                var pht = Sanitise(request.PqmHeqfTypeCol);
                var pc  = Sanitise(request.PqmCodeCol);
                var approvalValue = NormalizeFilterValue(request.QualApprovalFilterValue, "A");
                var typeCodes = ParseQualTypeCodes(request.QualTypeCodesText);
                var postgraduateTypeCodes = new HashSet<string>(typeCodes, StringComparer.OrdinalIgnoreCase);
                var qmt  = Sanitise(string.IsNullOrWhiteSpace(request.QualMinTimeTotalCol) ? "_053" : request.QualMinTimeTotalCol);
                var qwil = Sanitise(string.IsNullOrWhiteSpace(request.QualMinTimeWilCol)   ? "_054" : request.QualMinTimeWilCol);
                var qhq  = Sanitise(string.IsNullOrWhiteSpace(request.QualHeqfCol)         ? "_084" : request.QualHeqfCol);
                var qsub = Sanitise(string.IsNullOrWhiteSpace(request.QualTotalSubsidyCol) ? "_090" : request.QualTotalSubsidyCol);
                var pc1  = Sanitise(string.IsNullOrWhiteSpace(request.PqmCesmCode1Col)     ? "CESM_CODE1" : request.PqmCesmCode1Col);
                var pmt  = Sanitise(string.IsNullOrWhiteSpace(request.PqmMinTimeTotalCol)  ? "Total2" : request.PqmMinTimeTotalCol);
                var pwil = Sanitise(string.IsNullOrWhiteSpace(request.PqmWilCol)           ? "WIL_EL2" : request.PqmWilCol);
                var pacc = Sanitise(string.IsNullOrWhiteSpace(request.PqmAccreditationCol) ? "CHE_HEQC_Accreditation_Approval_Ref_Nr" : request.PqmAccreditationCol);
                var psub = Sanitise(string.IsNullOrWhiteSpace(request.PqmTotalSubsidyCol)  ? "Total2" : request.PqmTotalSubsidyCol);
                var heqfCodes = ParseHeqfCodes(string.IsNullOrWhiteSpace(request.HeqfIndicatorCodesCsv) ? "H/,HEQF,HEQSF" : request.HeqfIndicatorCodesCsv);
                var useMPrefixPopulationSplit = request.UseMPrefixPopulationSplit || request.ExcludeMPrefixPattern;

                // Load QUAL ⋈ CESM (LEFT JOIN so QUAL rows with no CESM row are included)
                var qualRecords = new List<QualRecord>();
                using (var cmd = new SqlCommand($@"
SELECT q.[{qi}], q.[{qn}], q.[{qa}], q.[{qht}],
       q.[_002], q.[{qmt}], q.[{qwil}], q.[_081], q.[_082], q.[_083],
       q.[{qhq}], q.[_085], q.[_086], q.[_087], q.[_088], q.[_089], q.[{qsub}],
       c.[{cc}]
FROM [{qt}] q
LEFT JOIN [{ct}] c ON q.[{qi}] = c.[{ci}]
WHERE UPPER(LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(255))))) = @QualApprovalValue", conn)
                    .WithLargeDataTimeout())
                {
                    cmd.Parameters.AddWithValue("@QualApprovalValue", approvalValue);
                    using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i)?.ToString() ?? "";
                        qualRecords.Add(new QualRecord(
                            QualId:    S(0),
                            QualName:  S(1),
                            QualApproval:  S(2),
                            QualHeqfType:  S(3),
                            Qual002:   S(4),
                            Qual053:   S(5),
                            Qual054:   S(6),
                            Qual081:   S(7),
                            Qual082:   S(8),
                            Qual083:   S(9),
                            Qual084:   S(10),
                            Qual085:   S(11),
                            Qual086:   S(12),
                            Qual087:   S(13),
                            Qual088:   S(14),
                            Qual089:   S(15),
                            Qual090:   S(16),
                            CesmCode:  r.IsDBNull(17) ? null : r.GetValue(17)?.ToString()));
                    }
                }

                // Load PQM
                var pqm = new List<PqmRow>();
                using (var cmd = new SqlCommand(
                    $"SELECT [{pn}], [{pht}], [{pc}], [{pc1}], [{pmt}], [{pwil}], [{pacc}], [{psub}] FROM [{pt}]", conn).WithLargeDataTimeout())
                using (var r = await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        pqm.Add(new PqmRow(
                            r.IsDBNull(0) ? null : r.GetValue(0)?.ToString(),  // Name
                            r.IsDBNull(1) ? null : r.GetValue(1)?.ToString(),  // HeqfType
                            r.IsDBNull(2) ? null : r.GetValue(2)?.ToString(),  // Code
                            r.IsDBNull(3) ? null : r.GetValue(3)?.ToString(),  // Code1
                            r.IsDBNull(4) ? null : r.GetValue(4)?.ToString(),  // MinTimeTotal
                            r.IsDBNull(5) ? null : r.GetValue(5)?.ToString(),  // WIL
                            r.IsDBNull(6) ? null : r.GetValue(6)?.ToString(),  // Accreditation
                            r.IsDBNull(7) ? null : r.GetValue(7)?.ToString()   // TotalSubsidy
                        ));
                    }
                }

                // Validate in memory
                var validationRows = qualRecords
                    .Select((q, idx) => ValidateRecord(idx + 1, q, pqm, postgraduateTypeCodes, heqfCodes, useMPrefixPopulationSplit))
                    .ToList();

                var total     = validationRows.Count;
                var passCount = validationRows.Count(row => row.ValidationResult == "PASS");
                var failCount = validationRows.Count(row => row.ValidationResult == "FAIL");
                var reviewCount = validationRows.Count(row => row.NeedsReview);
                var rate      = total > 0 ? Math.Round((decimal)failCount / total * 100, 2) : 0;

                var undergraduateCount = validationRows.Count(r => r.PopulationType == "Undergraduate");
                var postgraduateCount  = validationRows.Count(r => r.PopulationType == "Postgraduate");

                var controlSummaries = BuildRule11ControlSummaries(validationRows,
                    request.QualTable, request.QualMinTimeTotalCol ?? "_053", request.PqmMinTimeTotalCol ?? "Total2",
                    request.QualMinTimeWilCol ?? "_054", request.PqmWilCol ?? "WIL_EL2",
                    request.QualHeqfCol ?? "_084", request.PqmAccreditationCol ?? "CHE_HEQC_Accreditation_Approval_Ref_Nr",
                    request.QualTotalSubsidyCol ?? "_090", request.PqmTotalSubsidyCol ?? "Total2");

                var exceptions = validationRows
                    .Where(row => row.ValidationResult == "FAIL")
                    .Select(row => new Rule11ExceptionRecord
                    {
                        ValidationNumber = row.ValidationNumber,
                        QualId           = row.QualId,
                        QualName         = row.QualName,
                        QualApproval     = row.QualApproval,
                        QualHeqfType     = row.QualHeqfType,
                        Qual002          = row.Qual002,
                        Qual053          = row.Qual053,
                        Qual054          = row.Qual054,
                        Qual081          = row.Qual081,
                        Qual082          = row.Qual082,
                        Qual083          = row.Qual083,
                        Qual084          = row.Qual084,
                        Qual085          = row.Qual085,
                        Qual086          = row.Qual086,
                        Qual087          = row.Qual087,
                        Qual088          = row.Qual088,
                        Qual089          = row.Qual089,
                        Qual090          = row.Qual090,
                        PopulationType   = row.PopulationType,
                        CesmCode         = row.CesmCode,
                        PqmName          = row.PqmName,
                        PqmHeqfType      = row.PqmHeqfType,
                        PqmCode          = row.PqmCode,
                        PqmCesmCode1     = row.PqmCesmCode1,
                        PqmMinTimeTotal  = row.PqmMinTimeTotal,
                        PqmWIL           = row.PqmWIL,
                        PqmAccreditation = row.PqmAccreditation,
                        PqmTotalSubsidy  = row.PqmTotalSubsidy,
                        NameMatch        = row.NameMatch,
                        HeqfTypeMatch    = row.HeqfTypeMatch,
                        CesmCodeMatch    = row.CesmCodeMatch,
                        NeedsReview      = row.NeedsReview,
                        C2_TypeMatch     = row.C2_TypeMatch,
                        C3_MinTimeMatch  = row.C3_MinTimeMatch,
                        C4_WILMatch      = row.C4_WILMatch,
                        C5_HeqfMatch     = row.C5_HeqfMatch,
                        C5_ExpectedHeqf  = row.C5_ExpectedHeqf,
                        C6_SubsidyMatch  = row.C6_SubsidyMatch,
                        FailedControls   = row.FailedControls,
                        PopulationClassificationNote = row.PopulationClassificationNote,
                        ValidationResult = row.ValidationResult,
                        ExceptionReason  = row.ExceptionReason ?? ""
                    })
                    .ToList();

                var summary = new Rule11ValidationSummary
                {
                    Success          = true,
                    TotalValidated   = total,
                    PassCount        = passCount,
                    FailCount        = failCount,
                    ReviewCount      = reviewCount,
                    ExceptionRate    = rate,
                    Status           = failCount == 0 ? "PASS" : "FAIL",
                    Timestamp        = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Database         = request.Database,
                    QualTable        = request.QualTable,
                    QualIdCol        = request.QualIdCol,
                    QualNameCol      = request.QualNameCol,
                    QualApprovalCol  = request.QualApprovalCol,
                    QualHeqfTypeCol  = request.QualHeqfTypeCol,
                    QualApprovalFilterValue = approvalValue,
                    QualTypeCodesText = string.Join(", ", typeCodes),
                    CesmTable        = request.CesmTable,
                    CesmIdCol        = request.CesmIdCol,
                    CesmCodeCol      = request.CesmCodeCol,
                    PqmTable         = request.PqmTable,
                    PqmNameCol       = request.PqmNameCol,
                    PqmHeqfTypeCol   = request.PqmHeqfTypeCol,
                    PqmCodeCol       = request.PqmCodeCol,
                    PqmCesmCode1Col  = request.PqmCesmCode1Col ?? "CESM_CODE1",
                    PqmMinTimeTotalCol = request.PqmMinTimeTotalCol ?? "Total2",
                    PqmWilCol        = request.PqmWilCol ?? "WIL_EL2",
                    PqmAccreditationCol = request.PqmAccreditationCol ?? "CHE_HEQC_Accreditation_Approval_Ref_Nr",
                    PqmTotalSubsidyCol = request.PqmTotalSubsidyCol ?? "Total2",
                    QualMinTimeTotalCol = qmt,
                    QualMinTimeWilCol   = qwil,
                    QualHeqfCol         = qhq,
                    QualTotalSubsidyCol = qsub,
                    HeqfIndicatorCodesCsv = request.HeqfIndicatorCodesCsv ?? "H/,HEQF,HEQSF",
                    UseMPrefixPopulationSplit = useMPrefixPopulationSplit,
                    ExcludeMPrefixPattern = request.ExcludeMPrefixPattern,
                    UndergraduateCount = undergraduateCount,
                    PostgraduateCount  = postgraduateCount,
                    ControlSummaries   = controlSummaries,
                    ClientId         = request.ClientId,
                    ValidationRows   = validationRows,
                    Exceptions       = exceptions
                };

                if (request.ClientId > 0)
                {
                    await using var systemConnection = await OpenSystemConnectionAsync();
                    var systemUserId = await GetSystemUserIdByEmailAsync(systemConnection, userEmail);
                    if (!systemUserId.HasValue)
                        throw new InvalidOperationException("Current user is not available in the system database.");

                    await EnsureClientNotArchivedAsync(systemConnection, request.ClientId);
                    await ClearRuleSignoffsAsync(systemConnection, request.ClientId, 11);
                    await MarkPreviousRunsHistoricalAsync(systemConnection, request.ClientId, 11);

                    var runId = await InsertValidationRunAsync(systemConnection, request, summary, systemUserId.Value, userName);
                    summary.SavedRunId = runId;

                    await using var update = systemConnection.CreateConfiguredCommand();
                    update.CommandText = @"
UPDATE dbo.ValidationRuns
SET ResultsJSON = @ResultsJSON
WHERE RunID = @RunID;";
                    update.Parameters.AddWithValue("@RunID", runId);
                    update.Parameters.AddWithValue("@ResultsJSON",
                        ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
                    await update.ExecuteNonQueryAsync();
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule11ValidationSummary { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule11WorkspaceSaveResult> SaveWorkspaceAsync(
            Rule11ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                    return new Rule11WorkspaceSaveResult { Success = false, Error = "Run validation before saving the workspace." };

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                    return new Rule11WorkspaceSaveResult { Success = false, Error = "The saved workspace could not be found for this engagement." };

                await EnsureClientNotArchivedAsync(connection, request.ClientId);

                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule11WorkspaceSaveResult { Success = false, Error = "Current user is not available in the system database." };

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash    = await GetValidationRecordHashAsync(connection, request.RunId.Value);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer          = @HemisServer,
    AuditDatabase        = @AuditDatabase,
    StudTable            = @StudTable,
    DeceasedTable        = @DeceasedTable,
    StudColumn           = @StudColumn,
    DeceasedColumn       = @DeceasedColumn,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    WorkspaceSavedAt     = GETDATE(),
    PreviousHash         = @PreviousHash,
    RecordHash           = @RecordHash,
    Status               = 'Needs Review'
WHERE RunID   = @RunID
  AND ClientID = @ClientID;";
                command.Parameters.AddWithValue("@RunID",               request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID",            request.ClientId);
                command.Parameters.AddWithValue("@HemisServer",         request.Server);
                command.Parameters.AddWithValue("@AuditDatabase",       request.Database);
                command.Parameters.AddWithValue("@StudTable",           request.QualTable);
                command.Parameters.AddWithValue("@DeceasedTable",       request.CesmTable);
                command.Parameters.AddWithValue("@StudColumn",          request.PqmTable);
                command.Parameters.AddWithValue("@DeceasedColumn",      "");
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash",        (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash(
                    $"WorkspaceSave|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|" +
                    $"{request.QualTable}|{request.CesmTable}|{request.PqmTable}|" +
                    $"{request.QualApprovalFilterValue}|{request.QualTypeCodesText}|" +
                    $"{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule11WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0
                        ? "Workspace saved. Existing signoffs were removed and the run must be reviewed again."
                        : "Workspace saved and marked for review again.",
                    SignoffsCleared     = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace           = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule11WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule11WorkspaceSaveResult> BeginWorkspaceEditAsync(
            int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule11WorkspaceSaveResult { Success = false, Error = "Current user is not available in the system database." };

                var clientId = await GetClientIdForRunAsync(runId);
                if (!clientId.HasValue)
                    return new Rule11WorkspaceSaveResult { Success = false, Error = "Saved workspace was not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash    = await GetValidationRecordHashAsync(connection, runId);

                await using (var markEdit = connection.CreateConfiguredCommand())
                {
                    markEdit.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    WorkspaceSavedAt     = NULL,
    PreviousHash         = @PreviousHash,
    RecordHash           = @RecordHash,
    Status               = 'Needs Review'
WHERE RunID = @RunID;";
                    markEdit.Parameters.AddWithValue("@RunID", runId);
                    markEdit.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? reviewerEmail);
                    markEdit.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                    markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash(
                        $"WorkspaceEdit|{runId}|{reviewerName ?? reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                    await markEdit.ExecuteNonQueryAsync();
                }

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);
                return new Rule11WorkspaceSaveResult
                {
                    Success             = true,
                    Message             = clearedSignoffs > 0
                        ? "Editing has begun. Existing signoffs were removed so the workspace must be reviewed again."
                        : "Editing has begun.",
                    SignoffsCleared     = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace           = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule11WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        public async Task<Rule11WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(
            int clientId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID,
    vr.ClientID,
    ISNULL(vr.HemisServer,   '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable,     '') AS QualTable,
    ISNULL(vr.DeceasedTable, '') AS CesmTable,
    ISNULL(vr.StudColumn,    '') AS PqmTable,
    ISNULL(vr.Status,        '') AS Status,
    ISNULL(vr.LastEditedByUserName, '') AS LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID   = @ClientID
  AND vr.RuleNumber = 11
ORDER BY vr.IsCurrent DESC, vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summaryJson = reader.IsDBNull(10) ? null : reader.GetString(10);
            var summary = DeserializeSummary(summaryJson);
            if (summary != null)
                ApplyBrowserPreview(summary);

            var workspace = new Rule11WorkspaceStateViewModel
            {
                RunId         = reader.GetInt32(0),
                ClientId      = reader.GetInt32(1),
                Server        = reader.GetString(2),
                Database      = reader.GetString(3),
                Driver        = "ODBC Driver 17 for SQL Server",
                QualTable     = reader.GetString(4),
                CesmTable     = reader.GetString(5),
                PqmTable      = reader.GetString(6),
                CurrentStatus = reader.GetString(7),
                LastEditedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8),
                LastEditedAt         = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                Summary              = summary
            };

            if (summary != null)
            {
                workspace.QualIdCol       = summary.QualIdCol;
                workspace.QualNameCol     = summary.QualNameCol;
                workspace.QualApprovalCol = summary.QualApprovalCol;
                workspace.QualHeqfTypeCol = summary.QualHeqfTypeCol;
                workspace.QualApprovalFilterValue = summary.QualApprovalFilterValue;
                workspace.QualTypeCodesText = summary.QualTypeCodesText;
                workspace.CesmIdCol       = summary.CesmIdCol;
                workspace.CesmCodeCol     = summary.CesmCodeCol;
                workspace.PqmNameCol      = summary.PqmNameCol;
                workspace.PqmHeqfTypeCol  = summary.PqmHeqfTypeCol;
                workspace.PqmCodeCol      = summary.PqmCodeCol;
                workspace.PqmCesmCode1Col   = summary.PqmCesmCode1Col;
                workspace.PqmMinTimeTotalCol = summary.PqmMinTimeTotalCol;
                workspace.PqmWilCol         = summary.PqmWilCol;
                workspace.PqmAccreditationCol = summary.PqmAccreditationCol;
                workspace.PqmTotalSubsidyCol = summary.PqmTotalSubsidyCol;
                workspace.QualMinTimeTotalCol = summary.QualMinTimeTotalCol;
                workspace.QualMinTimeWilCol   = summary.QualMinTimeWilCol;
                workspace.QualHeqfCol         = summary.QualHeqfCol;
                workspace.QualTotalSubsidyCol = summary.QualTotalSubsidyCol;
                workspace.HeqfIndicatorCodesCsv = summary.HeqfIndicatorCodesCsv;
                workspace.UseMPrefixPopulationSplit = summary.UseMPrefixPopulationSplit;
                workspace.ExcludeMPrefixPattern = summary.ExcludeMPrefixPattern;
            }

            await reader.CloseAsync();

            int? currentUserId = null;
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                    workspace.CurrentUserEngagementRole =
                        await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? "";
            }

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            var currentRoleSignoff = signoffs.FirstOrDefault(s =>
                HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(
                    s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff   = currentRoleSignoff != null;
            workspace.CurrentUserSignoffComment = currentRoleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved          = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            if (string.IsNullOrWhiteSpace(workspace.CurrentStatus))
                workspace.CurrentStatus = workspace.Summary?.Status ?? "";

            return workspace;
        }

        public async Task<Rule11RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();

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

            var summaryJson = reader.IsDBNull(6) ? null : reader.GetString(6);
                var summary = DeserializeSummary(summaryJson);
                if (summary == null)
                    return null;
                EnsurePopulationTypes(summary);

            var viewModel = new Rule11RunReviewViewModel
            {
                RunId          = reader.GetInt32(0),
                ClientId       = reader.GetInt32(1),
                IsCurrentRun   = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer   = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Summary        = summary
            };

            await reader.CloseAsync();

            int? currentUserId = null;
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                    viewModel.CurrentUserEngagementRole =
                        await GetEngagementRoleAsync(connection, viewModel.ClientId, currentUserId.Value) ?? "";
            }

            viewModel.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            viewModel.HasDataAnalystSignoff = viewModel.Signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return viewModel;
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(runId);
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
IF EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID)
BEGIN
    UPDATE dbo.ReviewSignoffs
    SET SignoffRole = @SignoffRole, ReviewType = 'Final', Comment = @Comment, SignedOffAt = GETDATE()
    WHERE RunID = @RunID AND ReviewerID = @ReviewerID;
END
ELSE
BEGIN
    INSERT INTO dbo.ReviewSignoffs (ClientID, RunID, ReviewerID, SignoffRole, ReviewType, Comment, SignedOffAt)
    VALUES (@ClientID, @RunID, @ReviewerID, @SignoffRole, 'Final', @Comment, GETDATE());
END";
            command.Parameters.AddWithValue("@ClientID",    clientId.Value);
            command.Parameters.AddWithValue("@RunID",       runId);
            command.Parameters.AddWithValue("@ReviewerID",  reviewerId.Value);
            command.Parameters.AddWithValue("@SignoffRole", engagementRole!);
            command.Parameters.AddWithValue("@Comment",
                string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            await command.ExecuteNonQueryAsync();

            await UpdateRunStatusFromSignoffsAsync(connection, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(
                connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0) return;
        }

        public string GenerateSql(Rule11ValidationRequest request)
        {
            var qt  = request.QualTable;
            var ct  = request.CesmTable;
            var pt  = request.PqmTable;
            var qi  = request.QualIdCol;
            var qn  = request.QualNameCol;
            var qa  = request.QualApprovalCol;
            var qht = request.QualHeqfTypeCol;
            var ci  = request.CesmIdCol;
            var cc  = request.CesmCodeCol;
            var pn  = request.PqmNameCol;
            var pht = request.PqmHeqfTypeCol;
            var pc  = request.PqmCodeCol;
            var approvalValue = NormalizeFilterValue(request.QualApprovalFilterValue, "A");
                var typeCodes = ParseQualTypeCodes(request.QualTypeCodesText);
                var typeCodeSql = string.Join(", ", typeCodes.Select(code => $"'{EscapeSqlString(code)}'"));
                var populationTypeSql = $"CASE WHEN UPPER(LTRIM(RTRIM(CAST(q.[{qht}] AS NVARCHAR(255))))) IN ({typeCodeSql}) THEN 'Postgraduate' ELSE 'Undergraduate' END";
            string sqlNorm(string expr, int length) =>
                $"UPPER(LTRIM(RTRIM(CAST({expr} AS NVARCHAR({length})))))";
            string sqlTrimLeadingZeros(string expr) =>
                $"CASE WHEN {expr} = '' THEN '' WHEN PATINDEX('%[^0]%', {expr}) = 0 THEN '0' ELSE SUBSTRING({expr}, PATINDEX('%[^0]%', {expr}), LEN({expr})) END";
            string sqlPrefixMatch(string leftExpr, string rightExpr, int digits) =>
                $"(LEN({leftExpr}) >= {digits} AND LEN({rightExpr}) >= {digits} AND LEFT({leftExpr}, {digits}) = LEFT({rightExpr}, {digits}))";
            var qualNameSql = sqlNorm("b.Qual_Name", 500);
            var pqmNameSql = sqlNorm($"p.[{pn}]", 500);
            var qualHeqfSql = sqlNorm("b.Qual_HEQF_Type", 100);
            var pqmHeqfSql = sqlNorm($"p.[{pht}]", 100);
            var cesmSql = sqlNorm("b.CESM_Code", 100);
            var pqmCodeSql = sqlNorm($"p.[{pc}]", 100);
            var cesmTrimSql = sqlTrimLeadingZeros(cesmSql);
            var pqmTrimSql = sqlTrimLeadingZeros(pqmCodeSql);
            var exactMatchSql = $"{cesmSql} = {pqmCodeSql}";
            var reviewMatchSql = string.Join(" OR ", new[]
            {
                sqlPrefixMatch(cesmSql, pqmCodeSql, 4),
                sqlPrefixMatch(cesmTrimSql, pqmTrimSql, 4),
                sqlPrefixMatch(cesmSql, pqmCodeSql, 3),
                sqlPrefixMatch(cesmTrimSql, pqmTrimSql, 3)
            });

            return $@"-- ============================================================================
-- HEMIS 2026 - RULE 11: QUAL vs CESM vs PQM VALIDATION
-- 100% POPULATION WITH PROPER EXTRACTION
-- ============================================================================

IF OBJECT_ID('tempdb..#R11_Extracted_Population') IS NOT NULL DROP TABLE #R11_Extracted_Population;
IF OBJECT_ID('tempdb..#R11_Validation')           IS NOT NULL DROP TABLE #R11_Validation;

-- ============================================================================
-- STEP 1: EXTRACT 100% APPROVED QUALIFICATION POPULATION
-- ============================================================================

SELECT
    ROW_NUMBER() OVER (ORDER BY q.[{qi}]) AS Extract_Number,
    q.[{qi}]  AS [Qualification_Code],
    q.[_002]  AS [Previous_Years_Qualification_Code],
    q.[{qn}]  AS [Qualification_Name_Designator],
    q.[{qa}]  AS [Approval_Status],
    q.[{qht}] AS [Qualification_Type_Descriptor],
    q.[_053]  AS [Minimum_Time_Total],
    q.[_054]  AS [Minimum_Time_Experiential],
    q.[_081]  AS [Institution_Programme_Name],
    q.[_082]  AS [Qualifier],
    q.[_083]  AS [Abbreviation],
    q.[_084]  AS [Legacy_Indicator],
    q.[_085]  AS [NQF_Exit_Level],
    q.[_086]  AS [Minimum_Total_Credits],
    q.[_087]  AS [Minimum_Credits_At_Level],
    q.[_088]  AS [Maximum_Credits_At_Level],
    q.[_089]  AS [Mode_Of_Delivery],
    q.[_090]  AS [Total_Subsidy_Units],
    c.[{cc}]  AS [CESM_Code],
    {populationTypeSql} AS [Population_Type]
INTO #R11_Extracted_Population
FROM [{qt}] q
LEFT JOIN [{ct}] c ON q.[{qi}] = c.[{ci}]
WHERE UPPER(LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(50))))) = '{approvalValue}';

-- ============================================================================
-- STEP 2: VALIDATE EXTRACTED POPULATION AGAINST PQM
-- ============================================================================

SELECT
    e.Extract_Number,
    e.[Qualification_Code],
    e.[Previous_Years_Qualification_Code],
    e.[Qualification_Name_Designator],
    e.[Approval_Status],
    e.[Qualification_Type_Descriptor],
    e.[Population_Type],
    e.[CESM_Code],
    p.[{pn}]  AS [PQM_Qualification_Name],
    p.[{pht}] AS [PQM_HEQF_Qual_Type],
    p.[{pc}]  AS [PQM_CESM_Code],
    CASE WHEN p.[{pn}]  IS NOT NULL THEN 'YES' ELSE 'NO' END AS [Qualification_Name_Match],
    CASE WHEN p.[{pht}] IS NOT NULL THEN 'YES' ELSE 'NO' END AS [HEQF_Type_Match],
    CASE WHEN p.[{pc}]  IS NOT NULL THEN 'YES' ELSE 'NO' END AS [CESM_Code_Match],
    CASE
        WHEN p.[{pn}] IS NOT NULL AND p.[{pht}] IS NOT NULL AND p.[{pc}] IS NOT NULL THEN 'PASS'
        ELSE 'FAIL'
    END AS [Validation_Result],
    CASE
        WHEN p.[{pn}] IS NULL
            THEN 'Qualification name not found in [{pt}].'
        WHEN p.[{pht}] IS NULL
            THEN 'Qualification name found, but HEQF qualification type does not match [{pt}].'
        WHEN p.[{pc}] IS NULL
            THEN 'Qualification name and HEQF type found, but CESM code does not match [{pt}].'
        ELSE 'Qualification name, HEQF type and CESM code agree to [{pt}].'
    END AS [Validation_Reason]
INTO #R11_Validation
FROM #R11_Extracted_Population e
LEFT JOIN [{pt}] p
    ON UPPER(LTRIM(RTRIM(CAST(e.[Qualification_Name_Designator] AS NVARCHAR(500)))))
     = UPPER(LTRIM(RTRIM(CAST(p.[{pn}] AS NVARCHAR(500)))))
   AND UPPER(LTRIM(RTRIM(CAST(e.[Qualification_Type_Descriptor] AS NVARCHAR(100)))))
     = UPPER(LTRIM(RTRIM(CAST(p.[{pht}] AS NVARCHAR(100)))))
   AND UPPER(LTRIM(RTRIM(CAST(e.[CESM_Code] AS NVARCHAR(100)))))
     = UPPER(LTRIM(RTRIM(CAST(p.[{pc}] AS NVARCHAR(100)))));

-- ============================================================================
-- STEP 3: FULL EXTRACTED POPULATION
-- ============================================================================

SELECT * FROM #R11_Extracted_Population ORDER BY Extract_Number;

-- ============================================================================
-- STEP 4: FULL VALIDATION RESULT
-- ============================================================================

SELECT * FROM #R11_Validation ORDER BY Extract_Number;

-- ============================================================================
-- STEP 5: EXCEPTIONS ONLY
-- ============================================================================

SELECT * FROM #R11_Validation WHERE [Validation_Result] = 'FAIL' ORDER BY Extract_Number;

-- ============================================================================
-- STEP 6: SUMMARY
-- ============================================================================

SELECT
    COUNT(*)                                                                              AS [Total_Approved_Qualifications],
    SUM(CASE WHEN [Validation_Result] = 'PASS' THEN 1 ELSE 0 END)                       AS [PASS_Count],
    SUM(CASE WHEN [Validation_Result] = 'FAIL' THEN 1 ELSE 0 END)                       AS [FAIL_Count],
    CAST(SUM(CASE WHEN [Validation_Result] = 'FAIL' THEN 1 ELSE 0 END) * 100.0
         / NULLIF(COUNT(*), 0) AS DECIMAL(5,2))                                          AS [Exception_Rate_Pct]
FROM #R11_Validation;

DROP TABLE #R11_Extracted_Population;
DROP TABLE #R11_Validation;
-- ============================================================================
-- END RULE 11
-- ============================================================================
";
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private async Task<int> InsertValidationRunAsync(
            SqlConnection connection,
            Rule11ValidationRequest request,
            Rule11ValidationSummary summary,
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
            command.Parameters.AddWithValue("@ClientID",      request.ClientId);
            command.Parameters.AddWithValue("@UserID",        systemUserId);
            command.Parameters.AddWithValue("@HemisServer",   request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable",     request.QualTable);
            command.Parameters.AddWithValue("@DeceasedTable", request.CesmTable);
            command.Parameters.AddWithValue("@StudColumn",    request.PqmTable);
            command.Parameters.AddWithValue("@DeceasedColumn", "");
            command.Parameters.AddWithValue("@RuleNumber",    11);
            command.Parameters.AddWithValue("@RuleName",      "QUAL vs CESM vs PQM Validation");
            command.Parameters.AddWithValue("@Status",        summary.FailCount == 0 ? "Pass" : "Fail");
            command.Parameters.AddWithValue("@TotalRecords",  summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount",     summary.PassCount);
            command.Parameters.AddWithValue("@FailCount",     summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@ExceptionsJSON",
                ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary.Exceptions)));
            command.Parameters.AddWithValue("@ResultsJSON",
                ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? DBNull.Value);

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 11);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var value = await command.ExecuteScalarAsync();
            var runId = Convert.ToInt32(value);

            await using var hashCmd = connection.CreateConfiguredCommand();
            hashCmd.CommandText = @"UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCmd.Parameters.AddWithValue("@RunID", runId);
            hashCmd.Parameters.AddWithValue("@RecordHash", ComputeHash(
                $"ValidationRun|{runId}|{request.ClientId}|{systemUserId}|{summary.Status}|" +
                $"{summary.TotalValidated}|{summary.PassCount}|{summary.FailCount}|{summary.ReviewCount}|{summary.ExceptionRate}|" +
                $"{summary.Timestamp}|{request.QualApprovalFilterValue}|{request.QualTypeCodesText}|{previousHash}"));
            await hashCmd.ExecuteNonQueryAsync();
            return runId;
        }

        private async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var cnt = connection.CreateConfiguredCommand();
            cnt.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            cnt.Parameters.AddWithValue("@RunID", runId);
            var existingCount = Convert.ToInt32(await cnt.ExecuteScalarAsync());

            await using var del = connection.CreateConfiguredCommand();
            del.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            del.Parameters.AddWithValue("@RunID", runId);
            await del.ExecuteNonQueryAsync();

            await using var upd = connection.CreateConfiguredCommand();
            upd.CommandText = "UPDATE dbo.ValidationRuns SET Status = 'Needs Review' WHERE RunID = @RunID;";
            upd.Parameters.AddWithValue("@RunID", runId);
            await upd.ExecuteNonQueryAsync();

            return existingCount;
        }

        private async Task UpdateRunStatusFromSignoffsAsync(SqlConnection connection, int runId)
        {
            var hasAll = await HasAllRequiredSignoffsAsync(connection, runId);
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "UPDATE dbo.ValidationRuns SET Status = @Status WHERE RunID = @RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@Status", hasAll ? "Reviewed and Completed" : "Needs Review");
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<bool> HasAllRequiredSignoffsAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'DataAnalyst') THEN 1 ELSE 0 END AS HasDA,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Manager')     THEN 1 ELSE 0 END AS HasMgr,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Director')    THEN 1 ELSE 0 END AS HasDir;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return false;
            return (!r.IsDBNull(0) && r.GetInt32(0) == 1) &&
                   (!r.IsDBNull(1) && r.GetInt32(1) == 1) &&
                   (!r.IsDBNull(2) && r.GetInt32(2) == 1);
        }

        private async Task<int> ClearRuleSignoffsAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cnt = connection.CreateConfiguredCommand();
            cnt.CommandText = @"SELECT COUNT(1) FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns vr ON vr.RunID = rs.RunID
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = @RuleNumber;";
            cnt.Parameters.AddWithValue("@ClientID",   clientId);
            cnt.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var count = Convert.ToInt32(await cnt.ExecuteScalarAsync());

            await using var del = connection.CreateConfiguredCommand();
            del.CommandText = @"DELETE rs FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns vr ON vr.RunID = rs.RunID
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = @RuleNumber;";
            del.Parameters.AddWithValue("@ClientID",   clientId);
            del.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await del.ExecuteNonQueryAsync();
            return count;
        }

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"UPDATE dbo.ValidationRuns SET IsCurrent = 0
WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND IsCurrent = 1;";
            cmd.Parameters.AddWithValue("@ClientID",   clientId);
            cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(
            SqlConnection connection, int runId, int? currentUserId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
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
ORDER BY CASE ISNULL(rs.SignoffRole,'')
           WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END,
         rs.SignedOffAt DESC;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@CurrentUserID", currentUserId.HasValue ? currentUserId.Value : DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<RunSignoffViewModel>();
            while (await r.ReadAsync())
            {
                list.Add(new RunSignoffViewModel
                {
                    Id            = r.GetInt32(0),
                    SignoffRole   = r.IsDBNull(1) ? "" : r.GetString(1),
                    ReviewerName  = r.IsDBNull(2) ? "" : r.GetString(2),
                    ReviewerEmail = r.IsDBNull(3) ? "" : r.GetString(3),
                    Comment       = r.IsDBNull(4) ? "" : r.GetString(4),
                    SignedOffAt   = r.IsDBNull(5) ? DateTime.UtcNow : r.GetDateTime(5),
                    IsCurrentUser = !r.IsDBNull(6) && r.GetBoolean(6)
                });
            }
            return list;
        }

        private async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            cmd.Parameters.AddWithValue("@Email", email);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToInt32(v);
        }

        private async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"SELECT TOP 1 EngagementRole FROM dbo.UserClientAssignments
WHERE ClientID = @ClientID AND UserID = @UserID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            cmd.Parameters.AddWithValue("@UserID",   userId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private static async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"SELECT CASE WHEN EXISTS (
    SELECT 1 FROM dbo.ValidationRuns
    WHERE RunID = @RunID
      AND (WorkspaceSavedAt IS NOT NULL
           OR EXISTS (SELECT 1 FROM dbo.ReviewSignoffs rs
                      WHERE rs.RunID = ValidationRuns.RunID AND rs.SignoffRole = 'DataAnalyst'))
) THEN 1 ELSE 0 END;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private async Task<bool> HasSignoffRoleAsync(SqlConnection connection, int runId, string signoffRole)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole=@SignoffRole;";
            cmd.Parameters.AddWithValue("@RunID",       runId);
            cmd.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        private async Task<string?> GetValidationRecordHashAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"SELECT TOP 1 RecordHash FROM dbo.ValidationRuns
WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND RecordHash IS NOT NULL
ORDER BY RunTimestamp DESC, RunID DESC;";
            cmd.Parameters.AddWithValue("@ClientID",   clientId);
            cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            var status = Convert.ToString(await cmd.ExecuteScalarAsync());
            if (string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
        }

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server   = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trust    = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);

            var builder = new SqlConnectionStringBuilder
            {
                DataSource             = server,
                InitialCatalog         = database,
                IntegratedSecurity     = true,
                TrustServerCertificate = trust,
                Encrypt                = false,
                ConnectTimeout         = 180
            };

            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static Rule11ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                if (string.IsNullOrWhiteSpace(decoded)) return null;
                return JsonConvert.DeserializeObject<Rule11ValidationSummary>(decoded);
            }
            catch { return null; }
        }

        private static void ApplyBrowserPreview(Rule11ValidationSummary summary)
        {
            summary.ValidationRows = summary.ValidationRows.Take(BrowserPreviewRowLimit).ToList();
            summary.Exceptions     = summary.Exceptions.Take(BrowserPreviewRowLimit).ToList();
        }

        private static void EnsurePopulationTypes(Rule11ValidationSummary summary)
        {
            var postgraduateTypeCodes = new HashSet<string>(
                ParseQualTypeCodes(summary.QualTypeCodesText),
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in summary.ValidationRows)
            {
                if (string.IsNullOrWhiteSpace(row.PopulationType))
                    row.PopulationType = ClassifyPopulationType(row.QualHeqfType, postgraduateTypeCodes);
            }

            foreach (var ex in summary.Exceptions)
            {
                if (string.IsNullOrWhiteSpace(ex.PopulationType))
                    ex.PopulationType = ClassifyPopulationType(ex.QualHeqfType, postgraduateTypeCodes);
            }
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager",     StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director",    StringComparison.OrdinalIgnoreCase);

        private static string NormalizeFilterValue(string? value, string defaultValue) =>
            string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim().ToUpperInvariant();

        private static List<string> ParseQualTypeCodes(string? text)
        {
            var values = Regex.Split(text ?? "", @"[,\r\n;]+")
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return values.Count > 0 ? values : DefaultQualTypeCodes.ToList();
        }

        private static string EscapeSqlString(string value) =>
            (value ?? "").Replace("'", "''");

        private static string Sanitise(string? name) =>
            (name ?? "").Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }
}
