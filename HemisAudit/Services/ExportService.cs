using ClosedXML.Excel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IExportService
    {
        byte[] ExportRule36Excel(ValidationSummary summary);
        byte[] ExportRule37Excel(Rule37ValidationSummary summary);
        byte[] ExportRule37Csv(Rule37ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule11Excel(Rule11ValidationSummary summary);
        byte[] ExportRule11Csv(Rule11ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule54Excel(Rule54ValidationSummary summary);
        byte[] ExportRule54Csv(Rule54ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule55Excel(Rule55ValidationSummary summary);
        byte[] ExportRule55Csv(Rule55ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule57Excel(Rule57ValidationSummary summary);
        byte[] ExportRule57Csv(Rule57ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule58Excel(Rule58ValidationSummary summary);
        byte[] ExportRule58Csv(Rule58ValidationSummary summary);
        byte[] ExportRule59Excel(Rule59ValidationSummary summary);
        byte[] ExportRule59Csv(Rule59ValidationSummary summary);
        byte[] ExportRule60Excel(Rule41ValidationSummary summary);
        byte[] ExportRule38Excel(Rule38ValidationSummary summary);
        byte[] ExportRule38Csv(Rule38ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportExcel(Rule10ValidationSummary summary);
        byte[] ExportExcel(Rule11ValidationSummary summary);
        byte[] ExportExcel(Rule12ValidationSummary summary);
        byte[] ExportExcel(Rule34ValidationSummary summary);
        byte[] ExportExcel(Rule35ValidationSummary summary);
        byte[] ExportExcel(Rule26ValidationSummary summary);
        byte[] ExportExcel(Rule27ValidationSummary summary);
        byte[] ExportExcel(Rule13ValidationSummary summary);
        byte[] ExportExcel(Rule20ValidationSummary summary);
        byte[] ExportExcel(Rule17ValidationSummary summary);
        byte[] ExportExcel(Rule14ValidationSummary summary);
        byte[] ExportExcel(Rule15ValidationSummary summary);
        byte[] ExportExcel(Rule16ValidationSummary summary);
        byte[] ExportExcel(Rule18ValidationSummary summary);
        byte[] ExportExcel(Rule19ValidationSummary summary);
        byte[] ExportExcel(Rule22ValidationSummary summary);
        byte[] ExportExcel(Rule23ValidationSummary summary);
        byte[] ExportExcel(Rule24ValidationSummary summary);
        byte[] ExportExcel(Rule25ValidationSummary summary);
        byte[] ExportExcel(Rule21ValidationSummary summary);
        byte[] ExportExcel(Rule29ValidationSummary summary);
        byte[] ExportExcel(Rule31ValidationSummary summary);
        byte[] ExportExcel(Rule32ValidationSummary summary);
        byte[] ExportRule36Csv(ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportCsv(Rule10ValidationSummary summary);
        byte[] ExportCsv(Rule11ValidationSummary summary);
        byte[] ExportCsv(Rule12ValidationSummary summary);
        byte[] ExportCsv(Rule34ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportCsv(Rule35ValidationSummary summary);
        byte[] ExportCsv(Rule26ValidationSummary summary);
        byte[] ExportCsv(Rule27ValidationSummary summary);
        byte[] ExportCsv(Rule13ValidationSummary summary);
        byte[] ExportCsv(Rule20ValidationSummary summary);
        byte[] ExportCsv(Rule17ValidationSummary summary);
        byte[] ExportCsv(Rule14ValidationSummary summary);
        byte[] ExportCsv(Rule15ValidationSummary summary);
        byte[] ExportCsv(Rule16ValidationSummary summary);
        byte[] ExportCsv(Rule18ValidationSummary summary);
        byte[] ExportCsv(Rule19ValidationSummary summary);
        byte[] ExportCsv(Rule22ValidationSummary summary);
        byte[] ExportCsv(Rule23ValidationSummary summary);
        byte[] ExportCsv(Rule24ValidationSummary summary);
        byte[] ExportCsv(Rule25ValidationSummary summary);
        byte[] ExportCsv(Rule21ValidationSummary summary);
        byte[] ExportCsv(Rule29ValidationSummary summary);
        byte[] ExportCsv(Rule31ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportCsv(Rule32ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule21Excel(Rule21ValidationSummary summary);
        byte[] ExportRule21Csv(Rule21ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule39Excel(Rule39ValidationSummary summary);
        byte[] ExportRule39Csv(Rule39ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule51Excel(Rule51ValidationSummary summary);
        byte[] ExportRule51Csv(Rule51ValidationSummary summary);
        byte[] ExportRule52Excel(Rule52ValidationSummary summary);
        byte[] ExportRule52Csv(Rule52ValidationSummary summary);
        byte[] ExportRule53Excel(Rule53ValidationSummary summary);
        byte[] ExportRule53Csv(Rule53ValidationSummary summary);
        byte[] ExportRule66Excel(Rule66ValidationSummary summary);
        byte[] ExportRule66Csv(Rule66ValidationSummary summary);
        byte[] ExportRule67Excel(Rule67ValidationSummary summary);
        byte[] ExportRule67Csv(Rule67ValidationSummary summary);
        byte[] ExportRule68Excel(Rule68ValidationSummary summary);
        byte[] ExportRule68Csv(Rule68ValidationSummary summary);
        byte[] ExportSql(string sql);
        byte[] ExportRule64Excel(Rule64ValidationSummary summary);
    }

    public class ExportService : IExportService
    {
        private const int Rule18DetailedSheetRowThreshold = 10000;

        // ─── Excel Export — mirrors notebook Excel with 3 sheets ─────────────
        public byte[] ExportRule36Excel(ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            // ── Sheet 1: Validation Results ──
            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 36 VALIDATION RESULTS", 4);
            WriteValidationHeaderRow(wsResults, 2, summary);
            WriteValidationRows(wsResults, 3, summary);

            // ── Sheet 2: Summary ──
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 36: DECEASED STUDENTS VALIDATION", 2);

            var summaryData = new[]
            {
                ("Database",            summary.Database),
                ("STUD Table",          summary.StudTable),
                ("Deceased Table",      summary.DeceasedTable),
                ("STUD Column",         summary.StudColumn),
                ("Deceased Column",     summary.DeceasedColumn),
                ("Validation Rule",     $"[{summary.StudTable}].[{summary.StudColumn}] = [{summary.DeceasedTable}].[{summary.DeceasedColumn}]"),
                ("Validation Date",     summary.Timestamp),
                ("",                    ""),
                ("VALIDATION RESULTS",  ""),
                ("Total Validated",     summary.TotalValidated.ToString("N0")),
                ("PASS (Active)",       summary.PassCount.ToString("N0")),
                ("FAIL (Deceased)",     summary.FailCount.ToString("N0")),
                ("Exception Rate",      $"{summary.ExceptionRate:F2}%"),
                ("Status",              summary.Status),
            };

            int row = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var hdrCell = wsSummary.Cell(row, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(row, 1, row, 2).Merge();
                }
                else if (label == "")
                {
                    // blank separator
                }
                else
                {
                    wsSummary.Cell(row, 1).Value = label;
                    wsSummary.Cell(row, 1).Style.Font.Bold = true;
                    wsSummary.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(row, 2).Value = value;

                    if (label == "Status")
                    {
                        var color = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(row, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(row, 2).Style.Font.Bold = true;
                    }
                }
                row++;
            }

            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 60;

            // ── Sheet 3: Exceptions ──
            if (summary.Exceptions.Any())
            {
                var wsEx = wb.Worksheets.Add("Exceptions");
                StyleHeaderRow(wsEx, 1, "RULE 36 EXCEPTIONS — DECEASED STUDENTS", 5);

                var exHeaders = new[] { "Validation #", "Student ID", "Exception Reason", "Validation Result", "Additional Details" };
                for (int i = 0; i < exHeaders.Length; i++)
                {
                    var cell = wsEx.Cell(2, i + 1);
                    cell.Value = exHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    cell.Style.Font.FontColor = XLColor.White;
                }

                int exRow = 3;
                foreach (var ex in summary.Exceptions)
                {
                    wsEx.Cell(exRow, 1).Value = ex.ValidationNumber;
                    wsEx.Cell(exRow, 2).Value = ex.StudentId;
                    wsEx.Cell(exRow, 3).Value = ex.ExceptionReason;
                    wsEx.Cell(exRow, 4).Value = ex.ValidationResult;
                    wsEx.Cell(exRow, 5).Value = string.Join(" | ", ex.AdditionalColumns.Take(5).Select(kv => $"{kv.Key}={kv.Value}"));

                    // Red fill for FAIL rows
                    wsEx.Range(exRow, 1, exRow, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                    wsEx.Cell(exRow, 4).Style.Font.FontColor = XLColor.FromHtml("#C62828");
                    wsEx.Cell(exRow, 4).Style.Font.Bold = true;
                    exRow++;
                }

                for (int c = 1; c <= 5; c++) wsEx.Column(c).AdjustToContents();
            }

            // ── Sheet 4: Statistics ──
            var wsStats = wb.Worksheets.Add("Statistics");
            StyleHeaderRow(wsStats, 1, "VALIDATION STATISTICS", 2);

            var stats = new[]
            {
                ("Total Records Validated", (object)summary.TotalValidated),
                ("PASS Count",              (object)summary.PassCount),
                ("FAIL Count",              (object)summary.FailCount),
                ("Exception Rate (%)",      (object)(double)summary.ExceptionRate),
                ("Pass Rate (%)",           (object)(double)(summary.TotalValidated > 0
                    ? Math.Round((decimal)summary.PassCount / summary.TotalValidated * 100, 2) : 0)),
            };

            row = 2;
            foreach (var (label, value) in stats)
            {
                wsStats.Cell(row, 1).Value = label;
                wsStats.Cell(row, 1).Style.Font.Bold = true;
                wsStats.Cell(row, 2).SetValue(value?.ToString());
                row++;
            }
            wsStats.Column(1).Width = 30;
            wsStats.Column(2).Width = 20;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule34ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var hasBlock = !string.IsNullOrWhiteSpace(summary.BlockColumn);
            var usesClientComparison =
                !string.IsNullOrWhiteSpace(summary.ClientTableName) &&
                !string.IsNullOrWhiteSpace(summary.ClientJoinColumn);
            var censusHeaderLabel = usesClientComparison ? "Client Census Date" : "Stored Census Date";
            var censusColumnLabel = usesClientComparison ? "Client Census Date Column" : "Census Date Column";
            var colCount = hasBlock ? 11 : 10;

            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 34 VALIDATION RESULTS", colCount);

            var resultHeaderList = new List<string>
            {
                "Validation Number",
                "First Day", "Last Day", "c_Days", "c_Days_2",
                "Prepared Census Date", "Actual Census Date", censusHeaderLabel,
                "Day Status", "Validation Status"
            };
            if (hasBlock) resultHeaderList.Insert(1, $"{summary.BlockColumn} (block)");
            var resultHeaders = resultHeaderList.ToArray();

            for (int i = 0; i < resultHeaders.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = resultHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(i == 1 && hasBlock ? "#5D4037" : "#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var rowIndex = 3;
            foreach (var row in summary.ValidationRows)
            {
                int col = 1;
                wsResults.Cell(rowIndex, col++).Value = row.ValidationNumber;
                if (hasBlock) wsResults.Cell(rowIndex, col++).Value = row.BlockValue;
                wsResults.Cell(rowIndex, col++).Value = row.FirstDayValue;
                wsResults.Cell(rowIndex, col++).Value = row.LastDayValue;
                wsResults.Cell(rowIndex, col++).Value = row.CurrentDays?.ToString() ?? "NULL";
                wsResults.Cell(rowIndex, col++).Value = row.CurrentDaysHalf?.ToString() ?? "NULL";
                wsResults.Cell(rowIndex, col++).Value = row.ComputedCensusDate;
                wsResults.Cell(rowIndex, col++).Value = row.ActualCensusDate;
                wsResults.Cell(rowIndex, col++).Value = row.CensusDateValue;
                wsResults.Cell(rowIndex, col++).Value = row.DayStatus;
                wsResults.Cell(rowIndex, col++).Value = row.ValidationStatus;

                wsResults.Range(rowIndex, 1, rowIndex, colCount).Style.Fill.BackgroundColor =
                    row.DateMatch ? XLColor.FromHtml("#F3FFF3") : XLColor.FromHtml("#FFF3F3");
                rowIndex++;
            }

            for (int c = 1; c <= colCount; c++) wsResults.Column(c).AdjustToContents();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 34: CENSUS DATE VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("Client Census Table", usesClientComparison ? summary.ClientTableName : "(not used)"),
                ("First Day Column", summary.FirstDayColumn),
                ("Last Day Column", summary.LastDayColumn),
                (censusColumnLabel, summary.CensusDateColumn),
                ("Source Block / Join Column", string.IsNullOrWhiteSpace(summary.BlockColumn) ? "(not selected)" : summary.BlockColumn),
                ("Client Join Column", usesClientComparison ? summary.ClientJoinColumn : "(not used)"),
                ("Holiday Years", summary.HolidayYearRange),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("VALIDATION RESULTS", ""),
                ("Total Validated", summary.TotalValidated.ToString("N0")),
                ("PASS (Match)", summary.PassCount.ToString("N0")),
                ("FAIL (Mismatch)", summary.FailCount.ToString("N0")),
                ("Mismatch Rate", $"{summary.ExceptionRate:F2}%"),
                ("Computed Holidays", summary.HolidayCount.ToString("N0")),
                ("Computed Weekends", summary.WeekendCount.ToString("N0")),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;

                    if (label == "Status")
                    {
                        var color = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }

                summaryRow++;
            }

            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 60;

            var wsExceptions = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsExceptions, 1, "RULE 34 EXCEPTIONS", 10);
            for (int i = 0; i < resultHeaders.Length; i++)
            {
                var cell = wsExceptions.Cell(2, i + 1);
                cell.Value = resultHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var exceptionRow = 3;
            foreach (var row in summary.Exceptions)
            {
                wsExceptions.Cell(exceptionRow, 1).Value = row.ValidationNumber;
                wsExceptions.Cell(exceptionRow, 2).Value = row.FirstDayValue;
                wsExceptions.Cell(exceptionRow, 3).Value = row.LastDayValue;
                wsExceptions.Cell(exceptionRow, 4).Value = row.CurrentDays?.ToString() ?? "NULL";
                wsExceptions.Cell(exceptionRow, 5).Value = row.CurrentDaysHalf?.ToString() ?? "NULL";
                wsExceptions.Cell(exceptionRow, 6).Value = row.ComputedCensusDate;
                wsExceptions.Cell(exceptionRow, 7).Value = row.ActualCensusDate;
                wsExceptions.Cell(exceptionRow, 8).Value = row.CensusDateValue;
                wsExceptions.Cell(exceptionRow, 9).Value = row.DayStatus;
                wsExceptions.Cell(exceptionRow, 10).Value = row.ValidationStatus;
                wsExceptions.Range(exceptionRow, 1, exceptionRow, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                exceptionRow++;
            }

            for (int c = 1; c <= 10; c++) wsExceptions.Column(c).AdjustToContents();

            var wsStats = wb.Worksheets.Add("Statistics");
            StyleHeaderRow(wsStats, 1, "VALIDATION STATISTICS", 2);
            var stats = new[]
            {
                ("Total Records Validated", (object)summary.TotalValidated),
                ("PASS Count", (object)summary.PassCount),
                ("FAIL Count", (object)summary.FailCount),
                ("Mismatch Rate (%)", (object)(double)summary.ExceptionRate),
                ("Holiday Count", (object)summary.HolidayCount),
                ("Weekend Count", (object)summary.WeekendCount)
            };

            var statsRow = 2;
            foreach (var (label, value) in stats)
            {
                wsStats.Cell(statsRow, 1).Value = label;
                wsStats.Cell(statsRow, 1).Style.Font.Bold = true;
                wsStats.Cell(statsRow, 2).SetValue(value?.ToString());
                statsRow++;
            }

            wsStats.Column(1).Width = 30;
            wsStats.Column(2).Width = 20;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule35ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule35Headers(summary);

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 35: DUPLICATE CHECK ON dbo_CRSE", 2);

            var duplicateRule = string.IsNullOrWhiteSpace(summary.DuplicateColumn)
                ? "COUNT(selected field) = 1 => PASS | COUNT(selected field) > 1 => FAIL"
                : $"COUNT([{summary.DuplicateColumn}]) = 1 => PASS | COUNT([{summary.DuplicateColumn}]) > 1 => FAIL";

            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("Duplicate Field", summary.DuplicateColumn),
                ("Validation Rule", duplicateRule),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("VALIDATION RESULTS", ""),
                ("Total Validated", summary.TotalValidated.ToString("N0")),
                ("PASS (Unique Records)", summary.PassCount.ToString("N0")),
                ("FAIL (Duplicate Records)", summary.FailCount.ToString("N0")),
                ("Unique Values", summary.UniqueValues.ToString("N0")),
                ("Duplicate Values", summary.DuplicateValues.ToString("N0")),
                ("Records with Duplicates", summary.DuplicateRecords.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;

                    if (label == "Status")
                    {
                        var color = string.Equals(value, "PASS", StringComparison.OrdinalIgnoreCase)
                            ? XLColor.FromHtml("#C8E6C9")
                            : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 70;

            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 35 VALIDATION RESULTS", headers.Count);
            WriteRule35HeaderRow(wsResults, 2, headers);
            WriteRule35Rows(wsResults, 3, summary.ValidationRows, headers);

            var wsGroups = wb.Worksheets.Add("Duplicate Groups");
            StyleHeaderRow(wsGroups, 1, "RULE 35 DUPLICATE GROUPS", 2);
            wsGroups.Cell(2, 1).Value = "Duplicate Value";
            wsGroups.Cell(2, 2).Value = "Occurrence Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsGroups.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.DuplicateSummary.Count; i++)
            {
                var item = summary.DuplicateSummary[i];
                wsGroups.Cell(i + 3, 1).Value = item.Value;
                wsGroups.Cell(i + 3, 2).Value = item.Count;
            }
            wsGroups.Column(1).AdjustToContents();
            wsGroups.Column(2).AdjustToContents();

            var duplicateRows = summary.ValidationRows
                .Where(row => string.Equals(row.DuplicateStatus, "DUPLICATE", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (duplicateRows.Count > 0)
            {
                var wsExceptions = wb.Worksheets.Add("Exceptions");
                StyleHeaderRow(wsExceptions, 1, "RULE 35 DUPLICATE RECORDS", headers.Count);
                WriteRule35HeaderRow(wsExceptions, 2, headers);
                WriteRule35Rows(wsExceptions, 3, duplicateRows, headers);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule26ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 26: DBO_PROF TO PAYROLL_SAMPLE 4-CONTROL VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("PROF Table", summary.ProfTable),
                ("Payroll Table", summary.PayrollTable),
                ("Blank Payroll GROUP_NAME Pass Codes", summary.BlankPayrollGroupPassCodes),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("POPULATION SUMMARY", ""),
                ("PROF Records", summary.ProfRecordCount.ToString("N0")),
                ("Payroll Records", summary.PayrollRecordCount.ToString("N0")),
                ("Linked Records", summary.LinkedRecordCount.ToString("N0")),
                ("Total Control Tests", summary.TotalValidated.ToString("N0")),
                ("Total Exceptions", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "POPULATION SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 34;
            wsSummary.Column(2).Width = 72;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 26 CONTROL SUMMARY", 6);
            var controlHeaders = new[] { "Direction", "Control #", "Control Name", "Total Tested", "Exceptions", "Status" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControlSummary.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var controlRow = 3;
            foreach (var direction in summary.Directions)
            {
                foreach (var control in direction.Controls)
                {
                    wsControlSummary.Cell(controlRow, 1).Value = direction.DirectionLabel;
                    wsControlSummary.Cell(controlRow, 2).Value = control.ControlNumber;
                    wsControlSummary.Cell(controlRow, 3).Value = control.ControlName;
                    wsControlSummary.Cell(controlRow, 4).Value = control.TotalTested;
                    wsControlSummary.Cell(controlRow, 5).Value = control.ExceptionCount;
                    wsControlSummary.Cell(controlRow, 6).Value = control.Passed ? "PASS" : "FAIL";
                    controlRow++;
                }
            }
            for (var c = 1; c <= controlHeaders.Length; c++) wsControlSummary.Column(c).AdjustToContents();

            var wsEmployeeSummary = wb.Worksheets.Add("Employee Summary");
            WriteRule26EmployeeSummarySheet(wsEmployeeSummary, summary.Exceptions);

            var exceptionHeaders = new[] { "Direction", "Control", "Control Name", "Personnel Number", "Personnel Name", "Exception Reason", "Base Value", "Reference Value" };
            if (summary.Directions.Count <= 1)
            {
                var direction = summary.Directions.FirstOrDefault();
                var sheetName = direction?.DirectionKey == "prof_to_payroll" ? "PROF to Payroll" : direction?.DirectionLabel ?? "Exceptions";
                var title = direction == null ? "RULE 26 EXCEPTIONS" : $"{direction.DirectionLabel} EXCEPTIONS";
                WriteRule26ExceptionSheet(wb.Worksheets.Add(sheetName), title, exceptionHeaders, direction?.Exceptions ?? summary.Exceptions);
            }
            else
            {
                WriteRule26ExceptionSheet(wb.Worksheets.Add("Combined Exceptions"), "RULE 26 EXCEPTIONS", exceptionHeaders, summary.Exceptions);

                foreach (var direction in summary.Directions)
                {
                    var sheetName = direction.DirectionKey == "prof_to_payroll" ? "PROF to Payroll" : direction.DirectionLabel;
                    WriteRule26ExceptionSheet(wb.Worksheets.Add(sheetName), $"{direction.DirectionLabel} EXCEPTIONS", exceptionHeaders, direction.Exceptions);
                }
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteRule26ExceptionSheet(
            IXLWorksheet worksheet,
            string title,
            IReadOnlyList<string> headers,
            IEnumerable<Rule26ExceptionRowViewModel> exceptions)
        {
            StyleHeaderRow(worksheet, 1, title, headers.Count);
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = worksheet.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.WrapText = true;
            }

            var sortedExceptions = exceptions
                .OrderBy(exception => exception.ControlNumber)
                .ThenBy(exception => exception.PersonnelNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(exception => exception.PersonnelName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(exception => exception.ExceptionReason, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var row = 3;
            foreach (var exception in sortedExceptions)
            {
                worksheet.Cell(row, 1).Value = exception.DirectionLabel;
                worksheet.Cell(row, 2).Value = exception.ControlNumber;
                worksheet.Cell(row, 3).Value = exception.ControlName;
                worksheet.Cell(row, 4).Value = exception.PersonnelNumber;
                worksheet.Cell(row, 5).Value = exception.PersonnelName;
                worksheet.Cell(row, 6).Value = exception.ExceptionReason;
                worksheet.Cell(row, 7).Value = exception.BaseValue;
                worksheet.Cell(row, 8).Value = exception.ReferenceValue;
                worksheet.Range(row, 1, row, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                row++;
            }

            worksheet.SheetView.FreezeRows(2);
            worksheet.Range(2, 1, Math.Max(2, row - 1), headers.Count).SetAutoFilter();
            for (var c = 1; c <= headers.Count; c++) worksheet.Column(c).AdjustToContents();
        }

        private static void WriteRule26EmployeeSummarySheet(
            IXLWorksheet worksheet,
            IEnumerable<Rule26ExceptionRowViewModel> exceptions)
        {
            var headers = new[] { "Direction", "Personnel Number", "Personnel Name", "Exception Count", "Controls", "Exception Reasons" };
            StyleHeaderRow(worksheet, 1, "RULE 26 EMPLOYEE EXCEPTION SUMMARY", headers.Length);

            for (var i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var groupedExceptions = exceptions
                .GroupBy(
                    row => $"{row.DirectionLabel}|{row.PersonnelNumber}",
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.First().DirectionLabel)
                .ThenBy(group => group.First().PersonnelNumber);

            var rowIndex = 3;
            foreach (var group in groupedExceptions)
            {
                var firstRow = group.First();
                var personnelName = group
                    .Select(row => row.PersonnelName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                    ?? "";

                worksheet.Cell(rowIndex, 1).Value = firstRow.DirectionLabel;
                worksheet.Cell(rowIndex, 2).Value = firstRow.PersonnelNumber;
                worksheet.Cell(rowIndex, 3).Value = personnelName;
                worksheet.Cell(rowIndex, 4).Value = group.Count();
                worksheet.Cell(rowIndex, 5).Value = string.Join("; ",
                    group
                        .Select(row => $"C{row.ControlNumber} - {row.ControlName}")
                        .Distinct(StringComparer.OrdinalIgnoreCase));
                worksheet.Cell(rowIndex, 6).Value = string.Join(" | ",
                    group.Select(row => row.ExceptionReason).Distinct(StringComparer.OrdinalIgnoreCase));
                worksheet.Range(rowIndex, 1, rowIndex, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8F1");
                rowIndex++;
            }

            worksheet.SheetView.FreezeRows(2);
            worksheet.Range(2, 1, Math.Max(2, rowIndex - 1), headers.Length).SetAutoFilter();
            for (var c = 1; c <= headers.Length; c++) worksheet.Column(c).AdjustToContents();
        }

        public byte[] ExportExcel(Rule29ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule29Headers(summary);
            var sampleSheetName = summary.Sampled ? "Displayed Sample" : "Matching Records";

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 29: SINGLE COLUMN FILTER", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("Filter Column", summary.FilterColumn),
                ("Filter Value", summary.FilterValue),
                ("Breakdown Column", string.IsNullOrWhiteSpace(summary.BreakdownColumn) ? "(not selected)" : summary.BreakdownColumn),
                ("Sample Size", summary.SampleSize.ToString("N0")),
                ("Show All Records", summary.ShowAllRecords ? "Yes" : "No"),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("100% Source Records Tested", summary.TotalValidated.ToString("N0")),
                ("100% Matching Records Found", summary.MatchingCount.ToString("N0")),
                ("Displayed Sample Rows", summary.DisplayedCount.ToString("N0")),
                ("Displayed Result Type", summary.Sampled ? "Random sample of matching rows" : "All matching rows"),
                ("Download Scope", summary.Sampled ? "Summary + displayed sample rows + full breakdown" : "Summary + all matching rows + full breakdown"),
                ("Match Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            var wsMatches = wb.Worksheets.Add(sampleSheetName);
            StyleHeaderRow(wsMatches, 1, summary.Sampled ? "RULE 29 DISPLAYED RANDOM SAMPLE" : "RULE 29 MATCHING RECORDS", headers.Count);
            WriteRule29HeaderRow(wsMatches, 2, headers);
            WriteRule29Rows(wsMatches, 3, summary.MatchingRows, headers);

            var wsBreakdown = wb.Worksheets.Add("100% Breakdown");
            StyleHeaderRow(wsBreakdown, 1, "RULE 29 100% MATCHING BREAKDOWN", 2);
            wsBreakdown.Cell(2, 1).Value = string.IsNullOrWhiteSpace(summary.BreakdownColumn) ? "Breakdown Value" : summary.BreakdownColumn;
            wsBreakdown.Cell(2, 2).Value = "Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsBreakdown.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.Breakdown.Count; i++)
            {
                wsBreakdown.Cell(i + 3, 1).Value = summary.Breakdown[i].Value;
                wsBreakdown.Cell(i + 3, 2).Value = summary.Breakdown[i].Count;
            }
            wsBreakdown.Column(1).AdjustToContents();
            wsBreakdown.Column(2).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule21ValidationSummary summary) => ExportRule21Excel(summary);

        public byte[] ExportExcel(Rule17ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule17Headers();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 17: GRADUATE STUDENTS FULFILLED QUALIFICATION VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",               summary.Database),
                ("STUD Table",             summary.TableName),
                ("QUAL Table",             summary.QualTable),
                ("QUAL Join Column",       summary.QualJoinCol),
                ("QUAL Name Column",       summary.QualNameCol),
                ("Filter Column",          summary.FilterColumn),
                ("Filter Value",           summary.FilterValue),
                ("Validation Date",        summary.Timestamp),
                ("",                       ""),
                ("RESULT SUMMARY",         ""),
                ("Total Records Tested",   summary.TotalValidated.ToString("N0")),
                ("PASS Rows",              summary.PassCount.ToString("N0")),
                ("FAIL Rows",              summary.FailCount.ToString("N0")),
                ("PASS Rate",              $"{summary.ExceptionRate:F2}%"),
                ("Status",                 summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            var wsResults = wb.Worksheets.Add("Rule 17 Results");
            StyleHeaderRow(wsResults, 1, "RULE 17 — GRADUATE STUDENTS FULFILLED QUALIFICATION", headers.Count);
            WriteRule21HeaderRow(wsResults, 2, headers);
            WriteLegacyDynRows(wsResults, 3, summary.MatchingRows, headers);

            var wsBreakdown = wb.Worksheets.Add("Distinct Values");
            StyleHeaderRow(wsBreakdown, 1, "RULE 17 DISTINCT VALUES", 2);
            wsBreakdown.Cell(2, 1).Value = summary.FilterColumn;
            wsBreakdown.Cell(2, 2).Value = "Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsBreakdown.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.Breakdown.Count; i++)
            {
                wsBreakdown.Cell(i + 3, 1).Value = summary.Breakdown[i].Value;
                wsBreakdown.Cell(i + 3, 2).Value = summary.Breakdown[i].Count;
            }
            wsBreakdown.Column(1).AdjustToContents();
            wsBreakdown.Column(2).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule16ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule16Headers();
            var includeDetailedControlSheets = summary.ReviewRows.Count <= Rule18DetailedSheetRowThreshold;
            var exportNote = includeDetailedControlSheets
                ? (summary.Warning ?? "")
                : "Large export mode: the workbook keeps the full All Results sheet and skips duplicate control tabs to reduce preparation time.";

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 16: STUDENT POPULATION VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("STUD Table", summary.StudTable),
                ("Bridge Table", summary.BridgeTable),
                ("CRSE Table", summary.CrseTable),
                ("Validation Date", summary.Timestamp),
                ("Join Path", summary.TableLinkageText),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Unfulfilled Qualification Population", summary.UnfulfilledPopulationCount.ToString("N0")),
                ("Control Result Rows", summary.TotalValidated.ToString("N0")),
                ("Matching Rows", summary.PassCount.ToString("N0")),
                ("Non-Matching Rows", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Export Note", exportNote)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 36;
            wsSummary.Column(2).Width = 70;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 16 CONTROL SUMMARY", 6);
            var controlHeaders = new[] { "Control", "Criteria", "Matched Rows", "PASS", "FAIL", "Status" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControlSummary.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsControlSummary.Cell(i + 3, 1).Value = item.ControlLabel;
                wsControlSummary.Cell(i + 3, 2).Value = item.CriteriaText;
                wsControlSummary.Cell(i + 3, 3).Value = item.TotalCount;
                wsControlSummary.Cell(i + 3, 4).Value = item.PassCount;
                wsControlSummary.Cell(i + 3, 5).Value = item.FailCount;
                wsControlSummary.Cell(i + 3, 6).Value = item.Status;
            }
            for (var c = 1; c <= 6; c++)
                wsControlSummary.Column(c).AdjustToContents();

            var distVal = string.IsNullOrWhiteSpace(summary.DistanceVal) ? "D" : summary.DistanceVal;

            var distanceRows = summary.ReviewRows
                .Where(r => { r.DisplayValues.TryGetValue("Attendance_Mode", out var v); return string.Equals(v?.Trim(), distVal, StringComparison.OrdinalIgnoreCase); })
                .ToList();
            var normalRows = summary.ReviewRows
                .Where(r => { r.DisplayValues.TryGetValue("Attendance_Mode", out var v); return !string.Equals(v?.Trim(), distVal, StringComparison.OrdinalIgnoreCase); })
                .ToList();

            // Control 1: all rows
            var wsC1 = wb.Worksheets.Add("Control 1 - All");
            WriteRule16ControlSheet(wb, null, "RULE 16 CONTROL 1 — ALL POPULATION", summary.ReviewRows, headers, wsC1);
            // Control 2: distance students
            var wsC2 = wb.Worksheets.Add("Control 2 - Distance");
            WriteRule16ControlSheet(wb, null, "RULE 16 CONTROL 2 — DISTANCE LEARNING", distanceRows, headers, wsC2);
            // Control 3: non-distance students
            var wsC3 = wb.Worksheets.Add("Control 3 - Normal");
            WriteRule16ControlSheet(wb, null, "RULE 16 CONTROL 3 — NORMAL STUDENTS", normalRows, headers, wsC3);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule15ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule15Headers();

            // ── Sheet 1: Summary ─────────────────────────────────────────────────
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 15: COURSE CREDENTIALS VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",              summary.Database),
                ("CRED Table",            summary.CredTable),
                ("QUAL Table",            summary.QualTable),
                ("CREG Table",            summary.RegistrationTable),
                ("Validation Date",       summary.Timestamp),
                ("Join Path",             summary.TableLinkageText),
                ("Validation Mode",       summary.RuleModeText),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Approved Qualifications",      summary.ApprovedQualificationCount.ToString("N0")),
                ("Total CRED Rows (population)", summary.ApprovedCredentialCount.ToString("N0")),
                ("PASS (matched in CREG)",       summary.PassCount.ToString("N0")),
                ("FAIL (not matched in CREG)",   summary.FailCount.ToString("N0")),
                ("Exception Rate",               $"{summary.ExceptionRate:F2}%"),
                ("Overall Status",               summary.Status)
            };

            var sr = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hc = wsSummary.Cell(sr, 1);
                    hc.Value = label;
                    hc.Style.Font.Bold = true;
                    hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hc.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(sr, 1, sr, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(sr, 1).Value = label;
                    wsSummary.Cell(sr, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sr, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sr, 2).Value = value;
                }
                sr++;
            }
            wsSummary.Column(1).Width = 38;
            wsSummary.Column(2).Width = 70;

            // ── Sheet 2: Control Summary ──────────────────────────────────────────
            var wsCS = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsCS, 1, "RULE 15 CONTROL SUMMARY", 6);
            var ctrlHdrs = new[] { "Control", "Criteria", "Rows Tested", "PASS", "FAIL", "Status" };
            for (var i = 0; i < ctrlHdrs.Length; i++)
            {
                var cell = wsCS.Cell(2, i + 1);
                cell.Value = ctrlHdrs[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsCS.Cell(i + 3, 1).Value = item.ControlLabel;
                wsCS.Cell(i + 3, 2).Value = item.CriteriaText;
                wsCS.Cell(i + 3, 3).Value = item.TotalCount;
                wsCS.Cell(i + 3, 4).Value = item.PassCount;
                wsCS.Cell(i + 3, 5).Value = item.FailCount;
                wsCS.Cell(i + 3, 6).Value = item.Status;
            }
            for (var c = 1; c <= 6; c++) wsCS.Column(c).AdjustToContents();

            // ── Helper: get DisplayValues value ───────────────────────────────────
            static string DV(Rule15ValidationRowRecord row, string key) =>
                row.DisplayValues.TryGetValue(key, out var v) ? v ?? "" : "";

            // ── Sheet 3: CRED Population (Step 1) ────────────────────────────────
            var wsCred = wb.Worksheets.Add("CRED Population");
            StyleHeaderRow(wsCred, 1, $"STEP 1 — {summary.CredTable} EXTRACTED POPULATION", 6);
            var credPopHdrs = new[] { "Extract_Number", "Qualification_Code", "Course_Code", "Filler1", "Course_Level_Credit_Value", "Completed_Research_Course_Credit_Value" };
            for (var i = 0; i < credPopHdrs.Length; i++)
            {
                var cell = wsCred.Cell(2, i + 1);
                cell.Value = credPopHdrs[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            for (var i = 0; i < summary.ReviewRows.Count; i++)
            {
                var row = summary.ReviewRows[i];
                wsCred.Cell(i + 3, 1).Value = DV(row, "Extract_Number");
                wsCred.Cell(i + 3, 2).Value = DV(row, "Qualification_Code");
                wsCred.Cell(i + 3, 3).Value = DV(row, "Course_Code");
                wsCred.Cell(i + 3, 4).Value = DV(row, "Filler1");
                wsCred.Cell(i + 3, 5).Value = DV(row, "Course_Level_Credit_Value");
                wsCred.Cell(i + 3, 6).Value = DV(row, "Completed_Research_Course_Credit_Value");
            }
            for (var c = 1; c <= 6; c++) wsCred.Column(c).AdjustToContents();

            // ── Sheet 4: Approved QUAL (Step 2) ──────────────────────────────────
            var wsQual = wb.Worksheets.Add("Approved QUAL");
            StyleHeaderRow(wsQual, 1, $"STEP 2 — {summary.QualTable} APPROVED QUALIFICATIONS", 3);
            var qualHdrs = new[] { "Qualification_Code", "Qualification_Name_Designator", "Approval_Status" };
            for (var i = 0; i < qualHdrs.Length; i++)
            {
                var cell = wsQual.Cell(2, i + 1);
                cell.Value = qualHdrs[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            var qualSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var qualRow = 3;
            foreach (var row in summary.ReviewRows)
            {
                var qCode = DV(row, "Qualification_Code");
                if (!qualSeen.Add(qCode)) continue;
                if (string.IsNullOrEmpty(DV(row, "Approval_Status"))) continue;
                wsQual.Cell(qualRow, 1).Value = qCode;
                wsQual.Cell(qualRow, 2).Value = DV(row, "Qualification_Name_Designator");
                wsQual.Cell(qualRow, 3).Value = DV(row, "Approval_Status");
                qualRow++;
            }
            for (var c = 1; c <= 3; c++) wsQual.Column(c).AdjustToContents();

            // ── Sheet 5: CREG Population (Step 3) ────────────────────────────────
            var wsCreg = wb.Worksheets.Add("CREG Population");
            StyleHeaderRow(wsCreg, 1, $"STEP 3 — {summary.RegistrationTable} REGISTRATION POPULATION", 4);
            var cregHdrs = new[] { "Qualification_Code", "Course_Code", "First_Student_Number", "Registered_Student_Count" };
            for (var i = 0; i < cregHdrs.Length; i++)
            {
                var cell = wsCreg.Cell(2, i + 1);
                cell.Value = cregHdrs[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            var cregRowIdx = 3;
            foreach (var row in summary.ReviewRows.Where(r => string.Equals(DV(r, "CREG__MATCH"), "FOUND", StringComparison.OrdinalIgnoreCase)))
            {
                wsCreg.Cell(cregRowIdx, 1).Value = DV(row, "Qualification_Code");
                wsCreg.Cell(cregRowIdx, 2).Value = DV(row, "Course_Code");
                wsCreg.Cell(cregRowIdx, 3).Value = DV(row, "First_Student_Number");
                wsCreg.Cell(cregRowIdx, 4).Value = DV(row, "Registered_Student_Count");
                cregRowIdx++;
            }
            for (var c = 1; c <= 4; c++) wsCreg.Column(c).AdjustToContents();

            // ── Sheet 6: All Validation Results (Step 6) ─────────────────────────
            var wsAll = wb.Worksheets.Add("All Results");
            StyleHeaderRow(wsAll, 1, "STEP 6 — RULE 15 FULL VALIDATION RESULTS", headers.Count);
            WriteRule15HeaderRow(wsAll, 2, headers);
            WriteRule15Rows(wsAll, 3, summary.ReviewRows, headers);

            // ── Sheet 7: Exceptions Only (Step 7) ────────────────────────────────
            var exceptions = summary.ReviewRows
                .Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            WriteRule15ControlSheet(wb, "Exceptions", "STEP 7 — RULE 15 EXCEPTIONS (FAIL ONLY)", exceptions, headers);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule14ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule14Headers();
            var includeDetailedControlSheets = summary.ReviewRows.Count <= Rule18DetailedSheetRowThreshold;
            var exportNote = includeDetailedControlSheets
                ? (summary.Warning ?? "")
                : "Large export mode: the workbook keeps the full All Results sheet and skips duplicate control tabs to reduce preparation time.";

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 14: COURSE REGISTRATION VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("CRSE Table", summary.StudTable),
                ("CREG Table", summary.BridgeTable),
                ("Validation Date", summary.Timestamp),
                ("Join Path", summary.TableLinkageText),
                ("Validation Mode", summary.RuleModeText),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Approved Courses", summary.ApprovedCourseCount.ToString("N0")),
                ("Registered Courses", summary.RegisteredCourseCount.ToString("N0")),
                ("Missing Registrations", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Export Note", exportNote)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 36;
            wsSummary.Column(2).Width = 70;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 14 CONTROL SUMMARY", 6);
            var controlHeaders = new[] { "Control", "Criteria", "Rows Tested", "PASS", "FAIL", "Status" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControlSummary.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsControlSummary.Cell(i + 3, 1).Value = item.ControlLabel;
                wsControlSummary.Cell(i + 3, 2).Value = item.CriteriaText;
                wsControlSummary.Cell(i + 3, 3).Value = item.TotalCount;
                wsControlSummary.Cell(i + 3, 4).Value = item.PassCount;
                wsControlSummary.Cell(i + 3, 5).Value = item.FailCount;
                wsControlSummary.Cell(i + 3, 6).Value = item.Status;
            }
            for (var c = 1; c <= 6; c++)
                wsControlSummary.Column(c).AdjustToContents();

            var wsAll = wb.Worksheets.Add("All Results");
            StyleHeaderRow(wsAll, 1, "RULE 14 ALL RESULTS", headers.Count);
            WriteRule14HeaderRow(wsAll, 2, headers);
            WriteRule14Rows(wsAll, 3, summary.ReviewRows, headers);

            if (includeDetailedControlSheets)
            {
                WriteRule14ControlSheet(wb, "Control1", "RULE 14 CONTROL 1", summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_1", StringComparison.OrdinalIgnoreCase)).ToList(), headers);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule18ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule18Headers();
            var exportNote = summary.Warning ?? "";

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 18: NSFAS STUDENT VALIDATION", 2);
            var summaryData = new[]
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
                ("Export Note", exportNote)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 18 CONTROL SUMMARY", 6);
            var controlHeaders = new[] { "Control", "Criteria", "Matched Rows", "PASS", "FAIL", "Status" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControlSummary.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsControlSummary.Cell(i + 3, 1).Value = item.ControlLabel;
                wsControlSummary.Cell(i + 3, 2).Value = item.CriteriaText;
                wsControlSummary.Cell(i + 3, 3).Value = item.TotalCount;
                wsControlSummary.Cell(i + 3, 4).Value = item.PassCount;
                wsControlSummary.Cell(i + 3, 5).Value = item.FailCount;
                wsControlSummary.Cell(i + 3, 6).Value = item.Status;
            }
            for (var c = 1; c <= 6; c++)
                wsControlSummary.Column(c).AdjustToContents();

            WriteRule18ControlSheet(wb, "Control 1 - NSFAS Foundation",
                "RULE 18 CONTROL 1: NSFAS + FOUNDATION",
                summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_1", StringComparison.OrdinalIgnoreCase)).ToList(), headers);
            WriteRule18ControlSheet(wb, "Control 2 - Distance",
                "RULE 18 CONTROL 2: NSFAS + FOUNDATION + DISTANCE",
                summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_2", StringComparison.OrdinalIgnoreCase)).ToList(), headers);
            WriteRule18ControlSheet(wb, "Control 3 - Non-Foundation",
                "RULE 18 CONTROL 3: NSFAS + NOT FOUNDATION + NOT DISTANCE",
                summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_3", StringComparison.OrdinalIgnoreCase)).ToList(), headers);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule19ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var allRows = summary.MatchingRows;
            var hasCredCrse = !string.IsNullOrWhiteSpace(summary.CredTable) && !string.IsNullOrWhiteSpace(summary.CrseTable);
            var hasPqm = !string.IsNullOrWhiteSpace(summary.PqmTable);

            // Build ordered headers from SQL column sequence
            var headers = new List<string>
            {
                "#", "Control_Type", "Student_Qualification_Code", "Student_Number",
                "South_African_ID_Number", "Entrance_Category", "Date_Of_Birth",
                "Gender", "Race", "Nationality", "NSFAS_Status", "Previous_Years_Activity",
                "Attendance_Mode", "Qualification_Fulfilled_Indicator",
                "Student_Last_Name", "Student_First_Name", "Student_Middle_Name",
                "Percentage_Research_Curriculum",
                "QUAL_Qualification_Code", "Qualification_Name", "Qualification_Type"
            };
            if (hasCredCrse)
                headers.AddRange(new[] { "CRED_Qualification_Code", "CRED_Course_Code", "CRSE_Course_Code", "Course_Name", "Foundation_Course_Indicator" });
            if (hasPqm)
                headers.AddRange(new[] { "PQM_Qualification_Name", "PQM_Qualification_Type" });
            headers.Add("STUD_QUAL_Link_Check");
            if (hasCredCrse)
                headers.AddRange(new[] { "STUD_CRED_Link_Check", "CRED_CRSE_Link_Check" });
            headers.Add("Rule19_Filter_Check");
            headers.Add("PQM_Validation_Result");

            var passRows = allRows.Where(r => string.Equals(r.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase)).ToList();
            var failRows = allRows.Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();

            void WriteHeaders(IXLWorksheet ws, int row)
            {
                for (var c = 0; c < headers.Count; c++)
                {
                    var cell = ws.Cell(row, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    cell.Style.Font.FontColor = XLColor.White;
                }
            }

            void WriteRows(IXLWorksheet ws, int startRow, IEnumerable<Rule19ValidationRowRecord> rows, string? rowColor = null)
            {
                var rowIdx = startRow;
                foreach (var r in rows)
                {
                    var dv = r.DisplayValues;
                    ws.Cell(rowIdx, 1).Value = r.ValidationNumber;
                    for (var ci = 1; ci < headers.Count; ci++)
                    {
                        var colName = headers[ci];
                        var val = dv.TryGetValue(colName, out var v) ? (v ?? "") : "";
                        ws.Cell(rowIdx, ci + 1).Value = val;
                        var upper = val.ToUpperInvariant();
                        if ((colName == "PQM_Validation_Result" || colName == "Rule19_Filter_Check") && (upper == "PASS" || upper == "FAIL"))
                            ws.Cell(rowIdx, ci + 1).Style.Font.FontColor =
                                upper == "PASS" ? XLColor.FromHtml("#0f766e") : XLColor.FromHtml("#b91c1c");
                    }
                    if (rowColor != null)
                        ws.Range(rowIdx, 1, rowIdx, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(rowColor);
                    rowIdx++;
                }
            }

            // Summary sheet
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 19: MASTERS & PHD POPULATION", 2);
            var summaryData = new List<(string Label, string Value)>
            {
                ("Database", summary.Database),
                ("STUD Table", summary.StudTable),
                ("QUAL Table", summary.QualTable),
                ("Qual Code Column", summary.QualCodeColumn),
                ("Fulfilled Column", summary.FulfilledColumn),
                ("Fulfilled Value", summary.FulfilledValue),
                ("Qual Type Column", summary.QualTypeColumn),
                ("MD Types", summary.MdTypesText),
                ("CRED Table", string.IsNullOrWhiteSpace(summary.CredTable) ? "(not configured)" : summary.CredTable),
                ("CRSE Table", string.IsNullOrWhiteSpace(summary.CrseTable) ? "(not configured)" : summary.CrseTable),
                ("PQM Table", string.IsNullOrWhiteSpace(summary.PqmTable) ? "(not configured)" : summary.PqmTable),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Total Validated", summary.TotalValidated.ToString("N0")),
                ("Pass Count (PQM)", summary.PassCount.ToString("N0")),
                ("Fail Count (PQM)", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };
            var sRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdr = wsSummary.Cell(sRow, 1);
                    hdr.Value = label;
                    hdr.Style.Font.Bold = true;
                    hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdr.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(sRow, 1, sRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(sRow, 1).Value = label;
                    wsSummary.Cell(sRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sRow, 2).Value = value;
                    if (label == "Status")
                        wsSummary.Cell(sRow, 2).Style.Fill.BackgroundColor =
                            value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                }
                sRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 60;

            // Control 1 — All Results sheet
            var wsAll = wb.Worksheets.Add("Control 1 - All Results");
            StyleHeaderRow(wsAll, 1, "RULE 19 CONTROL 1: MASTERS & PHD POPULATION — ALL RESULTS", headers.Count);
            WriteHeaders(wsAll, 2);
            WriteRows(wsAll, 3, allRows);
            for (var c = 1; c <= headers.Count; c++) wsAll.Column(c).AdjustToContents();

            // PASS sheet
            var wsPass = wb.Worksheets.Add("PASS — In Population");
            StyleHeaderRow(wsPass, 1, "RULE 19 — PASS: PQM Validation Matched", headers.Count);
            WriteHeaders(wsPass, 2);
            WriteRows(wsPass, 3, passRows, "#F3FFF3");
            for (var c = 1; c <= headers.Count; c++) wsPass.Column(c).AdjustToContents();

            // FAIL sheet
            var wsFail = wb.Worksheets.Add("FAIL — Exceptions");
            StyleHeaderRow(wsFail, 1, "RULE 19 — FAIL: PQM Validation Not Matched", headers.Count);
            WriteHeaders(wsFail, 2);
            WriteRows(wsFail, 3, failRows, "#FFF3F3");
            for (var c = 1; c <= headers.Count; c++) wsFail.Column(c).AdjustToContents();

            // Breakdown sheet
            if (summary.Breakdown.Count > 0)
            {
                var wsBd = wb.Worksheets.Add("Qual Type Breakdown");
                StyleHeaderRow(wsBd, 1, "RULE 19 — QUAL TYPE BREAKDOWN", 2);
                wsBd.Cell(2, 1).Value = "Qual Type";
                wsBd.Cell(2, 2).Value = "Count";
                for (var c = 1; c <= 2; c++)
                {
                    wsBd.Cell(2, c).Style.Font.Bold = true;
                    wsBd.Cell(2, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    wsBd.Cell(2, c).Style.Font.FontColor = XLColor.White;
                }
                for (var i = 0; i < summary.Breakdown.Count; i++)
                {
                    wsBd.Cell(i + 3, 1).Value = summary.Breakdown[i].Value;
                    wsBd.Cell(i + 3, 2).Value = summary.Breakdown[i].Count;
                }
                wsBd.Column(1).AdjustToContents();
                wsBd.Column(2).AdjustToContents();
            }

            using var ms = new System.IO.MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule25ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule25Headers();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 25: RECONCILE COURSE DATASETS", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("CRSE Table", summary.CrseTable),
                ("Audit Table", summary.AuditTable),
                ("H16 Table", summary.H16Table),
                ("CRSE Course Code Column", summary.CrseCourseCodeColumn),
                ("Audit Course Code Column", summary.AuditCourseCodeColumn),
                ("H16 Course Code Column", summary.H16CourseCodeColumn),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("CRSE Records", summary.CrseCount.ToString("N0")),
                ("Audit Records", summary.AuditCount.ToString("N0")),
                ("H16 Records", summary.H16Count.ToString("N0")),
                ("Total Reconciled Rows", summary.TotalValidated.ToString("N0")),
                ("Matched Rows", summary.Matches.ToString("N0")),
                ("Mismatch Rows", summary.Mismatches.ToString("N0")),
                ("Pass Sample Saved", summary.PassSampleCount.ToString("N0")),
                ("Mismatch Rate", $"{summary.ExceptionRate:F2}%"),
                ("Match Rate", $"{summary.MatchRate:F2}%"),
                ("Status", summary.Status),
                ("Warning", summary.Warning ?? "")
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                    if (label == "Status")
                    {
                        var color = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 70;

            var wsPass = wb.Worksheets.Add("Pass Sample");
            StyleHeaderRow(wsPass, 1, "RULE 25 PASS SAMPLE", headers.Count);
            WriteRule25HeaderRow(wsPass, 2, headers);
            WriteRule25Rows(wsPass, 3, summary.PassSampleRows, headers, "#F3FFF3");

            var wsFail = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsFail, 1, "RULE 25 FAIL EXCEPTIONS", headers.Count);
            WriteRule25HeaderRow(wsFail, 2, headers);
            WriteRule25Rows(wsFail, 3, summary.FailRows, headers, "#FFF3F3");

            var wsBreakdown = wb.Worksheets.Add("Issue Breakdown");
            StyleHeaderRow(wsBreakdown, 1, "RULE 25 ISSUE BREAKDOWN", 2);
            wsBreakdown.Cell(2, 1).Value = "Status";
            wsBreakdown.Cell(2, 2).Value = "Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsBreakdown.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.IssueCounts.Count; i++)
            {
                wsBreakdown.Cell(i + 3, 1).Value = summary.IssueCounts[i].Status;
                wsBreakdown.Cell(i + 3, 2).Value = summary.IssueCounts[i].Count;
            }
            wsBreakdown.Column(1).AdjustToContents();
            wsBreakdown.Column(2).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule23ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule23Headers();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 23: RECONCILE DATASETS", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("STUD Table", summary.StudTable),
                ("Audit Table", summary.AuditTable),
                ("H16 Table", summary.H16Table),
                ("STUD Student # Column", summary.StudStudentNumberColumn),
                ("STUD Qualification Column", summary.StudQualificationColumn),
                ("STUD ID Column", summary.StudIdNumberColumn),
                ("Audit Student # Column", summary.AuditStudentNumberColumn),
                ("Audit Qualification Column", summary.AuditQualificationColumn),
                ("Audit ID Column", summary.AuditIdNumberColumn),
                ("H16 Student # Column", summary.H16StudentNumberColumn),
                ("H16 Qualification Column", summary.H16QualificationColumn),
                ("H16 ID Column", summary.H16IdNumberColumn),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("STUD Records", summary.StudCount.ToString("N0")),
                ("Audit Records", summary.AuditCount.ToString("N0")),
                ("H16 Records", summary.H16Count.ToString("N0")),
                ("Total Reconciled Rows", summary.TotalValidated.ToString("N0")),
                ("Matched Rows", summary.Matches.ToString("N0")),
                ("Mismatch Rows", summary.Mismatches.ToString("N0")),
                ("Pass Sample Saved", summary.PassSampleCount.ToString("N0")),
                ("Fail Rows Saved", summary.SavedFailRowCount.ToString("N0")),
                ("Mismatch Rate", $"{summary.ExceptionRate:F2}%"),
                ("Match Rate", $"{summary.MatchRate:F2}%"),
                ("Status", summary.Status),
                ("Warning", summary.Warning ?? "")
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                    if (label == "Status")
                    {
                        var color = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 70;

            var wsPass = wb.Worksheets.Add("Pass Sample");
            StyleHeaderRow(wsPass, 1, "RULE 23 PASS SAMPLE", headers.Count);
            WriteRule23HeaderRow(wsPass, 2, headers);
            WriteRule23Rows(wsPass, 3, summary.PassSampleRows, headers, "#F3FFF3");

            var wsFail = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsFail, 1, "RULE 23 FAIL EXCEPTIONS", headers.Count);
            WriteRule23HeaderRow(wsFail, 2, headers);
            WriteRule23Rows(wsFail, 3, summary.FailRows, headers, "#FFF3F3");

            var wsBreakdown = wb.Worksheets.Add("Issue Breakdown");
            StyleHeaderRow(wsBreakdown, 1, "RULE 23 ISSUE BREAKDOWN", 2);
            wsBreakdown.Cell(2, 1).Value = "Status";
            wsBreakdown.Cell(2, 2).Value = "Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsBreakdown.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.IssueCounts.Count; i++)
            {
                wsBreakdown.Cell(i + 3, 1).Value = summary.IssueCounts[i].Status;
                wsBreakdown.Cell(i + 3, 2).Value = summary.IssueCounts[i].Count;
            }
            wsBreakdown.Column(1).AdjustToContents();
            wsBreakdown.Column(2).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule13ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 13: CESM QUALIFICATION POPULATION VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("CESM Table", summary.StudTable),
                ("QUAL Table", summary.QualTable),
                ("STUD Table", summary.CregTable),
                ("Linkage Used", summary.TableLinkageText),
                ("Overall PASS Rule", summary.OverallStatusRuleText),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Qualifying CESM Population", summary.FoundationStudentCount.ToString("N0")),
                ("Validated Qualifications", summary.TotalValidated.ToString("N0")),
                ("PASS Qualifications", summary.PassCount.ToString("N0")),
                ("FAIL Qualifications", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Warning", summary.Warning ?? "")
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                    if (label == "Status")
                    {
                        var color = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }
                summaryRow++;
            }

            if (summary.ProcedureSteps.Any())
            {
                wsSummary.Cell(summaryRow, 1).Value = "PROCEDURE PERFORMED";
                wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                wsSummary.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.White;
                wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                summaryRow++;

                for (var i = 0; i < summary.ProcedureSteps.Count; i++)
                {
                    wsSummary.Cell(summaryRow, 1).Value = $"Step {i + 1}";
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = summary.ProcedureSteps[i];
                    summaryRow++;
                }
            }

            wsSummary.Column(1).Width = 34;
            wsSummary.Column(2).Width = 80;

            var headers = new[]
            {
                "Extract_Number",
                "Qualification_Code",
                "Major_Field_CESM (_006)",
                "Qualification_Name_Designator (_003)",
                "Qualification_Approval_Status",
                "Qualification_Type_Descriptor",
                "Legacy_Indicator",
                "NQF_Exit_Level",
                "Minimum_Total_Credits",
                "Linked_Student_Count",
                "STUD_Link_Result",
                "PQM_CESM_Code",
                "PQM_Authorised_Name",
                "Name_Match",
                "CESM_Code_Match",
                "PQM_Needs_Review",
                "PQM_Result",
                "PQM_Exception_Reason",
                "Validation_Result",
                "Validation_Reason"
            };

            void WriteR13HeaderRow(IXLWorksheet ws)
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(2, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.WrapText = true;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }
                ws.Row(2).Height = 36;
            }

            void WriteR13DataRow(IXLWorksheet ws, int rowIndex, Rule13ReviewRowViewModel row)
            {
                var isPass   = string.Equals(row.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase);
                var rowBg    = isPass
                    ? (row.PqmNeedsReview ? XLColor.FromHtml("#FFF8E1") : XLColor.FromHtml("#F3FFF3"))
                    : XLColor.FromHtml("#FFF3F3");
                var matchBg    = XLColor.FromHtml("#E8F5E9");
                var mismatchBg = XLColor.FromHtml("#FFEBEE");
                var emptyBg    = XLColor.FromHtml("#FFFDE7");

                bool hasPqmCode = !string.IsNullOrWhiteSpace(row.PqmCode);
                bool hasPqmName = !string.IsNullOrWhiteSpace(row.PqmName);

                var isStudPass = string.Equals(row.StudLinkResult, "PASS", StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(row.StudLinkResult) && row.StudLinkCount > 0);
                var studLinkDisplay = string.IsNullOrWhiteSpace(row.StudLinkResult)
                    ? (row.StudLinkCount > 0 ? "PASS" : "FAIL")
                    : row.StudLinkResult;

                ws.Cell(rowIndex, 1).Value  = row.ValidationNumber;
                ws.Cell(rowIndex, 2).Value  = row.QualificationCode001;
                ws.Cell(rowIndex, 3).Value  = row.CourseCode030;               // Major_Field_CESM (_006)
                ws.Cell(rowIndex, 4).Value  = row.QualificationDescription003; // Qualification_Name_Designator
                ws.Cell(rowIndex, 5).Value  = row.FoundationFlag106;           // Qualification_Approval_Status
                ws.Cell(rowIndex, 6).Value  = row.QualificationType005;        // Qualification_Type_Descriptor
                ws.Cell(rowIndex, 7).Value  = row.BridgeQualificationCode001;  // Legacy_Indicator
                ws.Cell(rowIndex, 8).Value  = row.CrseCourseCode030;           // NQF_Exit_Level
                ws.Cell(rowIndex, 9).Value  = row.FoundationCourse091;         // Minimum_Total_Credits
                ws.Cell(rowIndex, 10).Value = row.StudLinkCount > 0 ? row.StudLinkCount : (int.TryParse(row.StudentType, out var sc) ? sc : 0); // Linked_Student_Count
                ws.Cell(rowIndex, 11).Value = studLinkDisplay;                 // STUD_Link_Result
                ws.Cell(rowIndex, 12).Value = hasPqmCode ? row.PqmCode : "Not found in PQM"; // PQM_CESM_Code
                ws.Cell(rowIndex, 13).Value = hasPqmName ? row.PqmName : "Not found in PQM"; // PQM_Authorised_Name
                ws.Cell(rowIndex, 14).Value = row.PqmNameMatch ? "YES" : (hasPqmName ? "NO" : "—"); // Name_Match
                ws.Cell(rowIndex, 15).Value = row.PqmCodeMatch ? "YES" : (hasPqmCode ? "NO" : "—"); // CESM_Code_Match
                ws.Cell(rowIndex, 16).Value = row.PqmNeedsReview ? "YES" : "";
                ws.Cell(rowIndex, 17).Value = row.PqmResult ?? "";
                ws.Cell(rowIndex, 18).Value = row.PqmExceptionReason ?? "";
                ws.Cell(rowIndex, 19).Value = row.ValidationResult;
                ws.Cell(rowIndex, 20).Value = row.ValidationExplanation ?? "";

                ws.Range(rowIndex, 1, rowIndex, headers.Length).Style.Fill.BackgroundColor = rowBg;

                // STUD_Link_Result highlight
                ws.Cell(rowIndex, 11).Style.Font.Bold = true;
                ws.Cell(rowIndex, 11).Style.Font.FontColor = isStudPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");
                ws.Cell(rowIndex, 11).Style.Fill.BackgroundColor = isStudPass ? XLColor.FromHtml("#E8F5E9") : XLColor.FromHtml("#FFEBEE");
                ws.Cell(rowIndex, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // PQM code cell highlights
                ws.Cell(rowIndex, 12).Style.Fill.BackgroundColor = hasPqmCode ? rowBg : emptyBg;
                ws.Cell(rowIndex, 13).Style.Fill.BackgroundColor = hasPqmName ? rowBg : emptyBg;
                ws.Cell(rowIndex, 14).Style.Fill.BackgroundColor = row.PqmNameMatch ? matchBg : (hasPqmName ? mismatchBg : emptyBg);
                ws.Cell(rowIndex, 14).Style.Font.Bold = true;
                ws.Cell(rowIndex, 14).Style.Font.FontColor = row.PqmNameMatch
                    ? XLColor.FromHtml("#1B5E20") : (hasPqmName ? XLColor.FromHtml("#B71C1C") : XLColor.FromHtml("#B45309"));
                ws.Cell(rowIndex, 14).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(rowIndex, 15).Style.Fill.BackgroundColor = row.PqmCodeMatch ? matchBg : (hasPqmCode ? mismatchBg : emptyBg);
                ws.Cell(rowIndex, 15).Style.Font.Bold = true;
                ws.Cell(rowIndex, 15).Style.Font.FontColor = row.PqmCodeMatch
                    ? XLColor.FromHtml("#1B5E20") : (hasPqmCode ? XLColor.FromHtml("#B71C1C") : XLColor.FromHtml("#B45309"));
                ws.Cell(rowIndex, 15).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // PQM_Result
                ws.Cell(rowIndex, 17).Style.Font.Bold = true;
                if (!string.IsNullOrWhiteSpace(row.PqmResult))
                    ws.Cell(rowIndex, 17).Style.Font.FontColor = string.Equals(row.PqmResult, "PASS", StringComparison.OrdinalIgnoreCase)
                        ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");

                // Validation_Result
                ws.Cell(rowIndex, 19).Style.Font.Bold = true;
                ws.Cell(rowIndex, 19).Style.Font.FontColor = isPass
                    ? (row.PqmNeedsReview ? XLColor.FromHtml("#B45309") : XLColor.FromHtml("#1B5E20"))
                    : XLColor.FromHtml("#B71C1C");

                ws.Cell(rowIndex, 13).Style.Alignment.WrapText = true;
                ws.Cell(rowIndex, 18).Style.Alignment.WrapText = true;
                ws.Cell(rowIndex, 20).Style.Alignment.WrapText = true;
                ws.Row(rowIndex).Height = string.IsNullOrWhiteSpace(row.PqmExceptionReason) ? 18 : 36;
            }

            void SetR13ColWidths(IXLWorksheet ws)
            {
                ws.Column(1).Width  = 6;   // Extract_Number
                ws.Column(2).Width  = 14;  // Qualification_Code
                ws.Column(3).Width  = 16;  // Major_Field_CESM (_006)
                ws.Column(4).Width  = 46;  // Qualification_Name_Designator (_003)
                ws.Column(5).Width  = 14;  // Qualification_Approval_Status
                ws.Column(6).Width  = 14;  // Qualification_Type_Descriptor
                ws.Column(7).Width  = 14;  // Legacy_Indicator
                ws.Column(8).Width  = 12;  // NQF_Exit_Level
                ws.Column(9).Width  = 16;  // Minimum_Total_Credits
                ws.Column(10).Width = 14;  // Linked_Student_Count
                ws.Column(11).Width = 14;  // STUD_Link_Result
                ws.Column(12).Width = 16;  // PQM_CESM_Code
                ws.Column(13).Width = 46;  // PQM_Authorised_Name
                ws.Column(14).Width = 10;  // Name_Match
                ws.Column(15).Width = 10;  // CESM_Code_Match
                ws.Column(16).Width = 10;  // PQM_Needs_Review
                ws.Column(17).Width = 10;  // PQM_Result
                ws.Column(18).Width = 70;  // PQM_Exception_Reason
                ws.Column(19).Width = 12;  // Validation_Result
                ws.Column(20).Width = 55;  // Validation_Reason
            }

            var wsRows = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsRows, 1, "RULE 13 VALIDATION RESULTS", headers.Length);
            WriteR13HeaderRow(wsRows);
            wsRows.SheetView.FreezeRows(2);
            var rowIndex = 3;
            foreach (var row in summary.ReviewRows) { WriteR13DataRow(wsRows, rowIndex++, row); }
            SetR13ColWidths(wsRows);

            var failRows = summary.ReviewRows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(row.PqmResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (failRows.Any())
            {
                var wsFail = wb.Worksheets.Add("Exceptions");
                StyleHeaderRow(wsFail, 1, "RULE 13 EXCEPTIONS", headers.Length);
                WriteR13HeaderRow(wsFail);
                wsFail.SheetView.FreezeRows(2);
                rowIndex = 3;
                foreach (var row in failRows) { WriteR13DataRow(wsFail, rowIndex++, row); }
                SetR13ColWidths(wsFail);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule12ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 12: COURSE SELECTION FROM dbo_CREG", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("CREG Table", summary.CregTable),
                ("CRES Table", summary.CresTable),
                ("Validation Date", summary.Timestamp),
                ("Join Path", summary.TableLinkageText),
                ("Validation Mode", summary.RuleModeText),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Selected Courses", summary.TotalValidated.ToString("N0")),
                ("Matched Courses", summary.PassCount.ToString("N0")),
                ("Missing Courses", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Export Note", summary.Warning ?? "")
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 34;
            wsSummary.Column(2).Width = 70;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 12 CONTROL SUMMARY", 6);
            var controlHeaders = new[] { "Control", "Criteria", "Rows Tested", "PASS", "FAIL", "Status" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControlSummary.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsControlSummary.Cell(i + 3, 1).Value = item.ControlLabel;
                wsControlSummary.Cell(i + 3, 2).Value = item.CriteriaText;
                wsControlSummary.Cell(i + 3, 3).Value = item.TotalCount;
                wsControlSummary.Cell(i + 3, 4).Value = item.PassCount;
                wsControlSummary.Cell(i + 3, 5).Value = item.FailCount;
                wsControlSummary.Cell(i + 3, 6).Value = item.Status;
            }
            for (var c = 1; c <= 6; c++)
                wsControlSummary.Column(c).AdjustToContents();

            var wsAll = wb.Worksheets.Add("Dashboard Results");
            StyleHeaderRow(wsAll, 1, "RULE 12 VALIDATION RESULTS", 2);
            wsAll.Row(2).Height = 22;

            // Compute the actual DisplayValues key aliases for optional CREG columns
            static string ColAlias(string? col) =>
                string.IsNullOrWhiteSpace(col) ? null! : $"CREG_{col.TrimStart('_')}";

            var r12Extra1Key = ColAlias(summary.CregExtra1Col);
            var r12Extra2Key = ColAlias(summary.CregExtra2Col);
            var r12FilterKey = ColAlias(summary.CregFilterCol);
            var r12Extra3Key = ColAlias(summary.CregExtra3Col);

            var r12ColKeys = new List<string>
            {
                "Extract_Number", "Student_Number", "Qualification_Code",
                "Course_Code", "Course_Name", "Course_Approval_Status"
            };
            if (!string.IsNullOrWhiteSpace(r12Extra1Key)) r12ColKeys.Add(r12Extra1Key);
            if (!string.IsNullOrWhiteSpace(r12Extra2Key)) r12ColKeys.Add(r12Extra2Key);
            if (!string.IsNullOrWhiteSpace(r12FilterKey)) r12ColKeys.Add(r12FilterKey);
            if (!string.IsNullOrWhiteSpace(r12Extra3Key)) r12ColKeys.Add(r12Extra3Key);
            r12ColKeys.Add("QUAL_Qualification_Code");
            r12ColKeys.Add("QUAL_Qualification_Name");
            r12ColKeys.Add("Validation_Result");
            r12ColKeys.Add("Validation_Reason");

            var r12TotalCols = r12ColKeys.Count;

            // Header row
            for (var i = 0; i < r12ColKeys.Count; i++)
            {
                var cell = wsAll.Cell(2, i + 1);
                cell.Value = r12ColKeys[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var rowsForExport = summary.ReviewRows.ToList();
            const int rule12RichFormattingRowThreshold = 5000;

            static string DisplayRule12Value(Rule12ValidationRowRecord row, string key)
                => row.DisplayValues.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

            IEnumerable<object?[]> BuildRule12ExportRows()
            {
                foreach (var row in rowsForExport)
                {
                    var values = r12ColKeys.Select(key => (object?)DisplayRule12Value(row, key)).ToArray();
                    yield return values;
                }
            }

            if (rowsForExport.Count == 0)
            {
                wsAll.Range(3, 1, 3, r12TotalCols).Merge();
                var noRowsCell = wsAll.Cell(3, 1);
                noRowsCell.Value = $"No exceptions — all {summary.PassCount:N0} course selections were found in QUAL.";
                noRowsCell.Style.Font.Italic = true;
                noRowsCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                wsAll.Columns(1, r12TotalCols).AdjustToContents();
                using var msNoRows = new MemoryStream();
                wb.SaveAs(msNoRows);
                return msNoRows.ToArray();
            }

            wsAll.Cell(3, 1).InsertData(BuildRule12ExportRows());
            wsAll.SheetView.FreezeRows(2);

            var lastDataRow = 2 + rowsForExport.Count;
            if (rowsForExport.Count <= rule12RichFormattingRowThreshold)
            {
                for (var rowOffset = 0; rowOffset < rowsForExport.Count; rowOffset++)
                {
                    var row = rowsForExport[rowOffset];
                    var rowIndex = 3 + rowOffset;
                    var isFail = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase);
                    var rowFill = isFail ? "#FFF3F3" : "#F3FFF3";
                    wsAll.Range(rowIndex, 1, rowIndex, r12TotalCols).Style.Fill.BackgroundColor = XLColor.FromHtml(rowFill);
                    wsAll.Range(rowIndex, 1, rowIndex, r12TotalCols).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                    var resultCol = r12ColKeys.IndexOf("Validation_Result") + 1;
                    wsAll.Cell(rowIndex, resultCol).Style.Font.Bold = true;
                    wsAll.Cell(rowIndex, resultCol).Style.Font.FontColor = isFail
                        ? XLColor.FromHtml("#B71C1C")
                        : XLColor.FromHtml("#1B5E20");
                }

                wsAll.Columns(1, r12TotalCols).AdjustToContents();
            }
            else
            {
                wsAll.Range(3, 1, lastDataRow, r12TotalCols).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                for (var col = 1; col <= r12TotalCols; col++)
                    wsAll.Column(col).Width = 18;
                var rsnColIdx = r12ColKeys.IndexOf("Validation_Reason") + 1;
                if (rsnColIdx > 0) wsAll.Column(rsnColIdx).Width = 50;
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule10ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, BuildIntegrityExportTitle(summary), 2);
            var summaryData = BuildIntegritySummaryData(summary);

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                }
                summaryRow++;
            }
            wsSummary.Column(1).Width = 34;
            wsSummary.Column(2).Width = 80;

            var wsRuleSummary = wb.Worksheets.Add("Rule Summary");
            StyleHeaderRow(wsRuleSummary, 1, $"{summary.RuleLabel.ToUpperInvariant()} SUMMARY", 7);
            var ruleHeaders = new[] { "Rule", "Name", "Table Scope", "Criteria", "Issues", "Severity", "Status" };
            for (var i = 0; i < ruleHeaders.Length; i++)
            {
                var cell = wsRuleSummary.Cell(2, i + 1);
                cell.Value = ruleHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsRuleSummary.Cell(i + 3, 1).Value = item.RuleId;
                wsRuleSummary.Cell(i + 3, 2).Value = item.ControlLabel;
                wsRuleSummary.Cell(i + 3, 3).Value = item.TableName;
                wsRuleSummary.Cell(i + 3, 4).Value = item.CriteriaText;
                wsRuleSummary.Cell(i + 3, 5).Value = item.ErrorCount;
                wsRuleSummary.Cell(i + 3, 6).Value = item.Severity;
                wsRuleSummary.Cell(i + 3, 7).Value = item.Status;
            }
            for (var c = 1; c <= 7; c++) wsRuleSummary.Column(c).AdjustToContents();

            var title = BuildIntegrityExportTitle(summary);

            if (summary.RuleNumber == 9)
            {
                var allExceptions = summary.ReviewRows
                    .Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var blankRows = allExceptions
                    .Where(r => r.DisplayValues.TryGetValue("Exception_Type", out var et) &&
                                string.Equals(et, "Blank Student Number", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var notInStudRows = allExceptions
                    .Where(r => r.DisplayValues.TryGetValue("Exception_Type", out var et) &&
                                string.Equals(et, "Student Not in STUD Table", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                WriteIntegrityResultSheet(wb, "Ghost Students", $"{title} - GHOST STUDENTS", summary, allExceptions);
                WriteIntegrityResultSheet(wb, "Blank Student Number", $"{title} - BLANK STUDENT NUMBER", summary, blankRows);
                WriteIntegrityResultSheet(wb, "Not in STUD Table", $"{title} - STUDENT NOT IN STUD TABLE", summary, notInStudRows);
            }
            else
            {
                WriteIntegrityResultSheet(wb, "Results", $"{title} - RESULTS", summary, summary.ReviewRows);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteIntegrityResultSheet(
            XLWorkbook wb,
            string sheetName,
            string sheetTitle,
            Rule10ValidationSummary summary,
            IReadOnlyList<Rule10ValidationRowRecord> rows)
        {
            var detailKeys = rows
                .SelectMany(row => row.DisplayValues.Keys)
                .Where(key => !string.Equals(key, "Validation_Result", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "Validation_Explanation", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "RULE_LABEL", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "RESULT_BADGE", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "FINAL_RESULT_MESSAGE", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ws = wb.Worksheets.Add(sheetName);
            StyleHeaderRow(ws, 1, sheetTitle, detailKeys.Count + 3);
            var headers = new List<string> { "Rule", "Validation Result", "Validation Explanation" };
            headers.AddRange(detailKeys.Select(key => GetIntegrityDetailHeader(summary, key)));
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var rowIndex = 3;
            foreach (var row in rows)
            {
                ws.Cell(rowIndex, 1).Value = row.RuleId;
                ws.Cell(rowIndex, 2).Value = row.ValidationResult;
                ws.Cell(rowIndex, 3).Value = row.ValidationExplanation;
                for (var i = 0; i < detailKeys.Count; i++)
                {
                    ws.Cell(rowIndex, i + 4).Value = row.DisplayValues.TryGetValue(detailKeys[i], out var value) ? value : "";
                }
                rowIndex++;
            }
            for (var c = 1; c <= headers.Count; c++) ws.Column(c).AdjustToContents();
        }

        private static string BuildIntegrityExportTitle(Rule10ValidationSummary summary)
        {
            var ruleLabel = string.IsNullOrWhiteSpace(summary.RuleLabel) ? $"Rule {summary.RuleNumber}" : summary.RuleLabel.Trim();
            var ruleTitle = string.IsNullOrWhiteSpace(summary.RuleTitle) ? "Integrity Check" : summary.RuleTitle.Trim();
            return $"HEMIS {ruleLabel.ToUpperInvariant()}: {ruleTitle.ToUpperInvariant()}";
        }

        private static List<(string Label, string Value)> BuildIntegritySummaryData(Rule10ValidationSummary summary)
        {
            var rows = new List<(string Label, string Value)>
            {
                ("Database", summary.Database),
                ("Rule", summary.RuleLabel),
                ("Rule Title", summary.RuleTitle),
                ("Validation Date", summary.Timestamp),
                ("Validation Mode", summary.RuleModeText)
            };

            var criteria = summary.ControlSummaries.FirstOrDefault()?.CriteriaText;
            if (!string.IsNullOrWhiteSpace(criteria))
                rows.Add(("Criteria", criteria));

            var parameters = ParseIntegrityRuleParameters(summary.RuleParameterJson);

            switch (summary.RuleNumber)
            {
                case 1:
                case 2:
                    rows.Add(("QUAL Table", summary.QualTable));
                    rows.Add(("Check Column", summary.QualColumn));
                    break;
                case 3:
                    rows.Add(("QUAL Table", summary.QualTable));
                    rows.Add(("Duplicate Check Column", summary.QualColumn));
                    break;
                case 4:
                    rows.Add(("CRSE Table", summary.CrseTable));
                    rows.Add(("Duplicate Check Column", summary.CrseColumn));
                    break;
                case 5:
                case 6:
                    rows.Add(("STUD Table", summary.StudTable));
                    rows.Add(("Check Column", summary.StudColumn));
                    if (summary.RuleNumber == 5 && parameters.TryGetValue("MatchValue", out var matchValue) && !string.IsNullOrWhiteSpace(matchValue))
                        rows.Add(("Configured Invalid Value", matchValue));
                    break;
                case 7:
                    rows.Add(("STUD Table", summary.StudTable));
                    rows.Add(("QUAL Table", summary.QualTable));
                    rows.Add(("STUD Qualification Column", summary.StudColumn));
                    rows.Add(("QUAL Qualification Column", summary.QualColumn));
                    if (parameters.TryGetValue("ContextColumn", out var rule7Context) && !string.IsNullOrWhiteSpace(rule7Context))
                        rows.Add(("STUD Student Number Column", rule7Context));
                    break;
                case 8:
                    rows.Add(("CREG Table", summary.CregTable));
                    rows.Add(("CRSE Table", summary.CrseTable));
                    rows.Add(("CREG Course Column", summary.CregColumn));
                    rows.Add(("CRSE Course Column", summary.CrseColumn));
                    if (parameters.TryGetValue("ContextColumn", out var rule8Context) && !string.IsNullOrWhiteSpace(rule8Context))
                        rows.Add(("CREG Student Number Column", rule8Context));
                    break;
                case 9:
                    rows.Add(("CREG Table", summary.CregTable));
                    rows.Add(("STUD Table", summary.StudTable));
                    rows.Add(("CREG Student Number Column", summary.CregColumn));
                    rows.Add(("STUD Student Number Column", summary.StudColumn));
                    break;
                case 10:
                    rows.Add(("Join Verification", summary.TableLinkageText));
                    break;
                default:
                    rows.Add(("QUAL Table", summary.QualTable));
                    rows.Add(("STUD Table", summary.StudTable));
                    rows.Add(("CREG Table", summary.CregTable));
                    rows.Add(("CRSE Table", summary.CrseTable));
                    break;
            }

            rows.Add(("", ""));
            rows.Add(("RESULT SUMMARY", ""));
            rows.Add(("Total Checks", summary.TotalChecks.ToString("N0")));
            rows.Add(("Passed Checks", summary.PassedChecks.ToString("N0")));
            rows.Add(("Failed Checks", summary.FailedChecks.ToString("N0")));
            rows.Add(("Total Issues", summary.TotalIssues.ToString("N0")));
            rows.Add(("High Severity Rules", summary.HighSeverityCount.ToString("N0")));
            rows.Add(("Exception Rate", $"{summary.ExceptionRate:F2}%"));
            rows.Add(("Status", summary.Status));
            rows.Add(("Overall Status", summary.OverallStatusText));
            rows.Add(("Export Note", summary.Warning ?? ""));

            return rows;
        }

        private static Dictionary<string, string> ParseIntegrityRuleParameters(string? json)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json))
                return values;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return values;

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            values[property.Name] = value.Trim();
                    }
                }
            }
            catch
            {
            }

            return values;
        }

        private static string GetIntegrityDetailHeader(Rule10ValidationSummary summary, string key)
        {
            var normalized = key.ToUpperInvariant();
            return summary.RuleNumber switch
            {
                1 or 2 or 5 or 6 => normalized switch
                {
                    "TABLE_NAME" => "Selected Table",
                    "COLUMN_NAME" => "Check Column",
                    "COLUMN_VALUE" => "Exception Value",
                    "EXPECTED_VALUE" => "Configured Value",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                3 or 4 => normalized switch
                {
                    "TABLE_NAME" => "Selected Table",
                    "COLUMN_NAME" => "Duplicate Check Column",
                    "DUPLICATE_VALUE" => "Duplicated Value",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                7 => normalized switch
                {
                    "STUDENT_TABLE_NAME" => "STUD Table",
                    "STUDENT_COLUMN_NAME" => "Student Number Column",
                    "STUDENT_COLUMN_VALUE" => "Student Number",
                    "LEFT_TABLE_NAME" => "STUD Table",
                    "LEFT_COLUMN_NAME" => "STUD Qualification Column",
                    "LEFT_COLUMN_VALUE" => "STUD Qualification Value",
                    "RIGHT_TABLE_NAME" => "QUAL Table",
                    "RIGHT_COLUMN_NAME" => "QUAL Qualification Column",
                    "RIGHT_COLUMN_VALUE" => "QUAL Qualification Value",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                8 => normalized switch
                {
                    "STUDENT_COLUMN_VALUE" => "Student Number",
                    "LEFT_TABLE_NAME" => "CREG Table",
                    "LEFT_COLUMN_NAME" => "CREG Course Column",
                    "LEFT_COLUMN_VALUE" => "CREG Course Value",
                    "RIGHT_TABLE_NAME" => "CRSE Table",
                    "RIGHT_COLUMN_NAME" => "CRSE Course Column",
                    "RIGHT_COLUMN_VALUE" => "CRSE Course Value",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                9 => normalized switch
                {
                    "ROWNO" => "Row No",
                    "STUDENTNO" => "Student No",
                    "INSTCODE" => "Inst Code",
                    "COLYEAR" => "Col Year",
                    "EXCEPTION_TYPE" => "Reason",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                10 => normalized switch
                {
                    "TABLE_NAME" => "Dataset Table",
                    "COLUMN_NAME" => "Key Column",
                    "COLUMN_VALUE" => "Detected Value",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                _ => GetGenericIntegrityDetailHeader(key)
            };
        }

        private static string GetGenericIntegrityDetailHeader(string key) =>
            key.ToUpperInvariant() switch
            {
                "TABLE_NAME" => "Table",
                "COLUMN_NAME" => "Column",
                "COLUMN_VALUE" => "Column Value",
                "EXPECTED_VALUE" => "Configured Value",
                "DUPLICATE_VALUE" => "Duplicate Value",
                "DUPLICATE_COUNT" => "Duplicate Count",
                "STUDENT_TABLE_NAME" => "Student Table",
                "STUDENT_COLUMN_NAME" => "Student Column",
                "STUDENT_COLUMN_VALUE" => "Student Value",
                "LEFT_TABLE_NAME" => "Left Table",
                "LEFT_COLUMN_NAME" => "Left Column",
                "LEFT_COLUMN_VALUE" => "Left Value",
                "RIGHT_TABLE_NAME" => "Right Table",
                "RIGHT_COLUMN_NAME" => "Right Column",
                "RIGHT_COLUMN_VALUE" => "Right Value",
                "LEFT_STUDENT_COLUMN_NAME" => "Left Student Column",
                "LEFT_STUDENT_COLUMN_VALUE" => "Left Student Value",
                "RIGHT_STUDENT_COLUMN_NAME" => "Right Student Column",
                "RIGHT_STUDENT_COLUMN_VALUE" => "Right Student Value",
                "LEFT_CONTEXT_TABLE_NAME" => "Left Context Table",
                "RIGHT_CONTEXT_TABLE_NAME" => "Right Context Table",
                _ => key.Replace('_', ' ')
            };

        public byte[] ExportExcel(Rule11ValidationSummary summary)
            => ExportRule11Excel(summary);

        public byte[] ExportExcel(Rule20ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var headers = GetRule20DashboardHeaders();
            var previewRows = BuildRule20DashboardPreviewRows(summary.ReviewRows);

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 20: FOUNDATION VALIDATION (3-PART)", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("STUD Table", summary.StudTable),
                ("QUAL Table", summary.QualTable),
                ("Bridge Table", summary.CregTable),
                ("CRSE Table", summary.CrseTable),
                ("Linkage Used", summary.TableLinkageText),
                ("Overall PASS Rule", summary.OverallStatusRuleText),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Foundation Students", summary.FoundationStudentCount.ToString("N0")),
                ("Total Validated Rows", summary.TotalValidated.ToString("N0")),
                ("PASS Rows", summary.PassCount.ToString("N0")),
                ("FAIL Rows", summary.FailCount.ToString("N0")),
                ("Fail Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Warning", summary.Warning ?? "")
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                    if (label == "Status")
                    {
                        var color = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }
                summaryRow++;
            }
            if (summary.ProcedureSteps.Any())
            {
                wsSummary.Cell(summaryRow, 1).Value = "PROCEDURE PERFORMED";
                wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                wsSummary.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.White;
                wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                summaryRow++;

                for (var i = 0; i < summary.ProcedureSteps.Count; i++)
                {
                    wsSummary.Cell(summaryRow, 1).Value = $"Step {i + 1}";
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = summary.ProcedureSteps[i];
                    summaryRow++;
                }
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 72;

            var wsRows = wb.Worksheets.Add("Validated Rows");
            StyleHeaderRow(wsRows, 1, "RULE 20 VALIDATED ROWS", headers.Count);
            WriteRule20DashboardHeaderRow(wsRows, 2, headers);
            WriteRule20DashboardRows(wsRows, 3, previewRows, headers);

            var failRows = previewRows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (failRows.Any())
            {
                var wsFail = wb.Worksheets.Add("FAIL Rows");
                StyleHeaderRow(wsFail, 1, "RULE 20 FAIL ROWS", headers.Count);
                WriteRule20DashboardHeaderRow(wsFail, 2, headers);
                WriteRule20DashboardRows(wsFail, 3, failRows, headers);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule22ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var mappedColumns = GetRule22MappedColumns(summary);
            var headers = GetRule22Headers(summary);

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 22: STAFF SAMPLING (DBO_PROF)", 2);
            var summaryData = new List<(string Label, string Value)>
            {
                ("Database", summary.Database),
                ("PROF Table", summary.ProfTable),
                ("Employment Status Filter Column", summary.Column041),
                ("Personnel Category Filter Column", summary.Column039),
                ("Employment Status Filter Value", summary.FilterValue041),
                ("Personnel Category Filter Value", summary.FilterValue039),
                ("Validation Date", summary.Timestamp),
                ("", ""),
            };

            summaryData.AddRange(mappedColumns.Select(item => ($"{item.Label} Column", item.ColumnName)));
            summaryData.AddRange(new[]
            {
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Total Validated", summary.TotalValidated.ToString("N0")),
                ("PASS Rows", summary.PassCount.ToString("N0")),
                ("FAIL Rows", summary.FailCount.ToString("N0")),
                ("Fail Rate", $"{summary.ExceptionRate:F2}%"),
                ("Control 1 Available", summary.Control1Count.ToString("N0")),
                ("Control 2 Available", summary.Control2Count.ToString("N0")),
                ("Control 3 Available", summary.Control3Count.ToString("N0")),
                ("Status", summary.Status),
                ("Warning", summary.Warning ?? "")
            });

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                    if (label == "Status")
                    {
                        var color = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }
                summaryRow++;
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 72;

            var wsControls = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControls, 1, "RULE 22 CONTROL SUMMARY", 5);
            var controlHeaders = new[] { "Control", "Definition", "Rows", "PASS", "FAIL" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControls.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsControls.Cell(i + 3, 1).Value = item.ControlType;
                wsControls.Cell(i + 3, 2).Value = item.ControlDefinition;
                wsControls.Cell(i + 3, 3).Value = item.AvailableCount;
                wsControls.Cell(i + 3, 4).Value = item.PassCount;
                wsControls.Cell(i + 3, 5).Value = item.FailCount;
            }
            for (var c = 1; c <= controlHeaders.Length; c++) wsControls.Column(c).AdjustToContents();

            var wsRows = wb.Worksheets.Add("Review Rows");
            StyleHeaderRow(wsRows, 1, "RULE 22 REVIEW ROWS", headers.Count);
            WriteRule22HeaderRow(wsRows, 2, headers);
            WriteRule22Rows(wsRows, 3, summary.ReviewRows, headers);

            var failRows = summary.ReviewRows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (failRows.Any())
            {
                var wsFail = wb.Worksheets.Add("FAIL Rows");
                StyleHeaderRow(wsFail, 1, "RULE 22 FAIL ROWS", headers.Count);
                WriteRule22HeaderRow(wsFail, 2, headers);
                WriteRule22Rows(wsFail, 3, failRows, headers);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule24ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule24Headers();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 24: RECONCILE QUALIFICATION DATASETS", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("QUAL Table", summary.QualTable),
                ("Audit Table", summary.AuditTable),
                ("H16 Table", summary.H16Table),
                ("QUAL Code Column", summary.QualCodeColumn),
                ("Approval Status Column", summary.Control1OnlyMode ? "Not applied" : summary.ApprovalStatusColumn),
                ("Excluded Approval Value", summary.Control1OnlyMode ? "Not applied" : summary.ExcludedApprovalStatusValue),
                ("Validation Mode", summary.Control1OnlyMode ? "Control 1 Only" : "Control 1 + Control 2"),
                ("Audit Qualification Code Column", summary.AuditQualCodeColumn),
                ("H16 Qualification Code Column", summary.H16QualCodeColumn),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("QUAL Records", summary.QualCount.ToString("N0")),
                ("Audit Records", summary.AuditCount.ToString("N0")),
                ("H16 Records", summary.H16Count.ToString("N0")),
                ("Total Reconciled Rows", summary.TotalValidated.ToString("N0")),
                ("Matched Rows", summary.Matches.ToString("N0")),
                ("Mismatch Rows", summary.Mismatches.ToString("N0")),
                ("Pass Sample Saved", summary.PassSampleCount.ToString("N0")),
                ("Mismatch Rate", $"{summary.ExceptionRate:F2}%"),
                ("Match Rate", $"{summary.MatchRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                    if (label == "Status")
                    {
                        var color = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 70;

            var wsPass = wb.Worksheets.Add("Pass Sample");
            StyleHeaderRow(wsPass, 1, "RULE 24 PASS SAMPLE", headers.Count);
            WriteRule24HeaderRow(wsPass, 2, headers);
            WriteRule24Rows(wsPass, 3, summary.PassSampleRows, headers, "#F3FFF3");

            var wsFail = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsFail, 1, "RULE 24 FAIL EXCEPTIONS", headers.Count);
            WriteRule24HeaderRow(wsFail, 2, headers);
            WriteRule24Rows(wsFail, 3, summary.FailRows, headers, "#FFF3F3");

            var wsBreakdown = wb.Worksheets.Add("Issue Breakdown");
            StyleHeaderRow(wsBreakdown, 1, "RULE 24 ISSUE BREAKDOWN", 2);
            wsBreakdown.Cell(2, 1).Value = "Status";
            wsBreakdown.Cell(2, 2).Value = "Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsBreakdown.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.IssueCounts.Count; i++)
            {
                wsBreakdown.Cell(i + 3, 1).Value = summary.IssueCounts[i].Status;
                wsBreakdown.Cell(i + 3, 2).Value = summary.IssueCounts[i].Count;
            }
            wsBreakdown.Column(1).AdjustToContents();
            wsBreakdown.Column(2).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule27ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = new[] { "Validation_Number", "Filter_Value" }
                .Concat(summary.MatchingRows
                    .SelectMany(r => r.DisplayValues.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 27: ERROR VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("Filter Column", summary.FilterColumn),
                ("Filter Value", summary.FilterValue),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("100% Source Records Tested", summary.TotalValidated.ToString("N0")),
                ("Matching Records Retrieved", summary.MatchingCount.ToString("N0")),
                ("Coverage", "100%"),
                ("Match Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            var wsMatches = wb.Worksheets.Add("Filtered Records");
            StyleHeaderRow(wsMatches, 1, "RULE 27 FILTERED RECORDS", headers.Count);
            WriteRule27HeaderRow(wsMatches, 2, headers);
            WriteRule27Rows(wsMatches, 3, summary.MatchingRows, headers);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportCsv(Rule26ValidationSummary summary)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Direction,ControlNumber,ControlName,PersonnelNumber,PersonnelName,ExceptionReason,BaseValue,ReferenceValue");
            foreach (var row in summary.Exceptions)
            {
                builder
                    .Append(CsvEscape(row.DirectionLabel)).Append(',')
                    .Append(row.ControlNumber).Append(',')
                    .Append(CsvEscape(row.ControlName)).Append(',')
                    .Append(CsvEscape(row.PersonnelNumber)).Append(',')
                    .Append(CsvEscape(row.PersonnelName ?? "")).Append(',')
                    .Append(CsvEscape(row.ExceptionReason)).Append(',')
                    .Append(CsvEscape(row.BaseValue)).Append(',')
                    .Append(CsvEscape(row.ReferenceValue)).AppendLine();
            }

            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        public byte[] ExportExcel(Rule32ValidationSummary summary)
            => ExportFatalErrorExcel(summary, 32, "STUD");

        public byte[] ExportExcel(Rule31ValidationSummary summary) =>
            ExportFatalErrorExcel(ToRule32Summary(summary), 31, "QUAL");

        private byte[] ExportFatalErrorExcel(Rule32ValidationSummary summary, int ruleNumber, string tableScope)
        {
            using var wb = new XLWorkbook();

            var allHeaders = GetRule32Headers(summary);

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, $"HEMIS RULE {ruleNumber}: FATAL ERRORS WITH EXCLUSIONS ({tableScope})", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("Error Type Column", summary.ErrorTypeColumn),
                ("Error Column", summary.ErrorColumn),
                ("Filter Value", summary.ErrorTypeValue),
                ("Exclusion Codes", string.Join(", ", summary.Exclusions)),
                ("Normalized Codes", string.Join(", ", summary.NormalizedExclusions)),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("VALIDATION RESULTS", ""),
                ("Total Fatal Errors", summary.TotalFatal.ToString("N0")),
                ("Excluded", summary.ExcludedCount.ToString("N0")),
                ("Remaining", summary.RemainingCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                    if (label == "Status")
                    {
                        var color = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            var wsExcluded = wb.Worksheets.Add("Excluded");
            StyleHeaderRow(wsExcluded, 1, $"RULE {ruleNumber} EXCLUDED FATAL ERRORS", allHeaders.Count);
            WriteRule32HeaderRow(wsExcluded, 2, allHeaders);
            WriteRule32Rows(wsExcluded, 3, summary.ExcludedRows, allHeaders, "#F3FFF3");

            var wsRemaining = wb.Worksheets.Add("Remaining");
            StyleHeaderRow(wsRemaining, 1, $"RULE {ruleNumber} REMAINING FATAL ERRORS", allHeaders.Count);
            WriteRule32HeaderRow(wsRemaining, 2, allHeaders);
            WriteRule32Rows(wsRemaining, 3, summary.RemainingRows, allHeaders, "#FFF3F3");

            var wsStats = wb.Worksheets.Add("Breakdown");
            StyleHeaderRow(wsStats, 1, $"RULE {ruleNumber} ERROR CODE BREAKDOWN", 4);
            wsStats.Cell(2, 1).Value = "Excluded Code";
            wsStats.Cell(2, 2).Value = "Excluded Count";
            wsStats.Cell(2, 3).Value = "Remaining Code";
            wsStats.Cell(2, 4).Value = "Remaining Count";
            for (var col = 1; col <= 4; col++)
            {
                var cell = wsStats.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var maxRows = Math.Max(summary.ExcludedBreakdown.Count, summary.RemainingBreakdown.Count);
            for (var i = 0; i < maxRows; i++)
            {
                if (i < summary.ExcludedBreakdown.Count)
                {
                    wsStats.Cell(i + 3, 1).Value = summary.ExcludedBreakdown[i].ErrorCode;
                    wsStats.Cell(i + 3, 2).Value = summary.ExcludedBreakdown[i].Count;
                }
                if (i < summary.RemainingBreakdown.Count)
                {
                    wsStats.Cell(i + 3, 3).Value = summary.RemainingBreakdown[i].ErrorCode;
                    wsStats.Cell(i + 3, 4).Value = summary.RemainingBreakdown[i].Count;
                }
            }
            for (var col = 1; col <= 4; col++) wsStats.Column(col).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ─── CSV Export ───────────────────────────────────────────────────────
        public byte[] ExportRule36Csv(ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();

            if (exceptionsOnly)
            {
                sb.AppendLine("Validation_Number,Student_ID,Exception_Reason,Validation_Result");
                foreach (var ex in summary.Exceptions)
                {
                    sb.AppendLine($"{ex.ValidationNumber},{CsvEscape(ex.StudentId)},{CsvEscape(ex.ExceptionReason)},{ex.ValidationResult}");
                }
            }
            else
            {
                var extraHeaders = GetAdditionalHeaders(summary);
                sb.AppendLine(string.Join(",",
                    new[] { "Validation_Number", "Validation_Result", "Exception_Reason", "Student_ID" }
                        .Concat(extraHeaders)));

                foreach (var row in summary.ValidationRows)
                {
                    var values = new List<string>
                    {
                        row.ValidationNumber.ToString(),
                        CsvEscape(row.ValidationResult),
                        CsvEscape(row.ExceptionReason),
                        CsvEscape(row.StudentId)
                    };

                    foreach (var header in extraHeaders)
                        values.Add(CsvEscape(row.AdditionalColumns.TryGetValue(header, out var value) ? value : null));

                    sb.AppendLine(string.Join(",", values));
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportRule37Excel(Rule37ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var reviewRow = XLColor.FromHtml("#FFF8E1");
            var passRow = XLColor.FromHtml("#F3FFF3");
            var failRow = XLColor.FromHtml("#FFF3F3");
            var reviewFont = XLColor.FromHtml("#B45309");
            var passFont = XLColor.FromHtml("#1B5E20");
            var failFont = XLColor.FromHtml("#B71C1C");

            // Sheet 1: Full Validation Results
            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 37 — CESM vs PQM VALIDATION", 11);
            var headers = new[]
            {
                "#", "Record ID", "HEMIS CESM Code", "HEMIS Qual Name",
                "PQM Code", "PQM Name", "Code Match", "Name Match",
                "Review Required", "Result", "Review Note / Exception Reason"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.WrapText = true;
            }

            int rowIndex = 3;
            foreach (var row in summary.ValidationRows)
            {
                var rowFill = row.NeedsReview ? reviewRow : row.ValidationResult == "PASS" ? passRow : failRow;
                var resultFont = row.NeedsReview ? reviewFont : row.ValidationResult == "PASS" ? passFont : failFont;
                wsResults.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsResults.Cell(rowIndex, 2).Value = row.RecordId;
                wsResults.Cell(rowIndex, 3).Value = row.HemisCesmCode;
                wsResults.Cell(rowIndex, 4).Value = row.HemisQualName;
                wsResults.Cell(rowIndex, 5).Value = row.PqmCode ?? "";
                wsResults.Cell(rowIndex, 6).Value = row.PqmName ?? "";
                wsResults.Cell(rowIndex, 7).Value = row.CodeMatch ? "YES" : "NO";
                wsResults.Cell(rowIndex, 8).Value = row.NameMatch ? "YES" : "NO";
                wsResults.Cell(rowIndex, 9).Value = row.NeedsReview ? "YES" : "NO";
                wsResults.Cell(rowIndex, 10).Value = row.ValidationResult;
                wsResults.Cell(rowIndex, 11).Value = row.ExceptionReason ?? "";
                wsResults.Range(rowIndex, 1, rowIndex, 11).Style.Fill.BackgroundColor = rowFill;
                wsResults.Cell(rowIndex, 10).Style.Font.Bold = true;
                wsResults.Cell(rowIndex, 10).Style.Font.FontColor = resultFont;
                wsResults.Cell(rowIndex, 11).Style.Font.FontColor = resultFont;
                wsResults.Cell(rowIndex, 11).Style.Alignment.WrapText = true;
                wsResults.Row(rowIndex).Height = string.IsNullOrWhiteSpace(row.ExceptionReason) ? 18 : 42;
                rowIndex++;
            }
            for (int c = 1; c <= 11; c++) wsResults.Column(c).AdjustToContents();
            wsResults.Column(4).Width = Math.Max(wsResults.Column(4).Width, 28);
            wsResults.Column(6).Width = Math.Max(wsResults.Column(6).Width, 32);
            wsResults.Column(11).Width = Math.Max(wsResults.Column(11).Width, 70);
            wsResults.Column(4).Style.Alignment.WrapText = true;
            wsResults.Column(6).Style.Alignment.WrapText = true;
            wsResults.Column(11).Style.Alignment.WrapText = true;

            // Sheet 2: Exceptions
            var wsEx = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsEx, 1, "RULE 37 EXCEPTIONS — CESM vs PQM", 11);
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsEx.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.WrapText = true;
            }

            int exRow = 3;
            foreach (var ex in summary.Exceptions)
            {
                var rowFill = ex.NeedsReview ? reviewRow : ex.ValidationResult == "PASS" ? passRow : failRow;
                var resultFont = ex.NeedsReview ? reviewFont : ex.ValidationResult == "PASS" ? passFont : failFont;
                wsEx.Cell(exRow, 1).Value = ex.ValidationNumber;
                wsEx.Cell(exRow, 2).Value = ex.RecordId;
                wsEx.Cell(exRow, 3).Value = ex.HemisCesmCode;
                wsEx.Cell(exRow, 4).Value = ex.HemisQualName;
                wsEx.Cell(exRow, 5).Value = ex.PqmCode ?? "";
                wsEx.Cell(exRow, 6).Value = ex.PqmName ?? "";
                wsEx.Cell(exRow, 7).Value = ex.CodeMatch ? "YES" : "NO";
                wsEx.Cell(exRow, 8).Value = ex.NameMatch ? "YES" : "NO";
                wsEx.Cell(exRow, 9).Value = ex.NeedsReview ? "YES" : "NO";
                wsEx.Cell(exRow, 10).Value = ex.ValidationResult;
                wsEx.Cell(exRow, 11).Value = ex.ExceptionReason;
                wsEx.Range(exRow, 1, exRow, 11).Style.Fill.BackgroundColor = rowFill;
                wsEx.Cell(exRow, 10).Style.Font.FontColor = resultFont;
                wsEx.Cell(exRow, 10).Style.Font.Bold = true;
                wsEx.Cell(exRow, 11).Style.Font.FontColor = resultFont;
                wsEx.Cell(exRow, 11).Style.Alignment.WrapText = true;
                wsEx.Row(exRow).Height = string.IsNullOrWhiteSpace(ex.ExceptionReason) ? 18 : 42;
                exRow++;
            }
            for (int c = 1; c <= 11; c++) wsEx.Column(c).AdjustToContents();
            wsEx.Column(4).Width = Math.Max(wsEx.Column(4).Width, 28);
            wsEx.Column(6).Width = Math.Max(wsEx.Column(6).Width, 32);
            wsEx.Column(11).Width = Math.Max(wsEx.Column(11).Width, 70);
            wsEx.Column(4).Style.Alignment.WrapText = true;
            wsEx.Column(6).Style.Alignment.WrapText = true;
            wsEx.Column(11).Style.Alignment.WrapText = true;

            // Sheet 3: Summary
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 37: CESM vs PQM VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",            summary.Database),
                ("CESM Table",          $"{summary.CesmTable} (ID: {summary.CesmIdCol}, Code: {summary.CesmCodeCol})"),
                ("QUAL Table",          $"{summary.QualTable} (ID: {summary.QualIdCol}, Name: {summary.QualNameCol})"),
                ("PQM Table",           $"{summary.PqmTable} (Name: {summary.PqmNameCol}, Code1: {summary.PqmCode1Col}, Code2: {summary.PqmCode2Col})"),
                ("Validation Date",     summary.Timestamp),
                ("",                    ""),
                ("VALIDATION RESULTS",  ""),
                ("Total Validated",     summary.TotalValidated.ToString("N0")),
                ("PASS (Matched)",      summary.PassCount.ToString("N0")),
                ("PASS (Review required)", summary.ReviewCount.ToString("N0")),
                ("FAIL (Mismatch)",     summary.FailCount.ToString("N0")),
                ("Exception Rate",      $"{summary.ExceptionRate:F2}%"),
                ("Status",              summary.Status)
            };

            int summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;
                    if (label == "Status")
                    {
                        var color = value == "PASS"
                            ? XLColor.FromHtml("#C8E6C9")
                            : value == "PASS WITH REVIEW"
                                ? XLColor.FromHtml("#FFF8E1")
                                : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }
                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule37Csv(Rule37ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();

            if (exceptionsOnly)
            {
                sb.AppendLine("Validation_Number,Record_ID,HEMIS_CESM_Code,HEMIS_Qual_Name,PQM_Code,PQM_Name,Code_Match,Name_Match,Validation_Result,Exception_Reason");
                foreach (var ex in summary.Exceptions)
                {
                    sb.AppendLine(string.Join(",",
                        ex.ValidationNumber,
                        CsvEscape(ex.RecordId),
                        CsvEscape(ex.HemisCesmCode),
                        CsvEscape(ex.HemisQualName),
                        CsvEscape(ex.PqmCode),
                        CsvEscape(ex.PqmName),
                        ex.CodeMatch ? "YES" : "NO",
                        ex.NameMatch ? "YES" : "NO",
                        ex.ValidationResult,
                        CsvEscape(ex.ExceptionReason)));
                }
            }
            else
            {
                sb.AppendLine("Validation_Number,Record_ID,HEMIS_CESM_Code,HEMIS_Qual_Name,PQM_Code,PQM_Name,Code_Match,Name_Match,Validation_Result,Exception_Reason");
                foreach (var row in summary.ValidationRows)
                {
                    sb.AppendLine(string.Join(",",
                        row.ValidationNumber,
                        CsvEscape(row.RecordId),
                        CsvEscape(row.HemisCesmCode),
                        CsvEscape(row.HemisQualName),
                        CsvEscape(row.PqmCode),
                        CsvEscape(row.PqmName),
                        row.CodeMatch ? "YES" : "NO",
                        row.NameMatch ? "YES" : "NO",
                        row.ValidationResult,
                        CsvEscape(row.ExceptionReason)));
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportRule38Excel(Rule38ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var qualHdr    = XLColor.FromHtml("#558B2F");
            var qualSub    = XLColor.FromHtml("#AED581");
            var cesmHdr    = XLColor.FromHtml("#00838F");
            var cesmSub    = XLColor.FromHtml("#80DEEA");
            var pqmHdr     = XLColor.FromHtml("#6A1B9A");
            var pqmSub     = XLColor.FromHtml("#CE93D8");
            var ctrlHdr    = XLColor.FromHtml("#374151");
            var ctrlSub    = XLColor.FromHtml("#D1D5DB");
            var resultHdr  = XLColor.FromHtml("#37474F");
            var reasonHdr  = XLColor.FromHtml("#6D4C41");
            var passRow    = XLColor.FromHtml("#F3FFF3");
            var failRow    = XLColor.FromHtml("#FFF3F3");
            var reviewRow  = XLColor.FromHtml("#FFF8E1");
            var manualBg   = XLColor.FromHtml("#FFFDE7");
            const int totalCols = 24;

            static void WpCell(IXLWorksheet ws, int r, int c, object? val,
                XLColor? bg = null, bool bold = false, bool italic = false,
                bool wrapText = false, XLAlignmentHorizontalValues halign = XLAlignmentHorizontalValues.Left)
            {
                var cell = ws.Cell(r, c);
                if (val != null)
                    cell.Value = XLCellValue.FromObject(val);
                if (bg != null)
                    cell.Style.Fill.BackgroundColor = bg;
                cell.Style.Font.Bold = bold;
                cell.Style.Font.Italic = italic;
                cell.Style.Alignment.WrapText = wrapText;
                cell.Style.Alignment.Horizontal = halign;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            static void SectionBanner(IXLWorksheet ws, int row, int cols, string text)
            {
                ws.Range(row, 1, row, cols).Merge();
                WpCell(ws, row, 1, text, XLColor.FromHtml("#6A1B9A"), bold: true,
                    halign: XLAlignmentHorizontalValues.Left);
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                ws.Row(row).Height = 20;
            }

            string BuildReason(Rule38ValidationRow row)
            {
                var parts = new List<string>();
                if (row.ValidationResult == "FAIL" && row.FailedControls.Count > 0)
                    parts.Add("Failed: " + string.Join(", ", row.FailedControls));
                if (!string.IsNullOrWhiteSpace(row.MatchNote))
                    parts.Add(row.MatchNote);
                return parts.Count > 0
                    ? string.Join(" | ", parts)
                    : row.ValidationResult == "PASS"
                        ? "All Rule 38 checks matched on the selected PQM row."
                        : "";
            }

            string ResolvePqmName(Rule38ValidationRow row) =>
                string.IsNullOrWhiteSpace(row.PqmName) ? "Not found in PQM" : row.PqmName;

            void BuildHeader(IXLWorksheet ws, int hRow)
            {
                ws.Range(hRow, 1, hRow + 1, 1).Merge();
                WpCell(ws, hRow, 1, "#", XLColor.FromHtml("#424242"), bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 1).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 2, hRow, 10).Merge();
                WpCell(ws, hRow, 2, summary.QualTable, qualHdr, bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 2).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 11, hRow, 16).Merge();
                WpCell(ws, hRow, 11, summary.PqmTable, pqmHdr, bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 11).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 17, hRow, 22).Merge();
                WpCell(ws, hRow, 17, "Rule 38 Controls", ctrlHdr, bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 17).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 23, hRow + 1, 23).Merge();
                WpCell(ws, hRow, 23, "Result", resultHdr, bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 23).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 24, hRow + 1, 24).Merge();
                WpCell(ws, hRow, 24, "Reason", reasonHdr, bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 24).Style.Font.FontColor = XLColor.White;

                foreach (var (col, lbl, bg) in new (int, string, XLColor)[]
                {
                    (2, summary.QualIdCol, qualSub),
                    (3, summary.QualNameCol, qualSub),
                    (4, summary.QualApprovalCol, qualSub),
                    (5, summary.QualTypeCol, qualSub),
                    (6, summary.QualMinTimeTotalCol, qualSub),
                    (7, summary.QualMinTimeWilCol, qualSub),
                    (8, summary.QualHeqfCol, qualSub),
                    (9, summary.QualTotalSubsidyCol, qualSub),
                    (10, "Population Type", qualSub),
                    (11, summary.PqmNameCol, pqmSub),
                    (12, summary.PqmQualTypeCol, pqmSub),
                    (13, summary.PqmMinTimeTotalCol, pqmSub),
                    (14, summary.PqmWilCol, pqmSub),
                    (15, summary.PqmAccreditationCol, pqmSub),
                    (16, summary.PqmTotalSubsidyCol, pqmSub),
                    (17, "Review", ctrlSub),
                    (18, "C2", ctrlSub),
                    (19, "C3", ctrlSub),
                    (20, "C4", ctrlSub),
                    (21, "C5", ctrlSub),
                    (22, "C6", ctrlSub)
                })
                {
                    WpCell(ws, hRow + 1, col, lbl, bg, bold: true, wrapText: true,
                        halign: XLAlignmentHorizontalValues.Center);
                }

                ws.Row(hRow).Height = 22;
                ws.Row(hRow + 1).Height = 46;
            }

            void SetColWidths(IXLWorksheet ws)
            {
                ws.Column(1).Width = 6;
                ws.Column(2).Width = 14;
                ws.Column(3).Width = 42;
                ws.Column(4).Width = 10;
                ws.Column(5).Width = 12;
                ws.Column(6).Width = 14;
                ws.Column(7).Width = 14;
                ws.Column(8).Width = 14;
                ws.Column(9).Width = 14;
                ws.Column(10).Width = 18;
                ws.Column(11).Width = 42;
                ws.Column(12).Width = 18;
                ws.Column(13).Width = 14;
                ws.Column(14).Width = 14;
                ws.Column(15).Width = 24;
                ws.Column(16).Width = 14;
                ws.Column(17).Width = 10;
                ws.Column(18).Width = 8;
                ws.Column(19).Width = 8;
                ws.Column(20).Width = 8;
                ws.Column(21).Width = 8;
                ws.Column(22).Width = 8;
                ws.Column(23).Width = 10;
                ws.Column(24).Width = 85;
            }

            void WriteDataRow(IXLWorksheet ws, int r, Rule38ValidationRow row, bool exceptionSheet)
            {
                var rowBg = row.NeedsReview ? reviewRow : row.ValidationResult == "PASS" ? passRow : failRow;
                var reason = BuildReason(row);

                WpCell(ws, r, 1, row.ValidationNumber, rowBg, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r, 2, row.QualCode, rowBg);
                WpCell(ws, r, 3, row.QualName, rowBg, wrapText: true);
                WpCell(ws, r, 4, row.ApprovalStatus, rowBg);
                WpCell(ws, r, 5, row.QualType ?? "", rowBg);
                WpCell(ws, r, 6, row.MinTimeTotal ?? "", rowBg);
                WpCell(ws, r, 7, row.MinTimeWIL ?? "", rowBg);
                WpCell(ws, r, 8, row.HeqfIndicator ?? "", rowBg);
                WpCell(ws, r, 9, row.TotalSubsidy ?? "", rowBg);
                WpCell(ws, r, 10, row.PopulationType, rowBg);
                WpCell(ws, r, 11, ResolvePqmName(row), string.IsNullOrWhiteSpace(row.PqmName) ? manualBg : rowBg, wrapText: true);
                WpCell(ws, r, 12, row.PqmQualType ?? "", string.IsNullOrWhiteSpace(row.PqmQualType) ? manualBg : rowBg);
                WpCell(ws, r, 13, row.PqmMinTimeTotal ?? "", string.IsNullOrWhiteSpace(row.PqmMinTimeTotal) ? manualBg : rowBg);
                WpCell(ws, r, 14, row.PqmWIL ?? "", string.IsNullOrWhiteSpace(row.PqmWIL) ? manualBg : rowBg);
                WpCell(ws, r, 15, row.PqmAccreditation ?? "N/A", string.IsNullOrWhiteSpace(row.PqmAccreditation) ? manualBg : rowBg, wrapText: true);
                WpCell(ws, r, 16, row.PqmTotalSubsidy ?? "", string.IsNullOrWhiteSpace(row.PqmTotalSubsidy) ? manualBg : rowBg);
                WpCell(ws, r, 17, row.NeedsReview ? "YES" : "NO", rowBg, bold: row.NeedsReview, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r, 18, row.C2_TypeMatch ? "P" : "F", rowBg, bold: true, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r, 19, row.C3_MinTimeMatch ? "P" : "F", rowBg, bold: true, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r, 20, row.C4_WILMatch ? "P" : "F", rowBg, bold: true, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r, 21, row.C5_HeqfMatch ? "P" : "F", rowBg, bold: true, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r, 22, row.C6_SubsidyMatch ? "P" : "F", rowBg, bold: true, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r, 23, row.ValidationResult, rowBg, bold: true, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r, 24, reason, rowBg, wrapText: true);

                ws.Cell(r, 23).Style.Font.FontColor =
                    row.NeedsReview ? XLColor.FromHtml("#B45309")
                    : row.ValidationResult == "PASS" ? XLColor.FromHtml("#1B5E20")
                    : XLColor.FromHtml("#B71C1C");

                if (exceptionSheet)
                    ws.Cell(r, 24).Style.Font.FontColor = XLColor.FromHtml("#991B1B");
                else if (row.NeedsReview)
                    ws.Cell(r, 24).Style.Font.FontColor = XLColor.FromHtml("#92400E");
                else if (row.ValidationResult == "PASS")
                    ws.Cell(r, 24).Style.Font.FontColor = XLColor.FromHtml("#166534");
                else
                    ws.Cell(r, 24).Style.Font.FontColor = XLColor.FromHtml("#991B1B");

                ws.Row(r).Height = string.IsNullOrWhiteSpace(reason) ? 18 : 42;
            }

            void AppendNotes(IXLWorksheet ws, int startRow)
            {
                var row = startRow + 2;
                SectionBanner(ws, row, totalCols, "Dashboard Notes");
                row++;
                ws.Range(row, 1, row, totalCols).Merge();
                WpCell(ws, row, 1,
                    "Rule 38 validates approved QUAL records directly against PQM using QUAL._003 = Authorised_Qualification_Name, QUAL._005 = HEQF_Qual_Type, QUAL._053 = Total2, QUAL._054 = WIL_EL2, and QUAL._090 = Total2 before evaluating controls 5.1.2-5.1.6.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Row(row).Height = 22;
                row++;
                ws.Range(row, 1, row, totalCols).Merge();
                WpCell(ws, row, 1,
                    "C2 = Qualification Type, C3 = Minimum Time Total, C4 = Minimum Time WIL, C5 = HEQF Indicator, C6 = Total Subsidy. Review = manual follow-up flag when a matched row needs extra analyst attention.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Row(row).Height = 22;
            }

            // Sheet 1: Dashboard-style validation results
            var wsAll = wb.Worksheets.Add("Validation Results");
            wsAll.Range(1, 1, 1, totalCols).Merge();
            WpCell(wsAll, 1, 1, "HEMIS RULE 38 - QUAL vs PQM VALIDATION",
                XLColor.FromHtml("#1A237E"), bold: true, halign: XLAlignmentHorizontalValues.Center);
            wsAll.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsAll.Cell(1, 1).Style.Font.FontSize = 13;
            wsAll.Row(1).Height = 24;
            BuildHeader(wsAll, 2);
            SetColWidths(wsAll);
            wsAll.SheetView.FreezeRows(3);

            var rowIdx = 4;
            foreach (var row in summary.ValidationRows)
            {
                WriteDataRow(wsAll, rowIdx++, row, exceptionSheet: false);
            }
            AppendNotes(wsAll, rowIdx);

            // Sheet 2: Dashboard-style exceptions
            var wsEx = wb.Worksheets.Add("Exceptions");
            wsEx.Range(1, 1, 1, totalCols).Merge();
            WpCell(wsEx, 1, 1, "HEMIS RULE 38 - EXCEPTIONS (QUAL vs PQM)",
                XLColor.FromHtml("#B71C1C"), bold: true, halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsEx.Cell(1, 1).Style.Font.FontSize = 13;
            wsEx.Row(1).Height = 24;
            BuildHeader(wsEx, 2);
            SetColWidths(wsEx);
            wsEx.SheetView.FreezeRows(3);

            var exIdx = 4;
            foreach (var row in summary.ValidationRows.Where(r => r.ValidationResult == "FAIL"))
            {
                WriteDataRow(wsEx, exIdx++, row, exceptionSheet: true);
            }
            AppendNotes(wsEx, exIdx);

            // Sheet 3: Control summary
            var wsCtrl = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsCtrl, 1, "RULE 38 — CONTROL SUMMARY", 5);
            var ctrlHeaders = new[] { "Control", "Description", "Criteria", "Pass", "Fail" };
            for (int i = 0; i < ctrlHeaders.Length; i++)
            {
                var cell = wsCtrl.Cell(2, i + 1);
                cell.Value = ctrlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int ctrlRow = 3;
            foreach (var cs in summary.ControlSummaries)
            {
                wsCtrl.Cell(ctrlRow, 1).Value = cs.ControlId;
                wsCtrl.Cell(ctrlRow, 2).Value = cs.ControlLabel;
                wsCtrl.Cell(ctrlRow, 3).Value = cs.CriteriaText;
                wsCtrl.Cell(ctrlRow, 4).Value = cs.PassCount;
                wsCtrl.Cell(ctrlRow, 5).Value = cs.FailCount;
                ctrlRow++;
            }
            wsCtrl.Column(1).Width = 12;
            wsCtrl.Column(2).Width = 30;
            wsCtrl.Column(3).Width = 50;
            wsCtrl.Column(4).Width = 10;
            wsCtrl.Column(5).Width = 10;

            // Sheet 4: Summary stats
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 38: QUAL -> PQM VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",          summary.Database),
                ("QUAL Table",        $"{summary.QualTable} (ID: {summary.QualIdCol}, Name: {summary.QualNameCol})"),
                ("PQM Table",         $"{summary.PqmTable} (Name: {summary.PqmNameCol}, Type: {summary.PqmQualTypeCol}, Total: {summary.PqmMinTimeTotalCol}, WIL: {summary.PqmWilCol}, Subsidy: {summary.PqmTotalSubsidyCol})"),
                ("Population Split",  summary.UseMPrefixPopulationSplit
                    ? "Postgraduate = configured _005 codes or M_____ qualification codes; all other approved rows = Undergraduate."
                    : "Postgraduate = configured _005 codes; all other approved rows = Undergraduate."),
                ("Postgraduate Codes", summary.PostgraduateTypesCsv),
                ("Validation Date",   summary.Timestamp),
                ("",                  ""),
                ("VALIDATION RESULTS",""),
                ("Total QUAL Records",summary.TotalQualRecords.ToString("N0")),
                ("Approved Count",    summary.ApprovedCount.ToString("N0")),
                ("PQM Match Count",   summary.PqmMatchCount.ToString("N0")),
                ("PQM No Match",      summary.PqmNoMatchCount.ToString("N0")),
                ("Review Required",   summary.ReviewRequiredCount.ToString("N0")),
                ("Undergraduate",     summary.UndergraduateCount.ToString("N0")),
                ("Postgraduate",      summary.PostgraduateCount.ToString("N0")),
                ("Overall PASS",      summary.OverallPassCount.ToString("N0")),
                ("Overall FAIL",      summary.OverallFailCount.ToString("N0")),
                ("Exception Rate",    $"{summary.ExceptionRate:F2}%"),
                ("Status",            summary.Status)
            };
            int sumRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var hdrCell = wsSummary.Cell(sumRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(sumRow, 1, sumRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(sumRow, 1).Value = label;
                    wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sumRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sumRow, 2).Value = value;
                    if (label == "Status")
                    {
                        wsSummary.Cell(sumRow, 2).Style.Fill.BackgroundColor =
                            value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(sumRow, 2).Style.Font.Bold = true;
                    }
                }
                sumRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule38Csv(Rule38ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Validation_Number,Qual_Code,Qual_Name,Approval,Qual_Type,Population_Type,Qual_Min_Time,Qual_WIL,Qual_HEQF,Qual_Subsidy,PQM_Match,Review_Required,Match_Note,Authorised_Qualification_Name,PQM_Type,PQM_Min_Time,PQM_WIL,PQM_Accreditation,PQM_Subsidy,C2_Type,C3_Time,C4_WIL,C5_HEQF,C5_Expected,C6_Subsidy,Failed_Controls,Result");
            var rows = exceptionsOnly
                ? summary.ValidationRows.Where(r => r.ValidationResult == "FAIL")
                : summary.ValidationRows.AsEnumerable();
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",",
                    row.ValidationNumber,
                    CsvEscape(row.QualCode),
                    CsvEscape(row.QualName),
                    CsvEscape(row.ApprovalStatus),
                    CsvEscape(row.QualType),
                    CsvEscape(row.PopulationType),
                    CsvEscape(row.MinTimeTotal),
                    CsvEscape(row.MinTimeWIL),
                    CsvEscape(row.HeqfIndicator),
                    CsvEscape(row.TotalSubsidy),
                    row.HasPqmMatch ? "YES" : "NO",
                    row.NeedsReview ? "YES" : "NO",
                    CsvEscape(row.MatchNote),
                    CsvEscape(row.PqmName),
                    CsvEscape(row.PqmQualType),
                    CsvEscape(row.PqmMinTimeTotal),
                    CsvEscape(row.PqmWIL),
                    CsvEscape(row.PqmAccreditation),
                    CsvEscape(row.PqmTotalSubsidy),
                    row.C2_TypeMatch ? "PASS" : "FAIL",
                    row.C3_MinTimeMatch ? "PASS" : "FAIL",
                    row.C4_WILMatch ? "PASS" : "FAIL",
                    row.C5_HeqfMatch ? "PASS" : "FAIL",
                    CsvEscape(row.C5_ExpectedHeqf),
                    row.C6_SubsidyMatch ? "PASS" : "FAIL",
                    CsvEscape(string.Join("; ", row.FailedControls)),
                    CsvEscape(row.ValidationResult)));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule34ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            var censusHeader = !string.IsNullOrWhiteSpace(summary.ClientTableName) &&
                               !string.IsNullOrWhiteSpace(summary.ClientJoinColumn)
                ? "Client_Census_Date"
                : "Stored_Census_Date";
            sb.AppendLine($"Validation_Number,First_Day,Last_Day,c_Days,c_Days_2,Prepared_Census_Date,Actual_Census_Date,{censusHeader},Day_Status,Validation_Status");

            var rows = exceptionsOnly ? summary.Exceptions : summary.ValidationRows;
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",",
                    row.ValidationNumber,
                    CsvEscape(row.FirstDayValue),
                    CsvEscape(row.LastDayValue),
                    CsvEscape(row.CurrentDays?.ToString()),
                    CsvEscape(row.CurrentDaysHalf?.ToString()),
                    CsvEscape(row.ComputedCensusDate),
                    CsvEscape(row.ActualCensusDate),
                    CsvEscape(row.CensusDateValue),
                    CsvEscape(row.DayStatus),
                    CsvEscape(row.ValidationStatus)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule35ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule35Headers(summary);
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ValidationRows)
            {
                var values = headers.Select(header => header switch
                {
                    "Validation Number" => row.ValidationNumber.ToString(),
                    "Validation Result" => string.Equals(row.DuplicateStatus, "UNIQUE", StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL",
                    "Exception Reason" => string.Equals(row.DuplicateStatus, "UNIQUE", StringComparison.OrdinalIgnoreCase) ? "" : "Duplicate value found",
                    "Duplicate Value" => row.DuplicateValue,
                    "Occurrence Count" => row.OccurrenceCount.ToString(),
                    "Duplicate Status" => row.DuplicateStatus,
                    _ => row.DisplayValues.TryGetValue(header, out var value) ? value : null
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule29ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule29Headers(summary);
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.MatchingRows)
            {
                var values = headers.Select(header =>
                {
                    if (string.Equals(header, "Validation_Number", StringComparison.OrdinalIgnoreCase))
                        return row.ValidationNumber.ToString();
                    if (string.Equals(header, "Filter_Value", StringComparison.OrdinalIgnoreCase))
                        return row.FilterValue;
                    if (string.Equals(header, "Breakdown_Value", StringComparison.OrdinalIgnoreCase))
                        return row.BreakdownValue;

                    return row.DisplayValues.TryGetValue(header, out var value) ? value : null;
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule21ValidationSummary summary) => ExportRule21Csv(summary, false);

        public byte[] ExportCsv(Rule17ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule17Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));
            foreach (var row in summary.MatchingRows)
            {
                var values = headers.Select(header =>
                    row.DisplayValues.TryGetValue(header, out var value) ? value : null);
                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule14ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule14Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = headers.Select(header =>
                    row.DisplayValues.TryGetValue(header, out var value) ? value : null);

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule15ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule15Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = headers.Select(header =>
                    row.DisplayValues.TryGetValue(header, out var value) ? value : null);

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule16ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule16Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = headers.Select(header =>
                    row.DisplayValues.TryGetValue(header, out var value) ? value : null);

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule18ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule18Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = headers.Select(header =>
                    row.DisplayValues.TryGetValue(header, out var value) ? value : null);

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule19ValidationSummary summary) =>
            ExportCsv(ToRule29Summary(summary));

        public byte[] ExportCsv(Rule25ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule25Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.FailRows)
            {
                var values = headers.Select(header => header switch
                {
                    "Validation_Number" => row.ValidationNumber.ToString(),
                    "CRSE_CourseCode" => row.CrseCourseCode,
                    "AUDIT_CourseCode" => row.AuditCourseCode,
                    "H16_CourseCode" => row.H16CourseCode,
                    "Reconciliation_Status" => row.ReconciliationStatus,
                    "Issue_Description" => row.IssueDescription,
                    _ => ""
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule23ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule23Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.FailRows)
            {
                var values = headers.Select(header => header switch
                {
                    "Validation_Number" => row.ValidationNumber.ToString(),
                    "STUD_StudentNum" => row.StudStudentNumber,
                    "STUD_QualCode" => row.StudQualificationCode,
                    "STUD_IDNum" => row.StudIdNumber,
                    "AUDIT_StudentNum" => row.AuditStudentNumber,
                    "AUDIT_QualCode" => row.AuditQualificationCode,
                    "AUDIT_IDNum" => row.AuditIdNumber,
                    "H16_StudentNum" => row.H16StudentNumber,
                    "H16_QualCode" => row.H16QualificationCode,
                    "H16_IDNum" => row.H16IdNumber,
                    "Control_Type" => row.ControlType,
                    "Result_Type" => row.ResultType,
                    "Reconciliation_Status" => row.ReconciliationStatus,
                    "Issue_Description" => row.IssueDescription,
                    _ => ""
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule13ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = new[]
            {
                "Extract_Number",
                "Qualification_Code",
                "Major_Field_CESM (_006)",
                "Qualification_Name_Designator (_003)",
                "Qualification_Approval_Status",
                "Qualification_Type_Descriptor",
                "Legacy_Indicator",
                "NQF_Exit_Level",
                "Minimum_Total_Credits",
                "Linked_Student_Count",
                "STUD_Link_Result",
                "PQM_CESM_Code",
                "PQM_Authorised_Name",
                "Name_Match",
                "CESM_Code_Match",
                "PQM_Needs_Review",
                "PQM_Result",
                "PQM_Exception_Reason",
                "Validation_Result",
                "Validation_Reason"
            };
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = new[]
                {
                    row.ValidationNumber.ToString(),
                    row.QualificationCode001,
                    row.CourseCode030,
                    row.QualificationDescription003,
                    row.FoundationFlag106,
                    row.QualificationType005,
                    row.BridgeQualificationCode001,
                    row.CrseCourseCode030,
                    row.FoundationCourse091,
                    (row.StudLinkCount > 0 ? row.StudLinkCount : (int.TryParse(row.StudentType, out var csvSc) ? csvSc : 0)).ToString(),
                    string.IsNullOrWhiteSpace(row.StudLinkResult)
                        ? (row.StudLinkCount > 0 ? "PASS" : "FAIL")
                        : row.StudLinkResult,
                    string.IsNullOrWhiteSpace(row.PqmCode) ? "Not found in PQM" : row.PqmCode,
                    string.IsNullOrWhiteSpace(row.PqmName) ? "Not found in PQM" : row.PqmName,
                    row.PqmNameMatch ? "YES" : (string.IsNullOrWhiteSpace(row.PqmName) ? "—" : "NO"),
                    row.PqmCodeMatch ? "YES" : (string.IsNullOrWhiteSpace(row.PqmCode) ? "—" : "NO"),
                    row.PqmNeedsReview ? "YES" : "",
                    row.PqmResult ?? "",
                    row.PqmExceptionReason ?? "",
                    row.ValidationResult,
                    row.ValidationExplanation ?? ""
                };
                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule12ValidationSummary summary)
        {
            var r12CsvX1 = !string.IsNullOrWhiteSpace(summary.CregExtra1Col);
            var r12CsvX2 = !string.IsNullOrWhiteSpace(summary.CregExtra2Col);
            var r12CsvXF = !string.IsNullOrWhiteSpace(summary.CregFilterCol);
            var r12CsvX3 = !string.IsNullOrWhiteSpace(summary.CregExtra3Col);
            var r12CsvCX = !string.IsNullOrWhiteSpace(summary.CresExtra1Col);
            var sb = new StringBuilder();
            var headerParts = new List<string>
            {
                summary.CregStudentCol, summary.CregQualCol, summary.CregCourseCol
            };
            if (r12CsvX1) headerParts.Add(summary.CregExtra1Col!);
            if (r12CsvX2) headerParts.Add(summary.CregExtra2Col!);
            if (r12CsvXF) headerParts.Add(summary.CregFilterCol!);
            if (r12CsvX3) headerParts.Add(summary.CregExtra3Col!);
            headerParts.Add(summary.QualJoinCol);
            headerParts.Add(summary.QualDescCol);
            headerParts.Add(summary.CresCourseCol);
            headerParts.Add(summary.CresStatusCol);
            if (r12CsvCX) headerParts.Add(summary.CresExtra1Col!);
            headerParts.Add("Validation Result");
            headerParts.Add("Validation Explanation");
            sb.AppendLine(string.Join(",", headerParts.Select(CsvEscape)));

            // CSV exports ALL rows — CSV is plain text and handles large datasets efficiently.
            foreach (var row in summary.ReviewRows)
            {
                string Display(string key) => row.DisplayValues.TryGetValue(key, out var v) ? v ?? "" : "";
                var values = new List<string>
                {
                    CsvEscape(Display("CREG__007")),
                    CsvEscape(Display("CREG__001")),
                    CsvEscape(Display("CREG__030"))
                };
                if (r12CsvX1) values.Add(CsvEscape(Display("CREG__EXTRA1")));
                if (r12CsvX2) values.Add(CsvEscape(Display("CREG__EXTRA2")));
                if (r12CsvXF) values.Add(CsvEscape(Display("CREG__FILTER")));
                if (r12CsvX3) values.Add(CsvEscape(Display("CREG__EXTRA3")));
                values.Add(CsvEscape(Display("QUAL__001")));
                values.Add(CsvEscape(Display("QUAL__003")));
                values.Add(CsvEscape(Display("CRES__030")));
                values.Add(CsvEscape(Display("CRES__031")));
                if (r12CsvCX) values.Add(CsvEscape(Display("CRES__EXTRA1")));
                values.Add(CsvEscape(row.ValidationResult));
                values.Add(CsvEscape(row.ValidationExplanation));
                sb.AppendLine(string.Join(",", values));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule10ValidationSummary summary)
        {
            var detailKeys = summary.ReviewRows
                .SelectMany(row => row.DisplayValues.Keys)
                .Where(key => !string.Equals(key, "Validation_Result", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "Validation_Explanation", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "RULE_LABEL", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "RESULT_BADGE", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "FINAL_RESULT_MESSAGE", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            var headers = new List<string> { "Rule", "Validation Result", "Validation Explanation" };
            headers.AddRange(detailKeys.Select(key => GetIntegrityDetailHeader(summary, key)));
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = new List<string>
                {
                    row.RuleId.ToString(CultureInfo.InvariantCulture),
                    row.ValidationResult,
                    row.ValidationExplanation
                };
                values.AddRange(detailKeys.Select(key => row.DisplayValues.TryGetValue(key, out var value) ? value ?? "" : ""));
                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule11ValidationSummary summary)
            => ExportRule11Csv(summary, false);

        public byte[] ExportCsv(Rule20ValidationSummary summary)
        {
            var headers = GetRule20DashboardHeaders();
            var previewRows = BuildRule20DashboardPreviewRows(summary.ReviewRows);
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in previewRows)
            {
                var values = headers.Select(header => header switch
                {
                    "Validation_Number"                  => row.ValidationNumber.ToString(),
                    "Student_Number"                     => row.StudentNumber007,
                    "South_African_Identity_Number"      => row.StudentColumn008,
                    "Surname"                            => row.StudentColumn066,
                    "First_Name"                         => row.StudentColumn067,
                    "Middle_Name"                        => row.StudentColumn068,
                    "Date_of_Birth"                      => row.StudentColumn012,
                    "Gender"                             => row.StudentColumn013,
                    "Race"                               => row.StudentColumn014,
                    "Nationality"                        => row.StudentColumn015,
                    "Entrance_Category"                  => row.StudentColumn010,
                    "CESSM_Category"                     => row.StudentColumn026,
                    "Qualification_Requirements_Status"  => row.StudentColumn025,
                    "Qualification_Code"                 => row.QualificationCode001,
                    "Qualification_Description"          => row.QualificationDescription003,
                    "Foundation_Student_Indicator"       => row.FoundationFlag106,
                    "Course_Code"                        => row.CourseCode030,
                    "Course_Name"                        => row.CourseName058,
                    "Foundation_Course_Indicator"        => row.FoundationCourse091,
                    "Validation_Result"                  => row.ValidationResult,
                    "Audit_Rule"                         => "Rule 20 - Foundation Validation",
                    _                                    => ""
                });
                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule22ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule22Headers(summary);
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = headers.Select(header => header switch
                {
                    "Validation_Number"              => row.ValidationNumber.ToString(),
                    "Control_Type"                   => row.ControlType,
                    "Control_Definition"             => row.ControlDefinition,
                    "Control_Row_Number"             => row.SampleNumber.ToString(),
                    "Staff_Number"                   => row.StaffNumber037,
                    "Employment_Commencement_Year"   => row.Year038,
                    "Personnel_Category"             => row.Col039,
                    "Rank_of_Staff_Member"           => row.Col040,
                    "Date_of_Birth"                  => row.Col011,
                    "Gender"                         => row.Col012,
                    "Race"                           => row.Col013,
                    "Nationality"                    => row.Col014,
                    "Permanent_Temporary_Status"     => row.Col041,
                    "Full_Time_Part_Time_Status"     => row.Col042,
                    "Staff_Qualification"            => row.Col046,
                    "Joint_Appointment"              => row.Col047,
                    "On_Payroll_Code"                => row.Col048,
                    "Research_Fellow"                => row.Col094,
                    "Validation_Result"              => row.ValidationResult,
                    _                                => ""
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule24ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule24Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.FailRows)
            {
                var values = headers.Select(header => header switch
                {
                    "Validation_Number" => row.ValidationNumber.ToString(),
                    "QUAL_QualCode" => row.QualQualCode,
                    "QUAL_ApprovalStatus" => row.QualApprovalStatus,
                    "STUD_QualCode" => row.StudQualCode,
                    "AUDIT_QualCode" => row.AuditQualCode,
                    "H16_QualCode" => row.H16QualCode,
                    "Control_Type" => row.ControlType,
                    "Reconciliation_Status" => row.ReconciliationStatus,
                    "Issue_Description" => row.IssueDescription,
                    _ => ""
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule27ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = new[] { "Validation_Number", "Filter_Value" }
                .Concat(summary.MatchingRows
                    .SelectMany(r => r.DisplayValues.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                .ToList();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.MatchingRows)
            {
                var values = headers.Select(header =>
                {
                    if (string.Equals(header, "Validation_Number", StringComparison.OrdinalIgnoreCase))
                        return row.ValidationNumber.ToString();
                    if (string.Equals(header, "Filter_Value", StringComparison.OrdinalIgnoreCase))
                        return row.FilterValue;

                    return row.DisplayValues.TryGetValue(header, out var value) ? value : null;
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule32ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            var headers = GetRule32Headers(summary);
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            var rows = exceptionsOnly ? summary.RemainingRows : summary.ExcludedRows.Concat(summary.RemainingRows).ToList();
            foreach (var row in rows)
            {
                var values = headers.Select(header =>
                {
                    if (string.Equals(header, "Validation_Number", StringComparison.OrdinalIgnoreCase))
                        return row.ValidationNumber.ToString();
                    if (string.Equals(header, "Classification", StringComparison.OrdinalIgnoreCase))
                        return row.Classification;
                    if (string.Equals(header, "Error_Type_Value", StringComparison.OrdinalIgnoreCase))
                        return row.ErrorTypeValue;
                    if (string.Equals(header, "Error_Code", StringComparison.OrdinalIgnoreCase))
                        return row.ErrorCode;
                    if (string.Equals(header, "Normalized_Error_Code", StringComparison.OrdinalIgnoreCase))
                        return row.NormalizedErrorCode;

                    return row.DisplayValues.TryGetValue(header, out var value) ? value : null;
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule31ValidationSummary summary, bool exceptionsOnly = false) =>
            ExportCsv(ToRule32Summary(summary), exceptionsOnly);

        // ─── Rule 39 Export ──────────────────────────────────────────────────────

        public byte[] ExportRule39Excel(Rule39ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = new[]
            {
                "#", "STUD Qual Ref", "STUD _007", "STUD _008", "FTE Value", "STUD _012", "STUD _026",
                "QUAL Code", "QUAL Name", "NAL Qual Name", "NAL Aligned Name", "NAL Category", "HEQSF Ref",
                "SAQA ID", "NQF Level", "Credits", "Outcome", "Result", "Reason"
            };
            const int colCount = 19;

            var wsAll = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsAll, 1, "RULE 39 — FIRST-TIME ENTERING STUDENTS vs NON-ALIGNED QUALIFICATIONS", colCount);
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsAll.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int rowIdx = 3;
            foreach (var r in summary.FlaggedRows.Concat(summary.ClearSampleRows).OrderBy(r => r.RowNumber))
                WriteRule39ExcelRow(wsAll, rowIdx++, r);
            for (int c = 1; c <= colCount; c++) wsAll.Column(c).AdjustToContents();

            var wsEx = wb.Worksheets.Add("FLAGGED");
            StyleHeaderRow(wsEx, 1, "RULE 39 — FLAGGED (Non-Aligned Qual Found)", colCount);
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsEx.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int exIdx = 3;
            foreach (var r in summary.FlaggedRows) WriteRule39ExcelRow(wsEx, exIdx++, r);
            for (int c = 1; c <= colCount; c++) wsEx.Column(c).AdjustToContents();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 39: FIRST-TIME ENTERING vs NON-ALIGNED QUALIFICATIONS", 2);
            var summaryData = new List<(string Label, string Value)>
            {
                ("Database",                  summary.Database),
                ("STUD Table",                $"{summary.StudTable} | First-time column: {summary.StudFirstTimeColumn} = '{summary.StudFirstTimeValue}'"),
                ("STUD Qual Ref Column",      summary.StudQualRefColumn),
                ("QUAL Table",                summary.QualTable),
                ("QUAL Code Column",          summary.QualCodeColumn),
                ("QUAL Name Column",          summary.QualNameColumn),
                ("NAL Table",                 $"{summary.NalTable} | Category column: {summary.NalCategoryColumn} = '{summary.NalCategoryValue}'"),
                ("Join Key",                  $"STUD.{summary.StudQualRefColumn} = QUAL.{summary.QualCodeColumn} -> NAL.{summary.NalRefColumn}"),
                ("Validation Date",           summary.Timestamp),
                ("",                          ""),
                ("VALIDATION RESULTS",        ""),
                ("STUD Total Records",        summary.StudTotalCount.ToString("N0")),
                ("First-Time Entering (FTE)", summary.TotalValidated.ToString("N0")),
                ("QUAL Total Records",        summary.QualTotalCount.ToString("N0")),
                ("NAL Category Count",        summary.NalCategoryCount.ToString("N0")),
                ("FLAGGED (Non-Aligned)",     summary.FlaggedCount.ToString("N0")),
                ("CLEAR (Not Found)",         summary.ClearCount.ToString("N0")),
                ("Exception Rate",            $"{summary.ExceptionRate:F2}%"),
                ("Status",                    summary.Status)
            };
            if (!string.IsNullOrWhiteSpace(summary.Stud007Column)) summaryData.Insert(3, ("STUD _007 Column", summary.Stud007Column));
            if (!string.IsNullOrWhiteSpace(summary.Stud008Column)) summaryData.Insert(4, ("STUD _008 Column", summary.Stud008Column));
            if (!string.IsNullOrWhiteSpace(summary.Stud012Column)) summaryData.Insert(5, ("STUD _012 Column", summary.Stud012Column));
            if (!string.IsNullOrWhiteSpace(summary.Stud026Column)) summaryData.Insert(6, ("STUD _026 Column", summary.Stud026Column));
            int sumRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var hdrCell = wsSummary.Cell(sumRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(sumRow, 1, sumRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(sumRow, 1).Value = label;
                    wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sumRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sumRow, 2).Value = value;
                    if (label == "Status")
                    {
                        wsSummary.Cell(sumRow, 2).Style.Fill.BackgroundColor =
                            value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(sumRow, 2).Style.Font.Bold = true;
                    }
                }
                sumRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteRule39ExcelRow(IXLWorksheet ws, int rowIdx, Rule39ValidationRowViewModel row)
        {
            ws.Cell(rowIdx, 1).Value  = row.RowNumber;
            ws.Cell(rowIdx, 2).Value  = row.StudQualRef;
            ws.Cell(rowIdx, 3).Value  = row.Stud007Value ?? "";
            ws.Cell(rowIdx, 4).Value  = row.Stud008Value ?? "";
            ws.Cell(rowIdx, 5).Value  = row.Stud010Value;
            ws.Cell(rowIdx, 6).Value  = row.Stud012Value ?? "";
            ws.Cell(rowIdx, 7).Value  = row.Stud026Value ?? "";
            ws.Cell(rowIdx, 8).Value  = row.QualCodeValue ?? "";
            ws.Cell(rowIdx, 9).Value  = row.QualNameValue ?? "";
            ws.Cell(rowIdx, 10).Value = row.NalQualName ?? "";
            ws.Cell(rowIdx, 11).Value = row.NalAlignedName ?? "";
            ws.Cell(rowIdx, 12).Value = row.NalCategory ?? "";
            ws.Cell(rowIdx, 13).Value = row.NalHeqsfRef ?? "";
            ws.Cell(rowIdx, 14).Value = row.NalSaqaId ?? "";
            ws.Cell(rowIdx, 15).Value = row.NalNqf ?? "";
            ws.Cell(rowIdx, 16).Value = row.NalCredits ?? "";
            ws.Cell(rowIdx, 17).Value = row.NalOutcome ?? "";
            ws.Cell(rowIdx, 18).Value = row.Result;
            ws.Cell(rowIdx, 19).Value = row.ExceptionReason ?? "";

            var isFlagged = string.Equals(row.Result, "FLAGGED", StringComparison.OrdinalIgnoreCase);
            var bg = isFlagged ? XLColor.FromHtml("#FFF3F3") : XLColor.FromHtml("#F3FFF3");
            ws.Range(rowIdx, 1, rowIdx, 19).Style.Fill.BackgroundColor = bg;
            ws.Cell(rowIdx, 18).Style.Font.Bold = true;
            ws.Cell(rowIdx, 18).Style.Font.FontColor = isFlagged ? XLColor.FromHtml("#B71C1C") : XLColor.FromHtml("#1B5E20");
        }

        public byte[] ExportRule39Csv(Rule39ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Row_No,STUD_Qual_Ref,STUD_007,STUD_008,First_Time_Value,STUD_012,STUD_026,QUAL_Code,QUAL_Name,NAL_Qual_Name,NAL_Aligned_Name,NAL_Category,HEQSF_Ref,SAQA_ID,NQF_Level,Credits,Outcome,Result,Exception_Reason");
            var rows = exceptionsOnly
                ? summary.FlaggedRows.AsEnumerable()
                : summary.FlaggedRows.Concat(summary.ClearSampleRows).OrderBy(row => row.RowNumber);
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",",
                    row.RowNumber,
                    CsvEscape(row.StudQualRef),
                    CsvEscape(row.Stud007Value),
                    CsvEscape(row.Stud008Value),
                    CsvEscape(row.Stud010Value),
                    CsvEscape(row.Stud012Value),
                    CsvEscape(row.Stud026Value),
                    CsvEscape(row.QualCodeValue),
                    CsvEscape(row.QualNameValue),
                    CsvEscape(row.NalQualName),
                    CsvEscape(row.NalAlignedName),
                    CsvEscape(row.NalCategory),
                    CsvEscape(row.NalHeqsfRef),
                    CsvEscape(row.NalSaqaId),
                    CsvEscape(row.NalNqf),
                    CsvEscape(row.NalCredits),
                    CsvEscape(row.NalOutcome),
                    CsvEscape(row.Result),
                    CsvEscape(row.ExceptionReason)));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportRule21Excel(Rule21ValidationSummary summary)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(summary);
            var r39 = System.Text.Json.JsonSerializer.Deserialize<Rule39ValidationSummary>(json) ?? new Rule39ValidationSummary();
            return ExportRule39Excel(r39);
        }

        public byte[] ExportRule21Csv(Rule21ValidationSummary summary, bool exceptionsOnly = false)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(summary);
            var r39 = System.Text.Json.JsonSerializer.Deserialize<Rule39ValidationSummary>(json) ?? new Rule39ValidationSummary();
            return ExportRule39Csv(r39, exceptionsOnly);
        }

        // ─── SQL Export ───────────────────────────────────────────────────────
        public byte[] ExportSql(string sql) => Encoding.UTF8.GetBytes(sql);

        public byte[] ExportRule64Excel(Rule64ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var headers = new[] { "Source Table", "STUD Student No", "CREG Student No", "PRODUCTION Student No", "STUD Compare Value", "CREG Compare Value", "Error Code", "Result", "Explanation" };

            var allRows = (summary.FailRows ?? new List<Rule64ReviewRow>())
                .Concat(summary.PassRows ?? new List<Rule64ReviewRow>())
                .ToList();

            // Sheet 1: All Results (FAIL then PASS)
            var wsAll = wb.Worksheets.Add("All Results");
            StyleHeaderRow(wsAll, 1, "RULE 64 — STUD TO CREG VALIDATION", headers.Length);
            for (var i = 0; i < headers.Length; i++) { var c = wsAll.Cell(2, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000"); c.Style.Font.FontColor = XLColor.White; }
            WriteRule64Rows(wsAll, 3, allRows, headers);

            // Sheet 2: FAIL Only
            var wsFail = wb.Worksheets.Add("FAIL — Exceptions");
            StyleHeaderRow(wsFail, 1, "RULE 64 — EXCEPTIONS ONLY", headers.Length);
            for (var i = 0; i < headers.Length; i++) { var c = wsFail.Cell(2, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000"); c.Style.Font.FontColor = XLColor.White; }
            WriteRule64Rows(wsFail, 3, summary.FailRows ?? new List<Rule64ReviewRow>(), headers);

            // Sheet 3: PASS Only
            var wsPass = wb.Worksheets.Add("PASS — Found in CREG");
            StyleHeaderRow(wsPass, 1, "RULE 64 — PASS ROWS", headers.Length);
            for (var i = 0; i < headers.Length; i++) { var c = wsPass.Cell(2, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B5E20"); c.Style.Font.FontColor = XLColor.White; }
            WriteRule64Rows(wsPass, 3, summary.PassRows ?? new List<Rule64ReviewRow>(), headers);

            // Sheet 4: Summary
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 64: STUD TO CREG VALIDATION SUMMARY", 2);
            var summaryData = new[]
            {
                ("Database",           summary.Database),
                ("STUD Table",         summary.StudTable),
                ("CREG Table",         summary.CregTable),
                ("PRODUCTION Table",   summary.ProdTable),
                ("Timestamp",          summary.Timestamp),
                ("Total Rows",         summary.TotalCount.ToString()),
                ("PASS",               summary.PassCount.ToString()),
                ("FAIL",               summary.FailCount.ToString()),
                ("Exception Rate",     $"{summary.ExceptionRate}%"),
                ("Status",             summary.Status)
            };
            for (var i = 0; i < summaryData.Length; i++)
            {
                wsSummary.Cell(i + 2, 1).Value = summaryData[i].Item1;
                wsSummary.Cell(i + 2, 1).Style.Font.Bold = true;
                wsSummary.Cell(i + 2, 2).Value = summaryData[i].Item2;
            }
            wsSummary.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteRule64Rows(IXLWorksheet ws, int startRow, List<Rule64ReviewRow> rows, string[] headers)
        {
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Source Table"] = 1, ["STUD Student No"] = 2, ["CREG Student No"] = 3,
                ["PRODUCTION Student No"] = 4, ["STUD Compare Value"] = 5, ["CREG Compare Value"] = 6,
                ["Error Code"] = 7, ["Result"] = 8, ["Explanation"] = 9
            };
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                ws.Cell(rowIndex, 1).Value = row.SourceTable;
                ws.Cell(rowIndex, 2).Value = row.StudentNo;
                ws.Cell(rowIndex, 3).Value = row.CregStudentNo;
                ws.Cell(rowIndex, 4).Value = row.ProdStudentNo;
                ws.Cell(rowIndex, 5).Value = row.StudCompareValue;
                ws.Cell(rowIndex, 6).Value = row.CregCompareValue;
                ws.Cell(rowIndex, 7).Value = row.ErrorCode;
                ws.Cell(rowIndex, 8).Value = row.ValidationResult;
                ws.Cell(rowIndex, 9).Value = row.ValidationExplanation;
                var fill = string.Equals(row.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase) ? "#F3FFF3" : "#FFF3F3";
                ws.Range(rowIndex, 1, rowIndex, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }
            ws.Columns().AdjustToContents();
            ws.Column(9).Width = 80;
        }



        private static Rule29ValidationSummary ToRule29Summary(Rule19ValidationSummary summary) =>
            new()
            {
                Success = summary.Success,
                TotalValidated = summary.TotalValidated,
                MatchingCount = summary.MatchingCount,
                DisplayedCount = summary.DisplayedCount,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                TableName = $"{summary.StudTable} -> {summary.QualTable}",
                FilterColumn = summary.FulfilledColumn,
                FilterValue = summary.FulfilledValue,
                BreakdownColumn = summary.QualTypeColumn,
                SampleSize = summary.TotalValidated,
                ShowAllRecords = summary.ShowAllRecords,
                Sampled = false,
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                Warning = summary.Warning,
                Error = summary.Error,
                Breakdown = summary.Breakdown
                    .Select(item => new Rule29BreakdownItemViewModel
                    {
                        Value = item.Value,
                        Count = item.Count
                    })
                    .ToList(),
                MatchingRows = summary.MatchingRows
                    .Select(row => new Rule29ValidationRowRecord
                    {
                        ValidationNumber = row.ValidationNumber,
                        FilterValue = row.FulfilledValue,
                        BreakdownValue = row.QualTypeValue,
                        DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
                    })
                    .ToList()
            };

        // ─── Helpers ──────────────────────────────────────────────────────────
        private static void StyleHeaderRow(IXLWorksheet ws, int row, string title, int colSpan)
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

        private static void WriteValidationHeaderRow(IXLWorksheet ws, int row, ValidationSummary summary)
        {
            var headers = new[] { "Validation Number", "Validation Result", "Exception Reason", "Student ID" }
                .Concat(GetAdditionalHeaders(summary))
                .ToList();

            for (int i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteValidationRows(IXLWorksheet ws, int startRow, ValidationSummary summary)
        {
            var headers = GetAdditionalHeaders(summary);
            var rowIndex = startRow;

            foreach (var row in summary.ValidationRows)
            {
                ws.Cell(rowIndex, 1).Value = row.ValidationNumber;
                ws.Cell(rowIndex, 2).Value = row.ValidationResult;
                ws.Cell(rowIndex, 3).Value = row.ExceptionReason ?? "";
                ws.Cell(rowIndex, 4).Value = row.StudentId;

                for (int i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    row.AdditionalColumns.TryGetValue(header, out var value);
                    ws.Cell(rowIndex, 5 + i).Value = value ?? "";
                }

                if (row.ValidationResult == "FAIL")
                    ws.Range(rowIndex, 1, rowIndex, 4 + headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                else if (row.ValidationResult == "PASS")
                    ws.Range(rowIndex, 1, rowIndex, 4 + headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#F3FFF3");

                rowIndex++;
            }

            for (int c = 1; c <= 4 + headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static List<string> GetAdditionalHeaders(ValidationSummary summary) =>
            summary.ValidationRows
                .SelectMany(r => r.AdditionalColumns.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

        private static List<string> GetRule32Headers(Rule32ValidationSummary summary) =>
            new[] { "Validation_Number", "Classification", "Error_Type_Value", "Error_Code", "Normalized_Error_Code" }
                .Concat(summary.ExcludedRows
                    .Concat(summary.RemainingRows)
                    .SelectMany(r => r.DisplayValues.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                .ToList();

        private static List<string> GetRule35Headers(Rule35ValidationSummary summary) =>
            new[] { "Validation Number", "Validation Result", "Exception Reason", "Duplicate Value", "Occurrence Count", "Duplicate Status" }
                .Concat(summary.ValidationRows
                    .SelectMany(r => r.DisplayValues.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                .ToList();

        private static void WriteRule35HeaderRow(IXLWorksheet ws, int row, IReadOnlyList<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule35Rows(IXLWorksheet ws, int startRow, IReadOnlyCollection<Rule35ValidationRowRecord> rows, IReadOnlyList<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation Number" => row.ValidationNumber.ToString(),
                        "Validation Result" => string.Equals(row.DuplicateStatus, "UNIQUE", StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL",
                        "Exception Reason" => string.Equals(row.DuplicateStatus, "UNIQUE", StringComparison.OrdinalIgnoreCase) ? "" : "Duplicate value found",
                        "Duplicate Value" => row.DuplicateValue,
                        "Occurrence Count" => row.OccurrenceCount.ToString(),
                        "Duplicate Status" => row.DuplicateStatus,
                        _ => row.DisplayValues.TryGetValue(header, out var displayValue) ? displayValue : null
                    };

                    ws.Cell(rowIndex, i + 1).Value = value ?? "";
                }

                var fillColor = string.Equals(row.DuplicateStatus, "DUPLICATE", StringComparison.OrdinalIgnoreCase)
                    ? XLColor.FromHtml("#FFF3F3")
                    : XLColor.FromHtml("#F3FFF3");
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = fillColor;
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static List<string> GetRule29Headers(Rule29ValidationSummary summary) =>
            new[] { "Validation_Number", "Filter_Value", "Breakdown_Value" }
                .Concat(summary.MatchingRows
                    .SelectMany(r => r.DisplayValues.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                .ToList();

        private static List<string> GetRule17Headers() =>
            new()
            {
                "Extract_Number",
                "Student_Number",
                "South_African_ID_Number",
                "Qualification_Code",
                "Qualification_Name",
                "Entrance_Category",
                "Date_Of_Birth",
                "Gender",
                "Race",
                "Nationality",
                "Citizen_Resident_Status",
                "NSFAS_Status",
                "Previous_Years_Activity",
                "Attendance_Mode",
                "Qualification_Fulfilled_Indicator",
                "Student_Last_Name",
                "Student_First_Name",
                "Student_Middle_Name",
                "Validation_Result"
            };

        private static List<string> GetRule14Headers() =>
            new()
            {
                "Sample_Number",
                "Control_Type",
                "Control_Label",
                "Course_Code",
                "Course_Name",
                "Course_Approval_Status",
                "Course_CESM",
                "Course_Level_Code",
                "Contact_Only_Availability",
                "Distance_Only_Availability",
                "Mixed_Mode_Availability",
                "Experiential_Training_Indicator",
                "Foundation_Course",
                "NQF_Level",
                "NQF_Credit",
                "CREG_Course_Code",
                "Validation_Result",
                "Validation_Reason"
            };

        private static List<string> GetRule15Headers() =>
            new()
            {
                "Extract_Number",
                "Control_Type",
                "Control_Label",
                "Qualification_Code",
                "Course_Code",
                "Qualification_Name_Designator",
                "Approval_Status",
                "Filler1",
                "Course_Level_Credit_Value",
                "Completed_Research_Course_Credit_Value",
                "First_Student_Number",
                "Registered_Student_Count",
                "Validation_Result",
                "Validation_Reason"
            };

        private static List<string> GetRule16Headers() =>
            new()
            {
                "Extract_Number",
                "Control_Type",
                "Student_Number",
                "Student_Qualification_Code",
                "Qualification_Fulfilled_Indicator",
                "Attendance_Mode",
                "CREG_Qualification_Code",
                "CREG_Student_Number",
                "CREG_Course_Code",
                "CRSE_Course_Code",
                "Foundation_Course_Indicator",
                "CRSE_058",
                "Control_Check"
            };

        private static List<string> GetRule18Headers() =>
            new()
            {
                "Extract_Number",
                "Control_Type",
                "Student_Number",
                "Student_Qualification_Code",
                "NSFAS_Status",
                "Attendance_Mode",
                "Qualification_Fulfilled_Indicator",
                "CREG_Qualification_Code",
                "CREG_Course_Code",
                "CRSE_Course_Code",
                "Foundation_Course_Indicator",
                "CRSE_058",
                "Validation_Result"
            };

        private static List<string> GetRule24Headers() =>
            new()
            {
                "Validation_Number",
                "QUAL_QualCode",
                "QUAL_ApprovalStatus",
                "STUD_QualCode",
                "AUDIT_QualCode",
                "H16_QualCode",
                "Control_Type",
                "Reconciliation_Status",
                "Issue_Description"
            };

        private static List<string> GetRule23Headers() =>
            new()
            {
                "Validation_Number",
                "STUD_StudentNum",
                "STUD_QualCode",
                "STUD_IDNum",
                "AUDIT_StudentNum",
                "AUDIT_QualCode",
                "AUDIT_IDNum",
                "H16_StudentNum",
                "H16_QualCode",
                "H16_IDNum",
                "Control_Type",
                "Result_Type",
                "Reconciliation_Status",
                "Issue_Description"
            };

        private static List<string> GetRule20Headers() =>
            new()
            {
                "Validation_Number",
                "Part_Code",
                "Part_Title",
                "Part_Description",
                "Student_Number_007",
                "Student_Column_008",
                "Student_Column_010",
                "Student_Column_012",
                "Student_Column_026",
                "Qualification_Code_001",
                "Foundation_Flag_106",
                "Bridge_Qualification_Code_001",
                "Course_Code_030",
                "Foundation_Course_091",
                "Crse_Course_Code_030",
                "Name_019",
                "ID_Number_024",
                "Qualification_Description_003",
                "Qualification_Type_005",
                "Student_Type",
                "Notebook_Status",
                "Validation_Result",
                "Validation_Explanation"
            };

        private static List<string> GetRule20DashboardHeaders() =>
            new()
            {
                "Validation_Number",
                "Student_Number",
                "South_African_Identity_Number",
                "Surname",
                "First_Name",
                "Middle_Name",
                "Date_of_Birth",
                "Gender",
                "Race",
                "Nationality",
                "Entrance_Category",
                "CESSM_Category",
                "Qualification_Requirements_Status",
                "Qualification_Code",
                "Qualification_Description",
                "Foundation_Student_Indicator",
                "Course_Code",
                "Course_Name",
                "Foundation_Course_Indicator",
                "Validation_Result",
                "Audit_Rule"
            };

        private static List<string> GetRule22Headers(Rule22ValidationSummary summary) =>
            new()
            {
                "Validation_Number",
                "Control_Type",
                "Control_Definition",
                "Control_Row_Number",
                "Staff_Number",
                "Employment_Commencement_Year",
                "Personnel_Category",
                "Rank_of_Staff_Member",
                "Date_of_Birth",
                "Gender",
                "Race",
                "Nationality",
                "Permanent_Temporary_Status",
                "Full_Time_Part_Time_Status",
                "Staff_Qualification",
                "Joint_Appointment",
                "On_Payroll_Code",
                "Research_Fellow",
                "Validation_Result"
            };

        private static List<Rule22MappedColumnViewModel> GetRule22MappedColumns(Rule22ValidationSummary summary) =>
            (summary.MappedColumns?.Any() == true
                ? summary.MappedColumns
                : Rule22ColumnMappingHelper.Build(summary))
            .ToList();

        private static string GetRule22ColumnHeader(Rule22MappedColumnViewModel mapping) =>
            mapping.Label;

        private static List<string> GetRule25Headers() =>
            new()
            {
                "Validation_Number",
                "CRSE_CourseCode",
                "AUDIT_CourseCode",
                "H16_CourseCode",
                "Reconciliation_Status",
                "Issue_Description"
            };

        private static void WriteRule32HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule32Rows(IXLWorksheet ws, int startRow, List<Rule32ValidationRowRecord> rows, List<string> headers, string fillColor)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "Classification" => row.Classification,
                        "Error_Type_Value" => row.ErrorTypeValue,
                        "Error_Code" => row.ErrorCode,
                        "Normalized_Error_Code" => row.NormalizedErrorCode,
                        _ => row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fillColor);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule29HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule21HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule16HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule14HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule15HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule18HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule29Rows(IXLWorksheet ws, int startRow, List<Rule29ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "Filter_Value" => row.FilterValue,
                        "Breakdown_Value" => row.BreakdownValue,
                        _ => row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteLegacyDynRows(IXLWorksheet ws, int startRow, IEnumerable<Rule17ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : "";
                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                if (row.DisplayValues.TryGetValue("Validation_Result", out var validationResult))
                {
                    var fill = string.Equals(validationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                        ? "#FFF3F3"
                        : "#F3FFF3";
                    ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                }

                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule16Rows(IXLWorksheet ws, int startRow, List<Rule16ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : "";
                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            SetRule16ColumnWidths(ws);
        }

        private static void WriteRule14Rows(IXLWorksheet ws, int startRow, List<Rule14ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : "";
                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            SetRule14ColumnWidths(ws);
        }

        private static void WriteRule15Rows(IXLWorksheet ws, int startRow, List<Rule15ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : "";
                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            SetRule15ColumnWidths(ws);
        }

        private static void WriteRule18Rows(IXLWorksheet ws, int startRow, List<Rule18ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : "";
                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            SetRule18ColumnWidths(ws);
        }

        private static void WriteRule16ControlSheet(XLWorkbook workbook, string? sheetName, string title, List<Rule16ValidationRowRecord> rows, List<string> headers, IXLWorksheet? existingSheet = null)
        {
            var ws = existingSheet ?? workbook.Worksheets.Add(sheetName!);
            StyleHeaderRow(ws, 1, title, headers.Count);
            WriteRule16HeaderRow(ws, 2, headers);
            WriteRule16Rows(ws, 3, rows, headers);
        }

        private static void WriteRule14ControlSheet(XLWorkbook workbook, string sheetName, string title, List<Rule14ValidationRowRecord> rows, List<string> headers)
        {
            var ws = workbook.Worksheets.Add(sheetName);
            StyleHeaderRow(ws, 1, title, headers.Count);
            WriteRule14HeaderRow(ws, 2, headers);
            WriteRule14Rows(ws, 3, rows, headers);
        }

        private static void WriteRule15ControlSheet(XLWorkbook workbook, string sheetName, string title, List<Rule15ValidationRowRecord> rows, List<string> headers)
        {
            var ws = workbook.Worksheets.Add(sheetName);
            StyleHeaderRow(ws, 1, title, headers.Count);
            WriteRule15HeaderRow(ws, 2, headers);
            WriteRule15Rows(ws, 3, rows, headers);
        }

        private static void WriteRule18ControlSheet(XLWorkbook workbook, string sheetName, string title, List<Rule18ValidationRowRecord> rows, List<string> headers)
        {
            var ws = workbook.Worksheets.Add(sheetName);
            StyleHeaderRow(ws, 1, title, headers.Count);
            WriteRule18HeaderRow(ws, 2, headers);
            WriteRule18Rows(ws, 3, rows, headers);
        }

        private static void SetRule14ColumnWidths(IXLWorksheet ws)
        {
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 40;
            ws.Column(3).Width = 16;
            ws.Column(4).Width = 52;
            ws.Column(5).Width = 16;
            ws.Column(6).Width = 14;
            ws.Column(7).Width = 16;
        }

        private static void SetRule15ColumnWidths(IXLWorksheet ws)
        {
            ws.Column(1).Width  = 14;   // Extract_Number
            ws.Column(2).Width  = 12;   // Control_Type
            ws.Column(3).Width  = 70;   // Control_Label
            ws.Column(4).Width  = 20;   // Qualification_Code
            ws.Column(5).Width  = 20;   // Course_Code
            ws.Column(6).Width  = 45;   // Qualification_Name_Designator
            ws.Column(7).Width  = 16;   // Approval_Status
            ws.Column(8).Width  = 14;   // Filler1
            ws.Column(9).Width  = 26;   // Course_Level_Credit_Value
            ws.Column(10).Width = 38;   // Completed_Research_Course_Credit_Value
            ws.Column(11).Width = 22;   // First_Student_Number
            ws.Column(12).Width = 24;   // Registered_Student_Count
            ws.Column(13).Width = 16;   // Validation_Result
            ws.Column(14).Width = 70;   // Validation_Reason
        }

        private static void SetRule16ColumnWidths(IXLWorksheet ws)
        {
            ws.Column(1).Width  = 14;   // Extract_Number
            ws.Column(2).Width  = 18;   // Student_Number
            ws.Column(3).Width  = 22;   // Qualification_Code
            ws.Column(4).Width  = 30;   // Qualification_Fulfilled_Indicator
            ws.Column(5).Width  = 26;   // Distance_Learning_Indicator
            ws.Column(6).Width  = 24;   // CREG_Qualification_Code
            ws.Column(7).Width  = 22;   // CREG_Student_Number
            ws.Column(8).Width  = 18;   // Course_Code
            ws.Column(9).Width  = 18;   // CRSE_Course_Code
            ws.Column(10).Width = 26;   // Foundation_Course_Indicator
            ws.Column(11).Width = 36;   // Course_Name
            ws.Column(12).Width = 14;   // Control_1_Result
            ws.Column(13).Width = 14;   // Control_2_Result
            ws.Column(14).Width = 14;   // Control_3_Result
            ws.Column(15).Width = 20;   // Student_Category
        }

        private static void SetRule18ColumnWidths(IXLWorksheet ws)
        {
            ws.Column(1).Width = 14;   // Extract_Number
            ws.Column(2).Width = 28;   // Control_Type
            ws.Column(3).Width = 16;   // Student_Number
            ws.Column(4).Width = 28;   // Student_Qualification_Code
            ws.Column(5).Width = 16;   // NSFAS_Status
            ws.Column(6).Width = 16;   // Attendance_Mode
            ws.Column(7).Width = 14;   // Qualification_Fulfilled_Indicator
            ws.Column(8).Width = 14;   // CREG_Qualification_Code
            ws.Column(9).Width = 16;   // CREG_Course_Code
            ws.Column(10).Width = 16;  // CRSE_Course_Code
            ws.Column(11).Width = 16;  // Foundation_Course_Indicator
            ws.Column(12).Width = 14;  // CRSE_058
            ws.Column(13).Width = 14;  // Validation_Result
        }

        private static void WriteRule24HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule23HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule20HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule22HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule25HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule24Rows(IXLWorksheet ws, int startRow, List<Rule24ReconciliationRowViewModel> rows, List<string> headers, string fillColor)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "QUAL_QualCode" => row.QualQualCode,
                        "QUAL_ApprovalStatus" => row.QualApprovalStatus,
                        "STUD_QualCode" => row.StudQualCode,
                        "AUDIT_QualCode" => row.AuditQualCode,
                        "H16_QualCode" => row.H16QualCode,
                        "Control_Type" => row.ControlType,
                        "Reconciliation_Status" => row.ReconciliationStatus,
                        "Issue_Description" => row.IssueDescription,
                        _ => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fillColor);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule23Rows(IXLWorksheet ws, int startRow, List<Rule23ReconciliationRowViewModel> rows, List<string> headers, string fillColor)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "STUD_StudentNum" => row.StudStudentNumber,
                        "STUD_QualCode" => row.StudQualificationCode,
                        "STUD_IDNum" => row.StudIdNumber,
                        "AUDIT_StudentNum" => row.AuditStudentNumber,
                        "AUDIT_QualCode" => row.AuditQualificationCode,
                        "AUDIT_IDNum" => row.AuditIdNumber,
                        "H16_StudentNum" => row.H16StudentNumber,
                        "H16_QualCode" => row.H16QualificationCode,
                        "H16_IDNum" => row.H16IdNumber,
                        "Control_Type" => row.ControlType,
                        "Result_Type" => row.ResultType,
                        "Reconciliation_Status" => row.ReconciliationStatus,
                        "Issue_Description" => row.IssueDescription,
                        _ => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fillColor);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule20Rows(IXLWorksheet ws, int startRow, List<Rule20ReviewRowViewModel> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "Part_Code" => row.PartCode,
                        "Part_Title" => row.PartTitle,
                        "Part_Description" => row.PartDescription,
                        "Student_Number_007" => row.StudentNumber007,
                        "Student_Column_008" => row.StudentColumn008,
                        "Student_Column_010" => row.StudentColumn010,
                        "Student_Column_012" => row.StudentColumn012,
                        "Student_Column_026" => row.StudentColumn026,
                        "Qualification_Code_001" => row.QualificationCode001,
                        "Foundation_Flag_106" => row.FoundationFlag106,
                        "Bridge_Qualification_Code_001" => row.BridgeQualificationCode001,
                        "Course_Code_030" => row.CourseCode030,
                        "Foundation_Course_091" => row.FoundationCourse091,
                        "Crse_Course_Code_030" => row.CrseCourseCode030,
                        "Name_019" => row.Name019,
                        "ID_Number_024" => row.IdNumber024,
                        "Qualification_Description_003" => row.QualificationDescription003,
                        "Qualification_Type_005" => row.QualificationType005,
                        "Student_Type" => row.StudentType,
                        "Notebook_Status" => row.NotebookStatus,
                        "Validation_Result" => row.ValidationResult,
                        "Validation_Explanation" => row.ValidationExplanation,
                        _ => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule20DashboardHeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule20DashboardRows(IXLWorksheet ws, int startRow, List<Rule20ReviewRowViewModel> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var value = headers[i] switch
                    {
                        "Validation_Number"                  => row.ValidationNumber.ToString(),
                        "Student_Number"                     => row.StudentNumber007,
                        "South_African_Identity_Number"      => row.StudentColumn008,
                        "Surname"                            => row.StudentColumn066,
                        "First_Name"                         => row.StudentColumn067,
                        "Middle_Name"                        => row.StudentColumn068,
                        "Date_of_Birth"                      => row.StudentColumn012,
                        "Gender"                             => row.StudentColumn013,
                        "Race"                               => row.StudentColumn014,
                        "Nationality"                        => row.StudentColumn015,
                        "Entrance_Category"                  => row.StudentColumn010,
                        "CESSM_Category"                     => row.StudentColumn026,
                        "Qualification_Requirements_Status"  => row.StudentColumn025,
                        "Qualification_Code"                 => row.QualificationCode001,
                        "Qualification_Description"          => row.QualificationDescription003,
                        "Foundation_Student_Indicator"       => row.FoundationFlag106,
                        "Course_Code"                        => row.CourseCode030,
                        "Course_Name"                        => row.CourseName058,
                        "Foundation_Course_Indicator"        => row.FoundationCourse091,
                        "Validation_Result"                  => row.ValidationResult,
                        "Audit_Rule"                         => "Rule 20 - Foundation Validation",
                        _                                    => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static List<Rule20ReviewRowViewModel> BuildRule20DashboardPreviewRows(List<Rule20ReviewRowViewModel> rows)
        {
            return (rows ?? new List<Rule20ReviewRowViewModel>()).ToList();
        }

        private static void WriteRule22Rows(IXLWorksheet ws, int startRow, List<Rule22ReviewRowViewModel> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var value = headers[i] switch
                    {
                        "Validation_Number"              => row.ValidationNumber.ToString(),
                        "Control_Type"                   => row.ControlType,
                        "Control_Definition"             => row.ControlDefinition,
                        "Control_Row_Number"             => row.SampleNumber.ToString(),
                        "Staff_Number"                   => row.StaffNumber037,
                        "Employment_Commencement_Year"   => row.Year038,
                        "Personnel_Category"             => row.Col039,
                        "Rank_of_Staff_Member"           => row.Col040,
                        "Date_of_Birth"                  => row.Col011,
                        "Gender"                         => row.Col012,
                        "Race"                           => row.Col013,
                        "Nationality"                    => row.Col014,
                        "Permanent_Temporary_Status"     => row.Col041,
                        "Full_Time_Part_Time_Status"     => row.Col042,
                        "Staff_Qualification"            => row.Col046,
                        "Joint_Appointment"              => row.Col047,
                        "On_Payroll_Code"                => row.Col048,
                        "Research_Fellow"                => row.Col094,
                        "Validation_Result"              => row.ValidationResult,
                        _                                => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule25Rows(IXLWorksheet ws, int startRow, List<Rule25ReconciliationRowViewModel> rows, List<string> headers, string fillColor)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "CRSE_CourseCode" => row.CrseCourseCode,
                        "AUDIT_CourseCode" => row.AuditCourseCode,
                        "H16_CourseCode" => row.H16CourseCode,
                        "Reconciliation_Status" => row.ReconciliationStatus,
                        "Issue_Description" => row.IssueDescription,
                        _ => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fillColor);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule27HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule27Rows(IXLWorksheet ws, int startRow, List<Rule27ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "Filter_Value" => row.FilterValue,
                        _ => row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static Rule32ValidationSummary ToRule32Summary(Rule31ValidationSummary summary) =>
            new()
            {
                Success = summary.Success,
                TotalValidated = summary.TotalValidated,
                TotalFatal = summary.TotalFatal,
                ExcludedCount = summary.ExcludedCount,
                RemainingCount = summary.RemainingCount,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                TableName = summary.TableName,
                ErrorTypeColumn = summary.ErrorTypeColumn,
                ErrorColumn = summary.ErrorColumn,
                ErrorTypeValue = summary.ErrorTypeValue,
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                Exclusions = summary.Exclusions.ToList(),
                NormalizedExclusions = summary.NormalizedExclusions.ToList(),
                ExcludedBreakdown = summary.ExcludedBreakdown
                    .Select(item => new Rule32BreakdownItemViewModel { ErrorCode = item.ErrorCode, Count = item.Count })
                    .ToList(),
                RemainingBreakdown = summary.RemainingBreakdown
                    .Select(item => new Rule32BreakdownItemViewModel { ErrorCode = item.ErrorCode, Count = item.Count })
                    .ToList(),
                ExcludedRows = summary.ExcludedRows.Select(ToRule32Row).ToList(),
                RemainingRows = summary.RemainingRows.Select(ToRule32Row).ToList(),
                Warning = summary.Warning,
                Error = summary.Error
            };

        private static Rule16ValidationSummary ToRule16Summary(Rule15ValidationSummary summary) =>
            new()
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
                ProcedureSteps = summary.ProcedureSteps?.ToList() ?? new List<string>(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = (summary.ControlSummaries ?? new List<Rule15ControlSummaryItemViewModel>())
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
                ReviewRows = (summary.ReviewRows ?? new List<Rule15ValidationRowRecord>())
                    .Select(row => new Rule16ValidationRowRecord
                    {
                        ValidationNumber = row.ValidationNumber,
                        ControlType = row.ControlType,
                        ControlLabel = row.ControlLabel,
                        ValidationResult = row.ValidationResult,
                        ValidationExplanation = row.ValidationExplanation,
                        DisplayValues = new Dictionary<string, string?>(row.DisplayValues ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase)
                    })
                    .ToList(),
                Warning = summary.Warning,
                Error = summary.Error
            };

        private static Rule32ValidationRowRecord ToRule32Row(Rule31ValidationRowRecord row) =>
            new()
            {
                ValidationNumber = row.ValidationNumber,
                ErrorTypeValue = row.ErrorTypeValue,
                ErrorCode = row.ErrorCode,
                NormalizedErrorCode = row.NormalizedErrorCode,
                Classification = row.Classification,
                ErrorMessage = row.ErrorMessage,
                Description = row.Description,
                ElementInformation = row.ElementInformation,
                DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
            };

        private static string CsvEscape(string? val)
        {
            if (string.IsNullOrEmpty(val))
                return "";
            if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
                return $"\"{val.Replace("\"", "\"\"")}\"";
            return val;
        }

        // ─── Rule 51: VALPAC Data in Production ────────────────────────────────

        private static string ResolveRule11PopulationType(
            Rule11ValidationSummary summary,
            string? populationType,
            string? qualHeqfType)
        {
            if (!string.IsNullOrWhiteSpace(populationType))
                return populationType;

            var postgraduateTypeCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var code in (summary.QualTypeCodesText ?? "").Split(new[] { ',', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = code.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    postgraduateTypeCodes.Add(trimmed.ToUpperInvariant());
            }

            var heqfType = (qualHeqfType ?? "").Trim().ToUpperInvariant();
            return postgraduateTypeCodes.Contains(heqfType) ? "Postgraduate" : "Undergraduate";
        }

        private static List<Rule51ColumnMapping> GetRule51Mappings(Rule51ValidationSummary summary)
        {
            var mappings = summary.ColumnMappings?
                .Where(m => !string.IsNullOrWhiteSpace(m.ValpacColumn) && !string.IsNullOrWhiteSpace(m.ProdColumn))
                .Select(m => new Rule51ColumnMapping
                {
                    ValpacColumn = m.ValpacColumn,
                    ProdColumn = m.ProdColumn,
                    Label = m.Label
                })
                .ToList() ?? new List<Rule51ColumnMapping>();

            if (mappings.Count > 0)
                return mappings;

            return new List<Rule51ColumnMapping>
            {
                new() { ValpacColumn = summary.ValpacCol007 ?? "_007", ProdColumn = summary.ProdColStNo ?? "IAGSTNO", Label = "Student No" },
                new() { ValpacColumn = summary.ValpacCol008 ?? "_008", ProdColumn = summary.ProdColIdNo ?? "IADIDNO", Label = "ID No" },
                new() { ValpacColumn = summary.ValpacCol001 ?? "_001", ProdColumn = summary.ProdColQual ?? "IAGQUAL", Label = "Qualification" },
                new() { ValpacColumn = summary.ValpacColYear ?? "ColYear", ProdColumn = summary.ProdColYear ?? "IAGCYR", Label = "Year" }
            };
        }

        private static string Rule51ValpacAlias(int index) => $"VALPAC_COL_{index + 1}";
        private static string Rule51ProdAlias(int index) => $"PROD_COL_{index + 1}";

        private static string Rule51MappingHeader(Rule51ColumnMapping mapping, bool useProdColumn)
        {
            var column = useProdColumn ? mapping.ProdColumn : mapping.ValpacColumn;
            return string.IsNullOrWhiteSpace(mapping.Label) || string.Equals(mapping.Label, column, StringComparison.OrdinalIgnoreCase)
                ? column
                : $"{mapping.Label} [{column}]";
        }

        public byte[] ExportRule51Excel(Rule51ValidationSummary summary)
        {
            var mappings = GetRule51Mappings(summary);
            var col049Name = summary.ValpacCol049 ?? "_049";
            var has049 = !string.IsNullOrWhiteSpace(summary.ValpacCol049);
            var headerList = new List<string> { "Validation #" };
            headerList.AddRange(mappings.Select(m => $"VALPAC {Rule51MappingHeader(m, useProdColumn: false)}"));
            if (has049) headerList.Add($"VALPAC {col049Name} (Citizen/Res.)");
            headerList.AddRange(mappings.Select(m => $"PROD {Rule51MappingHeader(m, useProdColumn: true)}"));
            headerList.Add("Result");
            headerList.Add("Exception Reason");
            var headers = headerList.ToArray();

            // Column index of _049 (1-based) — used for purple styling
            var col049ColNum = has049 ? mappings.Count + 2 : -1; // after Validation# + VALPAC cols

            using var wb = new XLWorkbook();

            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 51 VALIDATION RESULTS — VALPAC IN PRODUCTION", headers.Length);
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = headers[i]; cell.Style.Font.Bold = true;
                var isCol049Header = has049 && (i + 1) == col049ColNum;
                cell.Style.Fill.BackgroundColor = isCol049Header ? XLColor.FromHtml("#6A1B9A") : XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            int rowIndex = 3;
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                wsResults.Cell(rowIndex, 1).Value = row.ValidationNumber;
                var columnIndex = 2;
                for (int i = 0; i < mappings.Count; i++)
                    wsResults.Cell(rowIndex, columnIndex++).Value = v.TryGetValue(Rule51ValpacAlias(i), out var valpacValue) ? valpacValue ?? "" : "";
                if (has049)
                {
                    var c049 = wsResults.Cell(rowIndex, columnIndex++);
                    c049.Value = v.TryGetValue("VALPAC_049_DISP", out var v049Val) ? v049Val ?? "" : "";
                    c049.Style.Font.FontColor = XLColor.FromHtml("#6A1B9A");
                }
                for (int i = 0; i < mappings.Count; i++)
                    wsResults.Cell(rowIndex, columnIndex++).Value = v.TryGetValue(Rule51ProdAlias(i), out var prodValue) ? prodValue ?? "" : "";
                var isPassReview = string.Equals(row.ValidationResult, "PASS_REVIEW", StringComparison.OrdinalIgnoreCase);
                var isNotInCreg  = string.Equals(row.ValidationResult, "NOT_IN_CREG",  StringComparison.OrdinalIgnoreCase);
                var isCregWithdrawn = string.Equals(row.ValidationResult, "CREG_WITHDRAWN", StringComparison.OrdinalIgnoreCase);
                var resultLabel  = isPassReview ? "PASS (Review)" : isNotInCreg ? "PASS (Not in CREG)" : isCregWithdrawn ? "PASS (Withdrew)" : row.ValidationResult;
                wsResults.Cell(rowIndex, columnIndex++).Value = resultLabel;
                wsResults.Cell(rowIndex, columnIndex).Value = row.ValidationExplanation ?? "";
                var isFail = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase);
                var fill = isFail ? XLColor.FromHtml("#FFF3F3") : isPassReview ? XLColor.FromHtml("#FFF8E1") : isNotInCreg ? XLColor.FromHtml("#E1F5FE") : isCregWithdrawn ? XLColor.FromHtml("#EDE7F6") : XLColor.FromHtml("#F3FFF3");
                wsResults.Range(rowIndex, 1, rowIndex, headers.Length).Style.Fill.BackgroundColor = fill;
                if (isFail && !string.IsNullOrEmpty(row.ValidationExplanation))
                {
                    wsResults.Cell(rowIndex, columnIndex).Style.Font.FontColor = XLColor.FromHtml("#8B0000");
                    wsResults.Cell(rowIndex, columnIndex).Style.Alignment.WrapText = true;
                }
                else if (isPassReview && !string.IsNullOrEmpty(row.ValidationExplanation))
                {
                    wsResults.Cell(rowIndex, columnIndex).Style.Font.FontColor = XLColor.FromHtml("#E65100");
                    wsResults.Cell(rowIndex, columnIndex).Style.Alignment.WrapText = true;
                }
                else if (isNotInCreg && !string.IsNullOrEmpty(row.ValidationExplanation))
                {
                    wsResults.Cell(rowIndex, columnIndex).Style.Font.FontColor = XLColor.FromHtml("#01579B");
                    wsResults.Cell(rowIndex, columnIndex).Style.Alignment.WrapText = true;
                }
                else if (isCregWithdrawn && !string.IsNullOrEmpty(row.ValidationExplanation))
                {
                    wsResults.Cell(rowIndex, columnIndex).Style.Font.FontColor = XLColor.FromHtml("#4527A0");
                    wsResults.Cell(rowIndex, columnIndex).Style.Alignment.WrapText = true;
                }
                rowIndex++;
            }
            for (int c = 1; c < headers.Length; c++) wsResults.Column(c).AdjustToContents();
            wsResults.Column(headers.Length).Width = 60;

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 51: VALPAC DATA IN PRODUCTION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database), ("VALPAC Table", summary.ValpacTable), ("PRODUCTION Table", summary.ProdTable),
                ("Column Mapping", summary.TableLinkageText), ("Validation Date", summary.Timestamp), ("", ""),
                ("VALIDATION RESULTS", ""), ("Total Tested", summary.TotalValidated.ToString("N0")),
                ("PASS (found in Production)", (summary.PassCount - summary.PassWithReviewCount - summary.NotInCregCount - summary.CregWithdrawnCount).ToString("N0")),
                ("Pass With Review Note (not an exception)", summary.PassWithReviewCount > 0 ? summary.PassWithReviewCount.ToString("N0") : "0"),
                ("Not in CREG (No Service)", summary.NotInCregCount > 0 ? summary.NotInCregCount.ToString("N0") : "0"),
                ($"Withdrew (CREG {summary.CregCompletionStatusCol ?? "_032"})", summary.CregWithdrawnCount > 0 ? summary.CregWithdrawnCount.ToString("N0") : "0"),
                ("FAIL (not in Production)", summary.FailCount.ToString("N0")),
                ("Foreign National Exempt", summary.ForeignNationalExemptCount > 0 ? summary.ForeignNationalExemptCount.ToString("N0") : "0"),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"), ("Status", summary.Status)
            };
            int sRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                { var hc = wsSummary.Cell(sRow, 1); hc.Value = label; hc.Style.Font.Bold = true; hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000"); hc.Style.Font.FontColor = XLColor.White; wsSummary.Range(sRow, 1, sRow, 2).Merge(); }
                else if (label != "")
                {
                    wsSummary.Cell(sRow, 1).Value = label; wsSummary.Cell(sRow, 1).Style.Font.Bold = true; wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sRow, 2).Value = value;
                    if (label == "Status") { wsSummary.Cell(sRow, 2).Style.Fill.BackgroundColor = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2"); wsSummary.Cell(sRow, 2).Style.Font.Bold = true; }
                    else if (label.StartsWith("Pass With Review")) { wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3E0"); wsSummary.Cell(sRow, 1).Style.Font.FontColor = XLColor.FromHtml("#E65100"); wsSummary.Cell(sRow, 2).Style.Font.FontColor = XLColor.FromHtml("#E65100"); wsSummary.Cell(sRow, 2).Style.Font.Bold = true; }
                    else if (label.StartsWith("Not in CREG")) { wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E1F5FE"); wsSummary.Cell(sRow, 1).Style.Font.FontColor = XLColor.FromHtml("#01579B"); wsSummary.Cell(sRow, 2).Style.Font.FontColor = XLColor.FromHtml("#01579B"); wsSummary.Cell(sRow, 2).Style.Font.Bold = true; }
                    else if (label.StartsWith("Withdrew")) { wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#EDE7F6"); wsSummary.Cell(sRow, 1).Style.Font.FontColor = XLColor.FromHtml("#4527A0"); wsSummary.Cell(sRow, 2).Style.Font.FontColor = XLColor.FromHtml("#4527A0"); wsSummary.Cell(sRow, 2).Style.Font.Bold = true; }
                }
                sRow++;
            }
            wsSummary.Column(1).Width = 32; wsSummary.Column(2).Width = 70;

            // Exception breakdown sheet — shows why each category of exception occurred
            if (summary.ExceptionCategories.Count > 0)
            {
                var wsBreak = wb.Worksheets.Add("Exception Breakdown");
                StyleHeaderRow(wsBreak, 1, "RULE 51 EXCEPTION BREAKDOWN — CATEGORIES", 3);
                var brkHeaders = new[] { "Exception Category", "Description", "Count" };
                for (int i = 0; i < brkHeaders.Length; i++)
                {
                    var c = wsBreak.Cell(2, i + 1);
                    c.Value = brkHeaders[i]; c.Style.Font.Bold = true;
                    c.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    c.Style.Font.FontColor = XLColor.White;
                }
                int bRow = 3;
                foreach (var cat in summary.ExceptionCategories)
                {
                    wsBreak.Cell(bRow, 1).Value = cat.Category;
                    wsBreak.Cell(bRow, 2).Value = cat.Description;
                    var countCell = wsBreak.Cell(bRow, 3);
                    countCell.Value = cat.Count;
                    countCell.Style.Font.Bold = true;
                    countCell.Style.Font.FontColor = XLColor.FromHtml("#8B0000");
                    wsBreak.Range(bRow, 1, bRow, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                    bRow++;
                }
                wsBreak.Column(1).Width = 30; wsBreak.Column(2).Width = 55; wsBreak.Column(3).Width = 12;
            }

            var failRows = summary.ReviewRows.Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();
            if (failRows.Any())
            {
                var wsEx = wb.Worksheets.Add("Exceptions");
                StyleHeaderRow(wsEx, 1, "RULE 51 EXCEPTIONS — VALPAC NOT IN PRODUCTION", headers.Length);
                for (int i = 0; i < headers.Length; i++)
                {
                    var c = wsEx.Cell(2, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true;
                    var isCol049Header = has049 && (i + 1) == col049ColNum;
                    c.Style.Fill.BackgroundColor = isCol049Header ? XLColor.FromHtml("#6A1B9A") : XLColor.FromHtml("#8B0000");
                    c.Style.Font.FontColor = XLColor.White;
                }
                int exRow = 3;
                foreach (var row in failRows)
                {
                    var v = row.DisplayValues;
                    wsEx.Cell(exRow, 1).Value = row.ValidationNumber;
                    var columnIndex = 2;
                    for (int i = 0; i < mappings.Count; i++)
                        wsEx.Cell(exRow, columnIndex++).Value = v.TryGetValue(Rule51ValpacAlias(i), out var valpacValue) ? valpacValue ?? "" : "";
                    if (has049)
                    {
                        var c049 = wsEx.Cell(exRow, columnIndex++);
                        c049.Value = v.TryGetValue("VALPAC_049_DISP", out var v049Val) ? v049Val ?? "" : "";
                        c049.Style.Font.FontColor = XLColor.FromHtml("#6A1B9A");
                    }
                    for (int i = 0; i < mappings.Count; i++)
                        wsEx.Cell(exRow, columnIndex++).Value = v.TryGetValue(Rule51ProdAlias(i), out var prodValue) ? prodValue ?? "" : "";
                    wsEx.Cell(exRow, columnIndex++).Value = "FAIL";
                    wsEx.Cell(exRow, columnIndex).Value = row.ValidationExplanation ?? "";
                    wsEx.Cell(exRow, columnIndex).Style.Font.FontColor = XLColor.FromHtml("#8B0000");
                    wsEx.Cell(exRow, columnIndex).Style.Alignment.WrapText = true;
                    wsEx.Range(exRow, 1, exRow, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                    exRow++;
                }
                for (int c = 1; c < headers.Length; c++) wsEx.Column(c).AdjustToContents();
                wsEx.Column(headers.Length).Width = 60;
            }

            var passReviewRows = summary.ReviewRows.Where(r => string.Equals(r.ValidationResult, "PASS_REVIEW", StringComparison.OrdinalIgnoreCase)).ToList();
            if (passReviewRows.Any())
            {
                var wsRev = wb.Worksheets.Add("Pass With Review");
                StyleHeaderRow(wsRev, 1, "RULE 51 — PASS WITH REVIEW NOTE (primary qualification matched, not an exception)", headers.Length);
                for (int i = 0; i < headers.Length; i++)
                {
                    var c = wsRev.Cell(2, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true;
                    var isCol049Header = has049 && (i + 1) == col049ColNum;
                    c.Style.Fill.BackgroundColor = isCol049Header ? XLColor.FromHtml("#6A1B9A") : XLColor.FromHtml("#E65100");
                    c.Style.Font.FontColor = XLColor.White;
                }
                int revRow = 3;
                foreach (var row in passReviewRows)
                {
                    var v = row.DisplayValues;
                    wsRev.Cell(revRow, 1).Value = row.ValidationNumber;
                    var columnIndex = 2;
                    for (int i = 0; i < mappings.Count; i++)
                        wsRev.Cell(revRow, columnIndex++).Value = v.TryGetValue(Rule51ValpacAlias(i), out var valpacValue) ? valpacValue ?? "" : "";
                    if (has049)
                    {
                        var c049 = wsRev.Cell(revRow, columnIndex++);
                        c049.Value = v.TryGetValue("VALPAC_049_DISP", out var v049Val) ? v049Val ?? "" : "";
                        c049.Style.Font.FontColor = XLColor.FromHtml("#6A1B9A");
                    }
                    for (int i = 0; i < mappings.Count; i++)
                        wsRev.Cell(revRow, columnIndex++).Value = v.TryGetValue(Rule51ProdAlias(i), out var prodValue) ? prodValue ?? "" : "";
                    wsRev.Cell(revRow, columnIndex++).Value = "PASS (Review)";
                    var noteCell = wsRev.Cell(revRow, columnIndex);
                    noteCell.Value = row.ValidationExplanation ?? "";
                    noteCell.Style.Font.FontColor = XLColor.FromHtml("#E65100");
                    noteCell.Style.Alignment.WrapText = true;
                    wsRev.Range(revRow, 1, revRow, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8E1");
                    revRow++;
                }
                for (int c = 1; c < headers.Length; c++) wsRev.Column(c).AdjustToContents();
                wsRev.Column(headers.Length).Width = 60;
            }

            var notInCregRows = summary.ReviewRows.Where(r => string.Equals(r.ValidationResult, "NOT_IN_CREG", StringComparison.OrdinalIgnoreCase)).ToList();
            if (notInCregRows.Any())
            {
                var wsCreg = wb.Worksheets.Add("Not in CREG");
                StyleHeaderRow(wsCreg, 1, "RULE 51 — NOT IN CREG (university has no service record for student — not an exception)", headers.Length);
                for (int i = 0; i < headers.Length; i++)
                {
                    var c = wsCreg.Cell(2, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true;
                    var isCol049Header = has049 && (i + 1) == col049ColNum;
                    c.Style.Fill.BackgroundColor = isCol049Header ? XLColor.FromHtml("#6A1B9A") : XLColor.FromHtml("#0288D1");
                    c.Style.Font.FontColor = XLColor.White;
                }
                int cregRow = 3;
                foreach (var row in notInCregRows)
                {
                    var v = row.DisplayValues;
                    wsCreg.Cell(cregRow, 1).Value = row.ValidationNumber;
                    var columnIndex = 2;
                    for (int i = 0; i < mappings.Count; i++)
                        wsCreg.Cell(cregRow, columnIndex++).Value = v.TryGetValue(Rule51ValpacAlias(i), out var valpacValue) ? valpacValue ?? "" : "";
                    if (has049)
                    {
                        var c049 = wsCreg.Cell(cregRow, columnIndex++);
                        c049.Value = v.TryGetValue("VALPAC_049_DISP", out var v049Val) ? v049Val ?? "" : "";
                        c049.Style.Font.FontColor = XLColor.FromHtml("#6A1B9A");
                    }
                    for (int i = 0; i < mappings.Count; i++)
                        wsCreg.Cell(cregRow, columnIndex++).Value = v.TryGetValue(Rule51ProdAlias(i), out var prodValue) ? prodValue ?? "" : "";
                    wsCreg.Cell(cregRow, columnIndex++).Value = "PASS (Not in CREG)";
                    var noteCell = wsCreg.Cell(cregRow, columnIndex);
                    noteCell.Value = row.ValidationExplanation ?? "";
                    noteCell.Style.Font.FontColor = XLColor.FromHtml("#01579B");
                    noteCell.Style.Alignment.WrapText = true;
                    wsCreg.Range(cregRow, 1, cregRow, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#E1F5FE");
                    cregRow++;
                }
                for (int c = 1; c < headers.Length; c++) wsCreg.Column(c).AdjustToContents();
                wsCreg.Column(headers.Length).Width = 60;
            }

            var cregWithdrawnRows = summary.ReviewRows.Where(r => string.Equals(r.ValidationResult, "CREG_WITHDRAWN", StringComparison.OrdinalIgnoreCase)).ToList();
            if (cregWithdrawnRows.Any())
            {
                var wsWd = wb.Worksheets.Add("Withdrew (CREG)");
                StyleHeaderRow(wsWd, 1, "RULE 51 — WITHDREW (CREG completion status indicates withdrawal — not an exception)", headers.Length);
                for (int i = 0; i < headers.Length; i++)
                {
                    var c = wsWd.Cell(2, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true;
                    var isCol049Header = has049 && (i + 1) == col049ColNum;
                    c.Style.Fill.BackgroundColor = isCol049Header ? XLColor.FromHtml("#6A1B9A") : XLColor.FromHtml("#5E35B1");
                    c.Style.Font.FontColor = XLColor.White;
                }
                int wdRow = 3;
                foreach (var row in cregWithdrawnRows)
                {
                    var v = row.DisplayValues;
                    wsWd.Cell(wdRow, 1).Value = row.ValidationNumber;
                    var columnIndex = 2;
                    for (int i = 0; i < mappings.Count; i++)
                        wsWd.Cell(wdRow, columnIndex++).Value = v.TryGetValue(Rule51ValpacAlias(i), out var valpacValue) ? valpacValue ?? "" : "";
                    if (has049)
                    {
                        var c049 = wsWd.Cell(wdRow, columnIndex++);
                        c049.Value = v.TryGetValue("VALPAC_049_DISP", out var v049Val) ? v049Val ?? "" : "";
                        c049.Style.Font.FontColor = XLColor.FromHtml("#6A1B9A");
                    }
                    for (int i = 0; i < mappings.Count; i++)
                        wsWd.Cell(wdRow, columnIndex++).Value = v.TryGetValue(Rule51ProdAlias(i), out var prodValue) ? prodValue ?? "" : "";
                    wsWd.Cell(wdRow, columnIndex++).Value = "PASS (Withdrew)";
                    var noteCell = wsWd.Cell(wdRow, columnIndex);
                    noteCell.Value = row.ValidationExplanation ?? "";
                    noteCell.Style.Font.FontColor = XLColor.FromHtml("#4527A0");
                    noteCell.Style.Alignment.WrapText = true;
                    wsWd.Range(wdRow, 1, wdRow, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#EDE7F6");
                    wdRow++;
                }
                for (int c = 1; c < headers.Length; c++) wsWd.Column(c).AdjustToContents();
                wsWd.Column(headers.Length).Width = 60;
            }

            var purePassRows = summary.ReviewRows.Where(r => string.Equals(r.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase)).ToList();
            if (purePassRows.Any())
            {
                var wsPass = wb.Worksheets.Add("Pass");
                StyleHeaderRow(wsPass, 1, "RULE 51 — PASS (Students found in PRODUCTION on primary qualification)", headers.Length);
                for (int i = 0; i < headers.Length; i++)
                {
                    var c = wsPass.Cell(2, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true;
                    var isCol049Header = has049 && (i + 1) == col049ColNum;
                    c.Style.Fill.BackgroundColor = isCol049Header ? XLColor.FromHtml("#6A1B9A") : XLColor.FromHtml("#2E7D32");
                    c.Style.Font.FontColor = XLColor.White;
                }
                int passRow = 3;
                foreach (var row in purePassRows)
                {
                    var v = row.DisplayValues;
                    wsPass.Cell(passRow, 1).Value = row.ValidationNumber;
                    var columnIndex = 2;
                    for (int i = 0; i < mappings.Count; i++)
                        wsPass.Cell(passRow, columnIndex++).Value = v.TryGetValue(Rule51ValpacAlias(i), out var valpacValue) ? valpacValue ?? "" : "";
                    if (has049)
                    {
                        var c049 = wsPass.Cell(passRow, columnIndex++);
                        c049.Value = v.TryGetValue("VALPAC_049_DISP", out var v049Val) ? v049Val ?? "" : "";
                        c049.Style.Font.FontColor = XLColor.FromHtml("#6A1B9A");
                    }
                    for (int i = 0; i < mappings.Count; i++)
                        wsPass.Cell(passRow, columnIndex++).Value = v.TryGetValue(Rule51ProdAlias(i), out var prodValue) ? prodValue ?? "" : "";
                    wsPass.Cell(passRow, columnIndex++).Value = "PASS";
                    wsPass.Cell(passRow, columnIndex).Value = row.ValidationExplanation ?? "";
                    wsPass.Range(passRow, 1, passRow, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F3FFF3");
                    passRow++;
                }
                for (int c = 1; c < headers.Length; c++) wsPass.Column(c).AdjustToContents();
                wsPass.Column(headers.Length).Width = 60;
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule51Csv(Rule51ValidationSummary summary)
        {
            var mappings = GetRule51Mappings(summary);
            var col049Name = summary.ValpacCol049 ?? "_049";
            var has049Csv = !string.IsNullOrWhiteSpace(summary.ValpacCol049);
            var headers = new List<string> { "Validation_Number" };
            headers.AddRange(mappings.Select(m => $"VALPAC_{Rule51MappingHeader(m, useProdColumn: false)}"));
            if (has049Csv) headers.Add($"VALPAC_{col049Name}_CitizenRes");
            headers.AddRange(mappings.Select(m => $"PROD_{Rule51MappingHeader(m, useProdColumn: true)}"));
            headers.Add("Validation_Result");
            headers.Add("Exception_Reason");

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                var cells = new List<string> { CsvEscape(row.ValidationNumber.ToString()) };
                for (int i = 0; i < mappings.Count; i++)
                    cells.Add(CsvEscape(v.TryGetValue(Rule51ValpacAlias(i), out var valpacValue) ? valpacValue : ""));
                if (has049Csv)
                    cells.Add(CsvEscape(v.TryGetValue("VALPAC_049_DISP", out var v049Csv) ? v049Csv : ""));
                for (int i = 0; i < mappings.Count; i++)
                    cells.Add(CsvEscape(v.TryGetValue(Rule51ProdAlias(i), out var prodValue) ? prodValue : ""));
                var csvResult = string.Equals(row.ValidationResult, "PASS_REVIEW", StringComparison.OrdinalIgnoreCase) ? "PASS (Review)"
                             : string.Equals(row.ValidationResult, "NOT_IN_CREG",  StringComparison.OrdinalIgnoreCase) ? "PASS (Not in CREG)"
                             : string.Equals(row.ValidationResult, "CREG_WITHDRAWN", StringComparison.OrdinalIgnoreCase) ? "PASS (Withdrew)"
                             : row.ValidationResult;
                cells.Add(CsvEscape(csvResult));
                cells.Add(CsvEscape(row.ValidationExplanation ?? ""));
                sb.AppendLine(string.Join(",", cells));
            }
            return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 52: QUAL VALPAC Data in Production (same structure as Rule 51) ──

        public byte[] ExportRule52Excel(Rule52ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var wsResults = wb.Worksheets.Add("Validation Results");
            var vSubj = summary.ValpacSubjCol ?? "_001";
            var pSubj = summary.ProdSubjCol ?? "IAIQUAL";
            var aCol  = summary.ApprovalStatusCol ?? "_004";
            StyleHeaderRow(wsResults, 1, $"RULE 52: {summary.ValpacTable ?? "dbo_QUAL"} [{vSubj}] IN {summary.ProdTable ?? "MT-audit-prod-QUAL"} [{pSubj}]", 5);
            var headers = new[] { "Validation #", $"{summary.ValpacTable} [{vSubj}]", $"Approval Status [{aCol}]", $"{summary.ProdTable} [{pSubj}]", "Result" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = headers[i]; cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int rowIndex = 3;
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                wsResults.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsResults.Cell(rowIndex, 2).Value = v.TryGetValue("VALPAC_SUBJ", out var vs) ? vs ?? "" : "";
                wsResults.Cell(rowIndex, 3).Value = v.TryGetValue("VALPAC_APPROVAL_STATUS", out var aps) ? aps ?? "" : "";
                wsResults.Cell(rowIndex, 4).Value = v.TryGetValue("PROD_SUBJ",   out var ps) ? ps ?? "" : "";
                wsResults.Cell(rowIndex, 5).Value = row.ValidationResult;
                wsResults.Range(rowIndex, 1, rowIndex, 5).Style.Fill.BackgroundColor =
                    string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase) ? XLColor.FromHtml("#FFF3F3") : XLColor.FromHtml("#F3FFF3");
                rowIndex++;
            }
            for (int c = 1; c <= 5; c++) wsResults.Column(c).AdjustToContents();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 52: dbo_QUAL QUALIFICATION CODE IN MT-audit-prod-QUAL", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database), ("Source Table (dbo_QUAL)", summary.ValpacTable), ("Target Table (MT-audit-prod-QUAL)", summary.ProdTable),
                ("Column Mapping", summary.TableLinkageText),
                ("Approval Status Column", summary.ApprovalStatusCol ?? "_004"),
                ("Approval Status Filter", string.IsNullOrWhiteSpace(summary.ApprovalStatusValues) ? "ALL — no filter applied" : summary.ApprovalStatusValues),
                ("Validation Date", summary.Timestamp), ("", ""),
                ("VALIDATION RESULTS", ""), ("Total Tested", summary.TotalValidated.ToString("N0")),
                ("PASS (found in PRODUCTION)", summary.PassCount.ToString("N0")), ("FAIL (not found in PRODUCTION)", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"), ("Status", summary.Status)
            };
            int sRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                { var hc = wsSummary.Cell(sRow, 1); hc.Value = label; hc.Style.Font.Bold = true; hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000"); hc.Style.Font.FontColor = XLColor.White; wsSummary.Range(sRow, 1, sRow, 2).Merge(); }
                else if (label != "")
                {
                    wsSummary.Cell(sRow, 1).Value = label; wsSummary.Cell(sRow, 1).Style.Font.Bold = true; wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sRow, 2).Value = value;
                    if (label == "Status") { wsSummary.Cell(sRow, 2).Style.Fill.BackgroundColor = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2"); wsSummary.Cell(sRow, 2).Style.Font.Bold = true; }
                }
                sRow++;
            }
            wsSummary.Column(1).Width = 36; wsSummary.Column(2).Width = 70;
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule52Csv(Rule52ValidationSummary summary)
        {
            var vSubj = summary.ValpacSubjCol ?? "_001";
            var pSubj = summary.ProdSubjCol ?? "IAIQUAL";
            var aCol  = summary.ApprovalStatusCol ?? "_004";
            var sb = new StringBuilder();
            sb.AppendLine($"Validation_Number,VALPAC_{vSubj},Approval_Status_{aCol},PROD_{pSubj},Validation_Result");
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvEscape(row.ValidationNumber.ToString()),
                    CsvEscape(v.TryGetValue("VALPAC_SUBJ", out var a) ? a : ""),
                    CsvEscape(v.TryGetValue("VALPAC_APPROVAL_STATUS", out var s) ? s : ""),
                    CsvEscape(v.TryGetValue("PROD_SUBJ",   out var b) ? b : ""),
                    CsvEscape(row.ValidationResult)
                }));
            }
            return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 53: CRSE VALPAC Data in Production ──────────────────────────

        public byte[] ExportRule53Excel(Rule53ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var wsResults = wb.Worksheets.Add("Validation Results");
            var vSubj = summary.ValpacSubjCol ?? "_030";
            var pSubj = summary.ProdSubjCol ?? "IALSUBJ";
            StyleHeaderRow(wsResults, 1, $"RULE 53: {summary.ValpacTable ?? "dbo_CRSE"} [{vSubj}] IN {summary.ProdTable ?? "MT-audit-prod-CRSE"} [{pSubj}]", 3);
            var headers = new[] { "Validation #", $"{summary.ValpacTable} [{vSubj}]", $"{summary.ProdTable} [{pSubj}]", "Result" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = headers[i]; cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B5E20");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int rowIndex = 3;
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                wsResults.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsResults.Cell(rowIndex, 2).Value = v.TryGetValue("VALPAC_SUBJ", out var vs) ? vs ?? "" : "";
                wsResults.Cell(rowIndex, 3).Value = v.TryGetValue("PROD_SUBJ",   out var ps) ? ps ?? "" : "";
                wsResults.Cell(rowIndex, 4).Value = row.ValidationResult;
                wsResults.Range(rowIndex, 1, rowIndex, 4).Style.Fill.BackgroundColor =
                    string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase) ? XLColor.FromHtml("#FFF3F3") : XLColor.FromHtml("#F3FFF3");
                rowIndex++;
            }
            for (int c = 1; c <= 4; c++) wsResults.Column(c).AdjustToContents();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 53: dbo_CRSE SUBJECT CODE IN MT-audit-prod-CRSE", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database), ("Source Table (dbo_CRSE)", summary.ValpacTable), ("Target Table (MT-audit-prod-CRSE)", summary.ProdTable),
                ("Column Mapping", summary.TableLinkageText), ("Validation Date", summary.Timestamp), ("", ""),
                ("VALIDATION RESULTS", ""), ("Total Tested", summary.TotalValidated.ToString("N0")),
                ("PASS (found in PRODUCTION)", summary.PassCount.ToString("N0")), ("FAIL (not found in PRODUCTION)", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"), ("Status", summary.Status)
            };
            int sRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                { var hc = wsSummary.Cell(sRow, 1); hc.Value = label; hc.Style.Font.Bold = true; hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B5E20"); hc.Style.Font.FontColor = XLColor.White; wsSummary.Range(sRow, 1, sRow, 2).Merge(); }
                else if (label != "")
                {
                    wsSummary.Cell(sRow, 1).Value = label; wsSummary.Cell(sRow, 1).Style.Font.Bold = true; wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sRow, 2).Value = value;
                    if (label == "Status") { wsSummary.Cell(sRow, 2).Style.Fill.BackgroundColor = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2"); wsSummary.Cell(sRow, 2).Style.Font.Bold = true; }
                }
                sRow++;
            }
            wsSummary.Column(1).Width = 36; wsSummary.Column(2).Width = 70;
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule53Csv(Rule53ValidationSummary summary)
        {
            var vSubj = summary.ValpacSubjCol ?? "_030";
            var pSubj = summary.ProdSubjCol ?? "IALSUBJ";
            var sb = new StringBuilder();
            sb.AppendLine($"Validation_Number,VALPAC_{vSubj},PROD_{pSubj},Validation_Result");
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvEscape(row.ValidationNumber.ToString()),
                    CsvEscape(v.TryGetValue("VALPAC_SUBJ", out var a) ? a : ""),
                    CsvEscape(v.TryGetValue("PROD_SUBJ",   out var b) ? b : ""),
                    CsvEscape(row.ValidationResult)
                }));
            }
            return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 66: NSFAS Students in CREG ──────────────────────────────────

        public byte[] ExportRule66Excel(Rule66ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var fundCol = summary.FundingSourceCol ?? "_019";
            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, $"RULE 66: NSFAS Students [{fundCol}] in CREG [{summary.CregStudentNoCol ?? "_007"}]", 4);
            var headers = new[] { "Validation #", $"STUD [{summary.StudStudentNoCol ?? "_007"}] Student No", $"Funding Source [{fundCol}]", $"CREG [{summary.CregStudentNoCol ?? "_007"}]", "Result" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = headers[i]; cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int rowIndex = 3;
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                wsResults.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsResults.Cell(rowIndex, 2).Value = v.TryGetValue("STUD_NO",       out var sn)  ? sn  ?? "" : "";
                wsResults.Cell(rowIndex, 3).Value = v.TryGetValue("FUNDING_SOURCE", out var fs)  ? fs  ?? "" : "";
                wsResults.Cell(rowIndex, 4).Value = v.TryGetValue("CREG_STUD_NO",  out var cn)  ? cn  ?? "" : "";
                wsResults.Cell(rowIndex, 5).Value = row.ValidationResult;
                wsResults.Range(rowIndex, 1, rowIndex, 5).Style.Fill.BackgroundColor =
                    string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase) ? XLColor.FromHtml("#FFF3F3") : XLColor.FromHtml("#F3FFF3");
                rowIndex++;
            }
            for (int c = 1; c <= 5; c++) wsResults.Column(c).AdjustToContents();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 66: NSFAS STUDENTS IN CREG", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("STUD Table", summary.StudTable),
                ("CREG Table", summary.CregTable),
                ("Funding Source Column", fundCol),
                ("Funding Source Filter", string.IsNullOrWhiteSpace(summary.FundingSourceValues) ? "ALL — no filter applied" : summary.FundingSourceValues),
                ("Validation Date", summary.Timestamp), ("", ""),
                ("VALIDATION RESULTS", ""),
                ("STUD Total Records", summary.StudRecordCount.ToString("N0")),
                ("NSFAS Students (funding filter)", summary.StudNsfasCount.ToString("N0")),
                ("CREG Total Records", summary.CregRecordCount.ToString("N0")),
                ("Found in CREG (PASS)", summary.PassCount.ToString("N0")),
                ("Missing from CREG (FAIL)", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };
            int sRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                { var hc = wsSummary.Cell(sRow, 1); hc.Value = label; hc.Style.Font.Bold = true; hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000"); hc.Style.Font.FontColor = XLColor.White; wsSummary.Range(sRow, 1, sRow, 2).Merge(); }
                else if (label != "")
                {
                    wsSummary.Cell(sRow, 1).Value = label; wsSummary.Cell(sRow, 1).Style.Font.Bold = true; wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sRow, 2).Value = value;
                    if (label == "Status") { wsSummary.Cell(sRow, 2).Style.Fill.BackgroundColor = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2"); wsSummary.Cell(sRow, 2).Style.Font.Bold = true; }
                }
                sRow++;
            }
            wsSummary.Column(1).Width = 40; wsSummary.Column(2).Width = 70;
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule66Csv(Rule66ValidationSummary summary)
        {
            var fundCol = summary.FundingSourceCol ?? "_019";
            var sb = new StringBuilder();
            sb.AppendLine($"Validation_Number,STUD_NO,Funding_Source_{fundCol},CREG_STUD_NO,Validation_Result");
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvEscape(row.ValidationNumber.ToString()),
                    CsvEscape(v.TryGetValue("STUD_NO",        out var sn) ? sn : ""),
                    CsvEscape(v.TryGetValue("FUNDING_SOURCE",  out var fs) ? fs : ""),
                    CsvEscape(v.TryGetValue("CREG_STUD_NO",   out var cn) ? cn : ""),
                    CsvEscape(row.ValidationResult)
                }));
            }
            return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 67: CREG-STUD Pair Validation ────────────────────────────────

        public byte[] ExportRule67Excel(Rule67ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var e051Col    = summary.CregE051Col ?? "_051";
            var e051Filter = string.IsNullOrWhiteSpace(summary.E051FilterValues) ? "ALL — no filter applied" : summary.E051FilterValues;
            var titleText  = $"RULE 67: CREG [{summary.CregStudentNoCol ?? "_007"}]+[{summary.CregQualCol ?? "_001"}] Pair in STUD | [{e051Col}] Filter: {e051Filter}";
            var hasDetail  = !string.IsNullOrWhiteSpace(summary.DetailTable);
            var headers    = new[] { "Validation #", "CREG [_007] Student No", "CREG [_001] Qual", $"CREG [{e051Col}] E051", "STUD [_007]", "STUD [_001] Qual", "In STUD", "Ghost Student", "Note", "E051 Valid", "Result", "Exception Code", "Reconciliation" };
            var colCount   = hasDetail ? 13 : 12;

            void WriteRule67Sheet(IXLWorksheet ws, IEnumerable<Rule67ValidationRowRecord> sheetRows, string headerBg)
            {
                StyleHeaderRow(ws, 1, titleText, colCount);
                for (int i = 0; i < colCount; i++)
                {
                    var hc = ws.Cell(2, i + 1);
                    hc.Value = headers[i]; hc.Style.Font.Bold = true;
                    hc.Style.Fill.BackgroundColor = XLColor.FromHtml(headerBg);
                    hc.Style.Font.FontColor = XLColor.White;
                }
                int r = 3;
                foreach (var row in sheetRows)
                {
                    var v = row.DisplayValues;
                    string DV(string k) => v.TryGetValue(k, out var val) ? val ?? "" : "";
                    var isFail  = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase);
                    var recon   = DV("Reconciliation_Status");
                    ws.Cell(r, 1).Value = row.ValidationNumber;
                    ws.Cell(r, 2).Value = DV("CREG_STUD_NO");
                    ws.Cell(r, 3).Value = DV("CREG_QUAL");
                    ws.Cell(r, 4).Value = DV("CREG_E051");
                    ws.Cell(r, 5).Value = DV("STUD_NO");
                    ws.Cell(r, 6).Value = DV("STUD_QUAL");
                    ws.Cell(r, 7).Value = DV("IN_STUD");
                    ws.Cell(r, 8).Value = DV("GHOST_STUDENT");
                    ws.Cell(r, 9).Value = DV("GHOST_STUDENT_NOTE");
                    ws.Cell(r, 10).Value = DV("E051_VALID");
                    ws.Cell(r, 11).Value = row.ValidationResult;
                    ws.Cell(r, 12).Value = isFail ? row.ExceptionCode : "";
                    if (hasDetail)
                    {
                        ws.Cell(r, 13).Value = recon;
                        if (recon == "Confirmed by Rule 29")
                            ws.Cell(r, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");
                        else if (recon == "Not in Rule 29")
                            ws.Cell(r, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8E1");
                    }
                    ws.Range(r, 1, r, colCount).Style.Fill.BackgroundColor = isFail ? XLColor.FromHtml("#FFF3F3") : XLColor.FromHtml("#F3FFF3");
                    if (string.Equals(DV("GHOST_STUDENT"), "Yes", StringComparison.OrdinalIgnoreCase))
                        ws.Cell(r, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDECEC");
                    if (hasDetail && recon == "Confirmed by Rule 29")
                        ws.Cell(r, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");
                    else if (hasDetail && recon == "Not in Rule 29")
                        ws.Cell(r, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8E1");
                    r++;
                }
                for (int c = 1; c <= colCount; c++) ws.Column(c).AdjustToContents();
            }

            static string GetFailReason(Rule67ValidationRowRecord row)
            {
                row.DisplayValues.TryGetValue("FailReason", out var fr);
                return fr ?? "";
            }
            static string GetRecon(Rule67ValidationRowRecord row)
            {
                row.DisplayValues.TryGetValue("Reconciliation_Status", out var rc);
                return rc ?? "";
            }
            static string GetE051Valid(Rule67ValidationRowRecord row)
            {
                row.DisplayValues.TryGetValue("E051_VALID", out var ev);
                return ev ?? "";
            }

            var allRows             = summary.ReviewRows;
            var passRows            = allRows.Where(r => string.Equals(r.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase)).ToList();
            var notInStudRows       = allRows.Where(r => string.Equals(GetFailReason(r), "Not found in STUD", StringComparison.OrdinalIgnoreCase)).ToList();
            var notInStudE051YesRows = notInStudRows.Where(r => string.Equals(GetE051Valid(r), "Yes", StringComparison.OrdinalIgnoreCase)).ToList();
            var notInStudE051NoRows  = notInStudRows.Where(r => string.Equals(GetE051Valid(r), "No", StringComparison.OrdinalIgnoreCase)).ToList();
            var e051InvalidRows     = allRows.Where(r => string.Equals(GetFailReason(r), "E051 code not in expected values", StringComparison.OrdinalIgnoreCase)).ToList();
            var confirmedRows       = hasDetail ? allRows.Where(r => string.Equals(GetRecon(r), "Confirmed by Rule 29", StringComparison.OrdinalIgnoreCase)).ToList() : null;
            var notConfirmedRows    = hasDetail ? allRows.Where(r => string.Equals(GetRecon(r), "Not in Rule 29", StringComparison.OrdinalIgnoreCase)).ToList() : null;

            WriteRule67Sheet(wb.Worksheets.Add("All Results"),         allRows,         "#1A237E");
            WriteRule67Sheet(wb.Worksheets.Add("PASS"),                passRows,        "#1B5E20");
            WriteRule67Sheet(wb.Worksheets.Add("Fail"),              notInStudRows,        "#B71C1C");
            WriteRule67Sheet(wb.Worksheets.Add("Fail (E051 Yes)"),   notInStudE051YesRows, "#C62828");
            WriteRule67Sheet(wb.Worksheets.Add("Fail (E051 No)"),    notInStudE051NoRows,  "#AD1457");
            WriteRule67Sheet(wb.Worksheets.Add("FAIL - E051 Invalid"), e051InvalidRows, "#E65100");

            if (hasDetail)
            {
                WriteRule67Sheet(wb.Worksheets.Add("Confirmed by Rule 29"),   confirmedRows!,    "#2E7D32");
                WriteRule67Sheet(wb.Worksheets.Add("Not Confirmed (R67 Only)"), notConfirmedRows!, "#F57F17");

                // Rule 29 Only sheet — ALL detail pairs with ConfirmedByR67 column
                var wsR29 = wb.Worksheets.Add("Rule 29 Only (00708)");
                StyleHeaderRow(wsR29, 1, $"RULE 29 DETAIL (error {summary.DetailErrorCode}): All records from [{summary.DetailTable}] — with Rule 67 confirmation status", 4);
                var r29Headers = new[] { "#", "Student No (from Rule 29)", "Qualification Code (from Rule 29)", "Rule 29 Error Message", "Confirmed by Rule 67?" };
                for (int i = 0; i < r29Headers.Length; i++)
                {
                    var hc = wsR29.Cell(2, i + 1);
                    hc.Value = r29Headers[i]; hc.Style.Font.Bold = true;
                    hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#880E4F");
                    hc.Style.Font.FontColor = XLColor.White;
                }
                int rr = 3;
                foreach (var row in summary.Rule29OnlyRows)
                {
                    var isConf      = string.Equals(row.ConfirmedByR67, "Yes", StringComparison.OrdinalIgnoreCase);
                    var isNotInCreg = string.Equals(row.ConfirmedByR67, "Not in CREG", StringComparison.OrdinalIgnoreCase);
                    wsR29.Cell(rr, 1).Value = row.RowNumber;
                    wsR29.Cell(rr, 2).Value = row.StudentNo;
                    wsR29.Cell(rr, 3).Value = row.QualCode;
                    wsR29.Cell(rr, 4).Value = row.ErrorMessage;
                    wsR29.Cell(rr, 5).Value = isConf      ? "Yes — Rule 67 also FAIL"
                                            : isNotInCreg ? "Not in CREG"
                                            :               "No — Rule 67 = PASS";
                    var rowColor = isConf      ? XLColor.FromHtml("#E8F5E9")
                                 : isNotInCreg ? XLColor.FromHtml("#FCE4EC")
                                 :               XLColor.FromHtml("#FFF8E1");
                    wsR29.Range(rr, 1, rr, 5).Style.Fill.BackgroundColor = rowColor;
                    var confCell = wsR29.Cell(rr, 5);
                    confCell.Style.Font.Bold = true;
                    confCell.Style.Font.FontColor = isConf      ? XLColor.FromHtml("#2E7D32")
                                                  : isNotInCreg ? XLColor.FromHtml("#B71C1C")
                                                  :               XLColor.FromHtml("#F57F17");
                    rr++;
                }
                for (int c = 1; c <= 5; c++) wsR29.Column(c).AdjustToContents();

                // Rule 29 Confirmed by Rule 67 sheet — subset of detail pairs confirmed in Rule 67 FAIL
                var wsR29Conf = wb.Worksheets.Add("R29 Confirmed by R67");
                StyleHeaderRow(wsR29Conf, 1, $"RULE 29 CONFIRMED BY RULE 67: Records from [{summary.DetailTable}] (error {summary.DetailErrorCode}) also found as FAIL in Rule 67", 3);
                var r29ConfHeaders = new[] { "#", "Student No (from Rule 29)", "Qualification Code (from Rule 29)" };
                for (int i = 0; i < r29ConfHeaders.Length; i++)
                {
                    var hc = wsR29Conf.Cell(2, i + 1);
                    hc.Value = r29ConfHeaders[i]; hc.Style.Font.Bold = true;
                    hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#1565C0");
                    hc.Style.Font.FontColor = XLColor.White;
                }
                int rrc = 3;
                int r29ConfNum = 1;
                foreach (var row in summary.Rule29OnlyRows.Where(r => string.Equals(r.ConfirmedByR67, "Yes", StringComparison.OrdinalIgnoreCase)))
                {
                    wsR29Conf.Cell(rrc, 1).Value = r29ConfNum++;
                    wsR29Conf.Cell(rrc, 2).Value = row.StudentNo;
                    wsR29Conf.Cell(rrc, 3).Value = row.QualCode;
                    wsR29Conf.Range(rrc, 1, rrc, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#E3F2FD");
                    rrc++;
                }
                for (int c = 1; c <= 3; c++) wsR29Conf.Column(c).AdjustToContents();

                // R29 In CREG - Not Confirmed — detail pairs in CREG but Rule 67 = PASS
                var wsR29NotConf = wb.Worksheets.Add("R29 In CREG - Not Confirmed");
                StyleHeaderRow(wsR29NotConf, 1, $"RULE 29 IN CREG — NOT CONFIRMED BY RULE 67: From [{summary.DetailTable}] (error {summary.DetailErrorCode}) — student IS in CREG but Rule 67 result = PASS", 3);
                var r29NotConfHeaders = new[] { "#", "Student No (from Rule 29)", "Qualification Code (from Rule 29)" };
                for (int i = 0; i < r29NotConfHeaders.Length; i++)
                {
                    var hc = wsR29NotConf.Cell(2, i + 1);
                    hc.Value = r29NotConfHeaders[i]; hc.Style.Font.Bold = true;
                    hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#6A1B9A");
                    hc.Style.Font.FontColor = XLColor.White;
                }
                int rrNc = 3;
                int r29NotConfNum = 1;
                foreach (var row in summary.Rule29OnlyRows.Where(r => string.Equals(r.ConfirmedByR67, "No", StringComparison.OrdinalIgnoreCase)))
                {
                    wsR29NotConf.Cell(rrNc, 1).Value = r29NotConfNum++;
                    wsR29NotConf.Cell(rrNc, 2).Value = row.StudentNo;
                    wsR29NotConf.Cell(rrNc, 3).Value = row.QualCode;
                    wsR29NotConf.Range(rrNc, 1, rrNc, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8E1");
                    rrNc++;
                }
                for (int c = 1; c <= 3; c++) wsR29NotConf.Column(c).AdjustToContents();

                // R29 Not in CREG — detail pairs where student is NOT in CREG at all (cannot be validated by Rule 67)
                var wsR29NotInCreg = wb.Worksheets.Add("R29 Not in CREG");
                StyleHeaderRow(wsR29NotInCreg, 1, $"RULE 29 NOT IN CREG: From [{summary.DetailTable}] (error {summary.DetailErrorCode}) — student NOT in CREG, cannot be validated by Rule 67", 3);
                var r29NotInCregHeaders = new[] { "#", "Student No (from Rule 29)", "Qualification Code (from Rule 29)" };
                for (int i = 0; i < r29NotInCregHeaders.Length; i++)
                {
                    var hc = wsR29NotInCreg.Cell(2, i + 1);
                    hc.Value = r29NotInCregHeaders[i]; hc.Style.Font.Bold = true;
                    hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#B71C1C");
                    hc.Style.Font.FontColor = XLColor.White;
                }
                int rrNic = 3;
                int r29NotInCregNum = 1;
                foreach (var row in summary.Rule29OnlyRows.Where(r => string.Equals(r.ConfirmedByR67, "Not in CREG", StringComparison.OrdinalIgnoreCase)))
                {
                    wsR29NotInCreg.Cell(rrNic, 1).Value = r29NotInCregNum++;
                    wsR29NotInCreg.Cell(rrNic, 2).Value = row.StudentNo;
                    wsR29NotInCreg.Cell(rrNic, 3).Value = row.QualCode;
                    wsR29NotInCreg.Range(rrNic, 1, rrNic, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFEBEE");
                    rrNic++;
                }
                for (int c = 1; c <= 3; c++) wsR29NotInCreg.Column(c).AdjustToContents();
            }

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 67: CREG-STUD PAIR VALIDATION", 2);
            var summaryRows = new List<(string label, string value)>
            {
                ("Database", summary.Database),
                ("CREG Table", summary.CregTable),
                ("STUD Table", summary.StudTable),
                ("E051 Column", e051Col),
                ("E051 Filter", e051Filter),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("VALIDATION RESULTS", ""),
                ("CREG Total Records", summary.CregRecordCount.ToString("N0")),
                ("STUD Total Records", summary.StudRecordCount.ToString("N0")),
                ("Distinct CREG Pairs Checked", summary.TotalValidated.ToString("N0")),
                ("PASS (in STUD + E051 valid)", summary.PassCount.ToString("N0")),
                ("Fail", summary.NotInStudCount.ToString("N0")),
                ("  -> Fail (E051 Yes)", summary.NotInStudE051ValidCount.ToString("N0")),
                ("  -> Fail (E051 No)", summary.NotInStudE051InvalidCount.ToString("N0")),
                ("FAIL — E051 Invalid", summary.InvalidE051Count.ToString("N0")),
                ("Total FAIL (exception 00708)", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };
            summaryRows = summaryRows
                .Where(item =>
                    !(item.label.Contains("E051 Invalid", StringComparison.Ordinal) &&
                      !item.label.StartsWith("FAIL - E051 Invalid", StringComparison.Ordinal)))
                .ToList();
            summaryRows.Insert(summaryRows.FindIndex(item => item.label.StartsWith("Total FAIL", StringComparison.Ordinal)), ("FAIL - E051 Invalid", summary.InvalidE051Count.ToString("N0")));
            if (hasDetail)
            {
                var r29ConfCount      = summary.Rule29OnlyRows.Count(r => string.Equals(r.ConfirmedByR67, "Yes",         StringComparison.OrdinalIgnoreCase));
                var r29InCregPassCount = summary.Rule29OnlyRows.Count(r => string.Equals(r.ConfirmedByR67, "No",          StringComparison.OrdinalIgnoreCase));
                var r29NotInCregCount  = summary.Rule29OnlyRows.Count(r => string.Equals(r.ConfirmedByR67, "Not in CREG", StringComparison.OrdinalIgnoreCase));
                summaryRows.Add(("", ""));
                summaryRows.Add(("RECONCILIATION vs RULE 29", ""));
                summaryRows.Add(($"Detail Table ({summary.DetailTable})", summary.DetailRecordCount.ToString("N0") + $" records (error {summary.DetailErrorCode})"));
                summaryRows.Add(("Confirmed by Rule 29 (Rule 67 FAIL ∩ Rule 29)", summary.ConfirmedByRule29Count.ToString("N0")));
                summaryRows.Add(("Not Confirmed (Rule 67 FAIL only)", summary.NotInRule29Count.ToString("N0")));
                summaryRows.Add(("Rule 29 All Detail Pairs", summary.Rule29OnlyRows.Count.ToString("N0")));
                summaryRows.Add(("  → R29 Yes — Rule 67 also FAIL", r29ConfCount.ToString("N0")));
                summaryRows.Add(("  → R29 In CREG — Not Confirmed (Rule 67 PASS)", r29InCregPassCount.ToString("N0")));
                summaryRows.Add(("  → R29 Not in CREG (cannot be validated by Rule 67)", r29NotInCregCount.ToString("N0")));
            }
            int sRow = 2;
            foreach (var (label, value) in summaryRows)
            {
                if (label is "VALIDATION RESULTS" or "RECONCILIATION vs RULE 29")
                {
                    var hc = wsSummary.Cell(sRow, 1); hc.Value = label; hc.Style.Font.Bold = true;
                    hc.Style.Fill.BackgroundColor = XLColor.FromHtml(label == "RECONCILIATION vs RULE 29" ? "#880E4F" : "#1A237E");
                    hc.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(sRow, 1, sRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(sRow, 1).Value = label; wsSummary.Cell(sRow, 1).Style.Font.Bold = true; wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sRow, 2).Value = value;
                    if (label == "Status") { wsSummary.Cell(sRow, 2).Style.Fill.BackgroundColor = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2"); wsSummary.Cell(sRow, 2).Style.Font.Bold = true; }
                }
                sRow++;
            }
            wsSummary.Column(1).Width = 45; wsSummary.Column(2).Width = 70;
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule67Csv(Rule67ValidationSummary summary)
        {
            var e051Col = summary.CregE051Col ?? "_051";
            var hasDetail = !string.IsNullOrWhiteSpace(summary.DetailTable);
            var sb = new StringBuilder();
            sb.AppendLine($"Validation_Number,CREG_STUD_NO,CREG_QUAL,CREG_{e051Col},STUD_NO,STUD_QUAL,IN_STUD,E051_VALID,Validation_Result,Exception_Code{(hasDetail ? ",Reconciliation" : "")}");
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                string DV(string k) => v.TryGetValue(k, out var val) ? val ?? "" : "";
                var fields = new List<string>
                {
                    CsvEscape(row.ValidationNumber.ToString()),
                    CsvEscape(DV("CREG_STUD_NO")),
                    CsvEscape(DV("CREG_QUAL")),
                    CsvEscape(DV("CREG_E051")),
                    CsvEscape(DV("STUD_NO")),
                    CsvEscape(DV("STUD_QUAL")),
                    CsvEscape(DV("IN_STUD")),
                    CsvEscape(DV("E051_VALID")),
                    CsvEscape(row.ValidationResult),
                    CsvEscape(string.Equals(row.ValidationResult,"FAIL",StringComparison.OrdinalIgnoreCase) ? row.ExceptionCode : "")
                };
                if (hasDetail) fields.Add(CsvEscape(DV("Reconciliation_Status")));
                sb.AppendLine(string.Join(",", fields));
            }
            return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 68: Credit Overload Validation ──────────────────────────────

        public byte[] ExportRule68Excel(Rule68ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var creditsCol = summary.CredCreditsCol ?? "_036";
            var hasDetail  = !string.IsNullOrWhiteSpace(summary.DetailTable);

            void WriteHeader(IXLWorksheet ws, int row, string[] headers, string hexBg)
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    var c = ws.Cell(row, i + 1);
                    c.Value = headers[i];
                    c.Style.Font.Bold = true;
                    c.Style.Font.FontColor = XLColor.White;
                    c.Style.Fill.BackgroundColor = XLColor.FromHtml(hexBg);
                    c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
            }

            void WriteR68Sheet(IXLWorksheet ws, IEnumerable<Rule68ValidationRowRecord> rows, string hexBg)
            {
                var headers = hasDetail
                    ? new[] { "#", "Student_No", "Qual_Code", "Qual_Name", "Course_Count", $"Total_{creditsCol}", "Validation_Result", "Error_Code", "Reconciliation_Status", "Validation_Explanation" }
                    : new[] { "#", "Student_No", "Qual_Code", "Qual_Name", "Course_Count", $"Total_{creditsCol}", "Validation_Result", "Error_Code", "Validation_Explanation" };
                WriteHeader(ws, 1, headers, hexBg);
                var dataRow = 2;
                foreach (var row in rows)
                {
                    var v = row.DisplayValues;
                    string DV(string k) => v.TryGetValue(k, out var val) ? val ?? "" : "";
                    ws.Cell(dataRow, 1).Value = row.ValidationNumber;
                    ws.Cell(dataRow, 2).Value = DV("Student_No");
                    ws.Cell(dataRow, 3).Value = DV("Qual_Code");
                    ws.Cell(dataRow, 4).Value = DV("Qual_Name");
                    ws.Cell(dataRow, 5).Value = DV("Course_Count");
                    ws.Cell(dataRow, 6).Value = DV("Total_Credits");
                    ws.Cell(dataRow, 7).Value = row.ValidationResult;
                    ws.Cell(dataRow, 8).Value = row.ErrorCode;
                    if (hasDetail) { ws.Cell(dataRow, 9).Value = row.ReconciliationStatus; ws.Cell(dataRow, 10).Value = DV("Validation_Explanation"); }
                    else ws.Cell(dataRow, 9).Value = DV("Validation_Explanation");
                    dataRow++;
                }
                ws.Columns().AdjustToContents();
            }

            // Summary sheet
            var wsSummary = wb.Worksheets.Add("Summary");
            var summaryHeaders = new[] { "Field", "Value" };
            WriteHeader(wsSummary, 1, summaryHeaders, "#1A237E");
            var summaryData = new List<(string, string)>
            {
                ("Rule", "Rule 68 – Credit Overload Validation"),
                ("Timestamp", summary.Timestamp),
                ("Database", summary.Database),
                ("CREG Table", summary.CregTable),
                ("QUAL Table", summary.QualTable),
                ("CRED Table", summary.CredTable),
                ("CRSE Table", summary.CrseTable),
                ("Detail Table", summary.DetailTable),
                ("Credit Column", $"CRED.[{creditsCol}]"),
                ("CREG Records", summary.CregRecordCount.ToString("N0")),
                ("Total Student-Qualification Pairs", summary.TotalValidated.ToString("N0")),
                ("PASS Count", summary.PassCount.ToString("N0")),
                ("FAIL Count (03603)", summary.FailCount.ToString("N0")),
                ("Exception Rate", summary.ExceptionRate.ToString("0.00") + "%"),
                ("Confirmed by Rule 32", summary.ConfirmedByRule32Count.ToString("N0")),
                ("Not in Rule 32", summary.NotInRule32Count.ToString("N0")),
                ("In Rule 32 Only", summary.Rule32OnlyCount.ToString("N0")),
                ("Overall Status", summary.Status)
            };
            for (var i = 0; i < summaryData.Count; i++)
            {
                wsSummary.Cell(i + 2, 1).Value = summaryData[i].Item1;
                wsSummary.Cell(i + 2, 2).Value = summaryData[i].Item2;
            }
            wsSummary.Columns().AdjustToContents();

            var allRows  = summary.ReviewRows.OrderBy(r => r.ValidationNumber).ToList();
            var failRows = allRows.Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();
            var passRows = allRows.Where(r => string.Equals(r.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase)).ToList();

            WriteR68Sheet(wb.Worksheets.Add("All Results"), allRows,  "#1A237E");
            WriteR68Sheet(wb.Worksheets.Add("PASS"),        passRows, "#1B5E20");
            WriteR68Sheet(wb.Worksheets.Add("FAIL (03603)"),failRows, "#B71C1C");

            if (hasDetail && summary.Rule32OnlyRows.Count > 0)
            {
                var wsR32 = wb.Worksheets.Add("Rule 32 Only");
                var r32Headers = new[] { "#", "Student_No", "Qual_Code", "Sum_Credits", "Confirmed_by_R68" };
                WriteHeader(wsR32, 1, r32Headers, "#F57F17");
                var dr = 2;
                foreach (var row in summary.Rule32OnlyRows)
                {
                    wsR32.Cell(dr, 1).Value = row.RowNumber;
                    wsR32.Cell(dr, 2).Value = row.StudentNo;
                    wsR32.Cell(dr, 3).Value = row.QualCode;
                    wsR32.Cell(dr, 4).Value = row.SumCredits;
                    wsR32.Cell(dr, 5).Value = row.ConfirmedByR68;
                    dr++;
                }
                wsR32.Columns().AdjustToContents();
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule68Csv(Rule68ValidationSummary summary)
        {
            var creditsCol = summary.CredCreditsCol ?? "_036";
            var hasDetail  = !string.IsNullOrWhiteSpace(summary.DetailTable);
            var sb = new StringBuilder();
            sb.AppendLine($"Validation_Number,Student_No,Qual_Code,Qual_Name,Course_Count,Total_{creditsCol},Validation_Result,Error_Code{(hasDetail ? ",Reconciliation_Status" : "")},Validation_Explanation");
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                string DV(string k) => v.TryGetValue(k, out var val) ? val ?? "" : "";
                var fields = new List<string>
                {
                    CsvEscape(row.ValidationNumber.ToString()),
                    CsvEscape(DV("Student_No")),
                    CsvEscape(DV("Qual_Code")),
                    CsvEscape(DV("Qual_Name")),
                    CsvEscape(DV("Course_Count")),
                    CsvEscape(DV("Total_Credits")),
                    CsvEscape(row.ValidationResult),
                    CsvEscape(row.ErrorCode)
                };
                if (hasDetail) fields.Add(CsvEscape(row.ReconciliationStatus));
                fields.Add(CsvEscape(DV("Validation_Explanation")));
                sb.AppendLine(string.Join(",", fields));
            }
            return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 11: QUAL vs CESM vs PQM Validation ───────────────────────────

        public byte[] ExportRule11Excel(Rule11ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var passRow   = XLColor.FromHtml("#F3FFF3");
            var failRow   = XLColor.FromHtml("#FFF3F3");
            var reviewRow = XLColor.FromHtml("#FFF8E1");
            var manualBg  = XLColor.FromHtml("#FFFDE7");

            // Previous extraction flat header — extended with PQM reconciliation + controls
            var r11Headers = new[]
            {
                "Extract_Number",
                "Qualification_Code",
                "Previous_Years_Qualification_Code",
                "Qualification_Name_Designator",
                "Approval_Status",
                "Qualification_Type_Descriptor",
                "Minimum_Time_Total",
                "Minimum_Time_Experiential",
                "Institution_Programme_Name",
                "Qualifier",
                "Abbreviation",
                "Legacy_Indicator",
                "NQF_Exit_Level",
                "Minimum_Total_Credits",
                "Minimum_Credits_At_Level",
                "Maximum_Credits_At_Level",
                "Mode_Of_Delivery",
                "Total_Subsidy_Units",
                // CESM reconciliation
                "CESM_Table_Code",
                "CESM_Found",
                "Population_Type",
                // PQM reconciliation columns
                "PQM_Authorised_Qualification_Name",
                "PQM_HEQF_Qual_Type",
                "PQM_CESM_Code",
                "PQM_Min_Time_Total",
                "PQM_WIL",
                "PQM_Accreditation_Ref",
                "PQM_Total_Subsidy",
                // Match & control columns
                "Name_Match",
                "HEQF_Type_Match",
                "CESM_Code_vs_PQM",
                "Review",
                "C2_Type",
                "C3_Min_Time",
                "C4_WIL",
                "C5_HEQF_Indicator",
                "C6_Subsidy",
                // Result
                "Validation_Result",
                "Validation_Reason"
            };
            var r11ColCount = r11Headers.Length;

            void WriteR11Header(IXLWorksheet ws, int hRow)
            {
                for (var i = 0; i < r11Headers.Length; i++)
                {
                    var cell = ws.Cell(hRow, i + 1);
                    cell.Value = r11Headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.WrapText = true;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                ws.Row(hRow).Height = 36;
            }

            string BuildReason(Rule11ValidationRow row)
            {
                var parts = new List<string>();
                if (row.FailedControls.Count > 0)
                    parts.Add("Failed controls: " + string.Join(", ", row.FailedControls));
                if (!string.IsNullOrWhiteSpace(row.ExceptionReason))
                    parts.Add(row.ExceptionReason);
                return parts.Count > 0
                    ? string.Join(" | ", parts)
                    : row.ValidationResult == "PASS" ? "All Rule 11 checks passed." : "";
            }

            void WriteR11DataRow(IXLWorksheet ws, int r, Rule11ValidationRow row, bool exSheet)
            {
                var isPass = string.Equals(row.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase);
                var bg = row.NeedsReview ? reviewRow : isPass ? passRow : failRow;
                var reason = BuildReason(row);

                var colData = new object?[]
                {
                    row.ValidationNumber,
                    row.QualId,
                    row.Qual002,
                    row.QualName,
                    row.QualApproval,
                    row.QualHeqfType,
                    row.Qual053,
                    row.Qual054,
                    row.Qual081,
                    row.Qual082,
                    row.Qual083,
                    row.Qual084,
                    row.Qual085,
                    row.Qual086,
                    row.Qual087,
                    row.Qual088,
                    row.Qual089,
                    row.Qual090,
                    row.CesmCode ?? "",
                    string.IsNullOrWhiteSpace(row.CesmCode) ? "NO" : "YES",  // CESM_Found
                    row.PopulationType,
                    string.IsNullOrWhiteSpace(row.PqmName) ? "Not found in PQM" : row.PqmName,
                    row.PqmHeqfType   ?? "",
                    row.PqmCode       ?? "",
                    row.PqmMinTimeTotal ?? "",
                    row.PqmWIL          ?? "",
                    row.PqmAccreditation ?? "",
                    row.PqmTotalSubsidy  ?? "",
                    row.NameMatch      ? "YES" : "NO",
                    row.HeqfTypeMatch  ? "YES" : "NO",
                    row.CesmCodeMatch  ? "YES" : "NO",
                    row.NeedsReview    ? "YES" : "NO",
                    row.C2_TypeMatch   ? "P" : "F",
                    row.C3_MinTimeMatch? "P" : "F",
                    row.C4_WILMatch    ? "P" : "F",
                    row.C5_HeqfMatch   ? "P" : "F",
                    row.C6_SubsidyMatch? "P" : "F",
                    row.ValidationResult,
                    reason
                };

                for (var i = 0; i < colData.Length; i++)
                {
                    ws.Cell(r, i + 1).Value = XLCellValue.FromObject(colData[i]);
                    // highlight empty CESM/PQM reconciliation cells (0-based: CESM=18, PQM=21–27)
                    var isEmptyCesm = i == 18 && string.IsNullOrWhiteSpace(colData[i]?.ToString());
                    var isEmptyPqm  = i >= 22 && i <= 28 && string.IsNullOrWhiteSpace(colData[i]?.ToString());
                    ws.Cell(r, i + 1).Style.Fill.BackgroundColor = (isEmptyCesm || isEmptyPqm) ? manualBg : bg;
                    // colour CESM_Found cell
                    if (i == 19)
                        ws.Cell(r, i + 1).Style.Font.FontColor = string.IsNullOrWhiteSpace(row.CesmCode)
                            ? XLColor.FromHtml("#B71C1C") : XLColor.FromHtml("#1B5E20");
                }

                var resultCol = r11ColCount - 1; // Validation_Result column (1-based)
                ws.Cell(r, resultCol).Style.Font.Bold = true;
                ws.Cell(r, resultCol).Style.Font.FontColor = row.NeedsReview
                    ? XLColor.FromHtml("#B45309")
                    : isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");

                if (exSheet || !isPass)
                    ws.Cell(r, r11ColCount).Style.Font.FontColor = XLColor.FromHtml("#991B1B");
                else if (row.NeedsReview)
                    ws.Cell(r, r11ColCount).Style.Font.FontColor = XLColor.FromHtml("#92400E");
                else
                    ws.Cell(r, r11ColCount).Style.Font.FontColor = XLColor.FromHtml("#166534");

                ws.Cell(r, r11ColCount).Style.Alignment.WrapText = true;
                ws.Row(r).Height = string.IsNullOrWhiteSpace(reason) ? 18 : 42;
            }

            void AdjustR11Cols(IXLWorksheet ws)
            {
                ws.Column(1).Width  = 6;   // Extract_Number
                ws.Column(2).Width  = 14;  // Qualification_Code
                ws.Column(3).Width  = 16;  // Previous_Years
                ws.Column(4).Width  = 44;  // Qualification_Name
                ws.Column(5).Width  = 12;  // Approval_Status
                ws.Column(6).Width  = 14;  // Type
                ws.Column(7).Width  = 14;  // Min_Time_Total
                ws.Column(8).Width  = 14;  // Min_Time_WIL
                for (var c = 9; c <= 17; c++) ws.Column(c).Width = 12;
                ws.Column(18).Width = 14;  // Total_Subsidy
                ws.Column(19).Width = 14;  // CESM_Table_Code
                ws.Column(20).Width = 10;  // CESM_Found
                ws.Column(21).Width = 16;  // Population_Type
                ws.Column(22).Width = 44;  // PQM_Name
                ws.Column(23).Width = 16;  // PQM_HEQF_Type
                ws.Column(24).Width = 14;  // PQM_CESM_Code
                ws.Column(25).Width = 14;  // PQM_Min_Time_Total
                ws.Column(26).Width = 12;  // PQM_WIL
                ws.Column(27).Width = 32;  // PQM_Accreditation
                ws.Column(28).Width = 14;  // PQM_Total_Subsidy
                for (var c = 29; c <= 37; c++) ws.Column(c).Width = 10;
                ws.Column(38).Width = 12;  // Validation_Result
                ws.Column(39).Width = 70;  // Validation_Reason
            }

            Rule11ValidationRow ExToRow(Rule11ExceptionRecord ex) => new()
            {
                ValidationNumber = ex.ValidationNumber, QualId = ex.QualId, Qual002 = ex.Qual002,
                QualName = ex.QualName, QualApproval = ex.QualApproval, QualHeqfType = ex.QualHeqfType,
                Qual053 = ex.Qual053, Qual054 = ex.Qual054, Qual081 = ex.Qual081, Qual082 = ex.Qual082,
                Qual083 = ex.Qual083, Qual084 = ex.Qual084, Qual085 = ex.Qual085, Qual086 = ex.Qual086,
                Qual087 = ex.Qual087, Qual088 = ex.Qual088, Qual089 = ex.Qual089, Qual090 = ex.Qual090,
                CesmCode = ex.CesmCode, PopulationType = ex.PopulationType,
                PqmName = ex.PqmName, PqmHeqfType = ex.PqmHeqfType, PqmCode = ex.PqmCode,
                PqmMinTimeTotal = ex.PqmMinTimeTotal, PqmWIL = ex.PqmWIL,
                PqmAccreditation = ex.PqmAccreditation, PqmTotalSubsidy = ex.PqmTotalSubsidy,
                NameMatch = ex.NameMatch, HeqfTypeMatch = ex.HeqfTypeMatch, CesmCodeMatch = ex.CesmCodeMatch,
                NeedsReview = ex.NeedsReview,
                C2_TypeMatch = ex.C2_TypeMatch, C3_MinTimeMatch = ex.C3_MinTimeMatch,
                C4_WILMatch = ex.C4_WILMatch, C5_HeqfMatch = ex.C5_HeqfMatch,
                C6_SubsidyMatch = ex.C6_SubsidyMatch, FailedControls = ex.FailedControls ?? new(),
                ValidationResult = ex.ValidationResult, ExceptionReason = ex.ExceptionReason
            };

            // ── Sheet 1: Validation Results ──────────────────────────────────
            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "HEMIS RULE 11: QUAL vs CESM vs PQM VALIDATION", r11ColCount);
            WriteR11Header(wsResults, 2);
            AdjustR11Cols(wsResults);
            wsResults.SheetView.FreezeRows(2);

            var rowIdx = 3;
            foreach (var row in summary.ValidationRows)
                WriteR11DataRow(wsResults, rowIdx++, row, false);

            // ── Sheet 2: Exceptions ──────────────────────────────────────────
            var wsEx = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsEx, 1, "HEMIS RULE 11: EXCEPTIONS", r11ColCount);
            WriteR11Header(wsEx, 2);
            AdjustR11Cols(wsEx);
            wsEx.SheetView.FreezeRows(2);

            var exRow = 3;
            foreach (var ex in summary.Exceptions)
                WriteR11DataRow(wsEx, exRow++, ExToRow(ex), true);

            // ── Sheet 3: Summary ─────────────────────────────────────────────
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 11: QUAL vs CESM vs PQM VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",               summary.Database),
                ("QUAL Table",             $"{summary.QualTable} — ID: {summary.QualIdCol} | Name: {summary.QualNameCol} | Approval: {summary.QualApprovalCol} | Type: {summary.QualHeqfTypeCol} | MinTime: {summary.QualMinTimeTotalCol} | WIL: {summary.QualMinTimeWilCol} | HEQF: {summary.QualHeqfCol} | Subsidy: {summary.QualTotalSubsidyCol}"),
                ("CESM Table",             $"{summary.CesmTable} — ID: {summary.CesmIdCol} | Code: {summary.CesmCodeCol}"),
                ("PQM Table",              $"{summary.PqmTable} — Name: {summary.PqmNameCol} | Type: {summary.PqmHeqfTypeCol} | Code: {summary.PqmCodeCol} | MinTime: {summary.PqmMinTimeTotalCol} | WIL: {summary.PqmWilCol} | Accreditation: {summary.PqmAccreditationCol} | Subsidy: {summary.PqmTotalSubsidyCol}"),
                ("HEQF Indicator Codes",   summary.HeqfIndicatorCodesCsv),
                ("M-prefix Pop. Split",    summary.UseMPrefixPopulationSplit ? "Yes" : "No"),
                ("Validation Date",        summary.Timestamp),
                ("", ""),
                ("VALIDATION RESULTS", ""),
                ("Total Validated",        summary.TotalValidated.ToString("N0")),
                ("PASS",                   summary.PassCount.ToString("N0")),
                ("PASS (Review)",          summary.ReviewCount.ToString("N0")),
                ("FAIL",                   summary.FailCount.ToString("N0")),
                ("Exception Rate",         $"{summary.ExceptionRate:F2}%"),
                ("Status",                 summary.Status),
                ("", ""),
                ("CONTROL SUMMARIES", ""),
            };

            var sRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label is "VALIDATION RESULTS" or "CONTROL SUMMARIES")
                {
                    wsSummary.Range(sRow, 1, sRow, 2).Merge();
                    var hc = wsSummary.Cell(sRow, 1);
                    hc.Value = label; hc.Style.Font.Bold = true;
                    hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hc.Style.Font.FontColor = XLColor.White;
                }
                else if (label != "")
                {
                    wsSummary.Cell(sRow, 1).Value = label;
                    wsSummary.Cell(sRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sRow, 2).Value = value;
                    if (label == "Status")
                    {
                        wsSummary.Cell(sRow, 2).Style.Fill.BackgroundColor =
                            value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(sRow, 2).Style.Font.Bold = true;
                    }
                }
                sRow++;
            }

            foreach (var cs in summary.ControlSummaries)
            {
                wsSummary.Cell(sRow, 1).Value = cs.ControlLabel;
                wsSummary.Cell(sRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                wsSummary.Cell(sRow, 2).Value = $"{cs.CriteriaText} — Pass: {cs.PassCount} | Fail: {cs.FailCount} | {cs.Status}";
                wsSummary.Cell(sRow, 2).Style.Fill.BackgroundColor =
                    cs.Status == "PASS" ? XLColor.FromHtml("#F3FFF3") : XLColor.FromHtml("#FFF3F3");
                sRow++;
            }

            wsSummary.Column(1).Width = 36;
            wsSummary.Column(2).Width = 90;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule11Csv(Rule11ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            var headers = new[]
            {
                "Extract_Number", "Qualification_Code", "Previous_Years_Qualification_Code",
                "Qualification_Name_Designator", "Approval_Status", "Qualification_Type_Descriptor",
                "Minimum_Time_Total", "Minimum_Time_Experiential", "Institution_Programme_Name",
                "Qualifier", "Abbreviation", "Legacy_Indicator",
                "NQF_Exit_Level", "Minimum_Total_Credits", "Minimum_Credits_At_Level",
                "Maximum_Credits_At_Level", "Mode_Of_Delivery", "Total_Subsidy_Units",
                "CESM_Table_Code", "CESM_Found", "Population_Type",
                "PQM_Authorised_Qualification_Name", "PQM_HEQF_Qual_Type", "PQM_CESM_Code",
                "PQM_Min_Time_Total", "PQM_WIL", "PQM_Accreditation_Ref", "PQM_Total_Subsidy",
                "Name_Match", "HEQF_Type_Match", "CESM_Code_vs_PQM",
                "Review", "C2_Type", "C3_Min_Time", "C4_WIL", "C5_HEQF_Indicator", "C6_Subsidy",
                "Validation_Result", "Validation_Reason"
            };
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            IEnumerable<Rule11ValidationRow> rows = exceptionsOnly
                ? summary.Exceptions.Select(ex => new Rule11ValidationRow
                {
                    ValidationNumber         = ex.ValidationNumber,
                    QualId                   = ex.QualId,  Qual002 = ex.Qual002,
                    QualName                 = ex.QualName, QualApproval = ex.QualApproval,
                    QualHeqfType             = ex.QualHeqfType,
                    Qual053 = ex.Qual053, Qual054 = ex.Qual054,
                    Qual081 = ex.Qual081, Qual082 = ex.Qual082, Qual083 = ex.Qual083,
                    Qual084 = ex.Qual084, Qual085 = ex.Qual085, Qual086 = ex.Qual086,
                    Qual087 = ex.Qual087, Qual088 = ex.Qual088, Qual089 = ex.Qual089, Qual090 = ex.Qual090,
                    CesmCode = ex.CesmCode, PopulationType = ex.PopulationType,
                    PqmName = ex.PqmName, PqmHeqfType = ex.PqmHeqfType, PqmCode = ex.PqmCode,
                    PqmMinTimeTotal = ex.PqmMinTimeTotal, PqmWIL = ex.PqmWIL,
                    PqmAccreditation = ex.PqmAccreditation, PqmTotalSubsidy = ex.PqmTotalSubsidy,
                    NameMatch = ex.NameMatch, HeqfTypeMatch = ex.HeqfTypeMatch,
                    CesmCodeMatch = ex.CesmCodeMatch, NeedsReview = ex.NeedsReview,
                    C2_TypeMatch = ex.C2_TypeMatch, C3_MinTimeMatch = ex.C3_MinTimeMatch,
                    C4_WILMatch = ex.C4_WILMatch, C5_HeqfMatch = ex.C5_HeqfMatch,
                    C6_SubsidyMatch = ex.C6_SubsidyMatch,
                    FailedControls = ex.FailedControls ?? new(),
                    ValidationResult = ex.ValidationResult, ExceptionReason = ex.ExceptionReason
                })
                : summary.ValidationRows;

            foreach (var row in rows)
            {
                var reason = row.FailedControls.Count > 0
                    ? $"Failed: {string.Join(", ", row.FailedControls)}. {row.ExceptionReason}"
                    : row.ExceptionReason ?? "";
                sb.AppendLine(string.Join(",",
                    row.ValidationNumber,
                    CsvEscape(row.QualId),        CsvEscape(row.Qual002),
                    CsvEscape(row.QualName),      CsvEscape(row.QualApproval),
                    CsvEscape(row.QualHeqfType),
                    CsvEscape(row.Qual053),       CsvEscape(row.Qual054),
                    CsvEscape(row.Qual081),       CsvEscape(row.Qual082),       CsvEscape(row.Qual083),
                    CsvEscape(row.Qual084),       CsvEscape(row.Qual085),       CsvEscape(row.Qual086),
                    CsvEscape(row.Qual087),       CsvEscape(row.Qual088),       CsvEscape(row.Qual089),
                    CsvEscape(row.Qual090),
                    CsvEscape(row.CesmCode),
                    string.IsNullOrWhiteSpace(row.CesmCode) ? "NO" : "YES",   // CESM_Found
                    CsvEscape(row.PopulationType),
                    CsvEscape(row.PqmName),       CsvEscape(row.PqmHeqfType),  CsvEscape(row.PqmCode),
                    CsvEscape(row.PqmMinTimeTotal), CsvEscape(row.PqmWIL),
                    CsvEscape(row.PqmAccreditation), CsvEscape(row.PqmTotalSubsidy),
                    row.NameMatch       ? "YES" : "NO",
                    row.HeqfTypeMatch   ? "YES" : "NO",
                    row.CesmCodeMatch   ? "YES" : "NO",
                    row.NeedsReview     ? "YES" : "NO",
                    row.C2_TypeMatch    ? "P" : "F",
                    row.C3_MinTimeMatch ? "P" : "F",
                    row.C4_WILMatch     ? "P" : "F",
                    row.C5_HeqfMatch    ? "P" : "F",
                    row.C6_SubsidyMatch ? "P" : "F",
                    CsvEscape(row.ValidationResult),
                    CsvEscape(reason)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 54: CRED vs QUAL vs PQM Validation ───────────────────────────

        public byte[] ExportRule54Excel(Rule54ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            // ── colour palette matching the spreadsheet ────────────────────────
            var credHdr    = XLColor.FromHtml("#558B2F");
            var credSub    = XLColor.FromHtml("#AED581");
            var qualHdr    = XLColor.FromHtml("#00838F");
            var qualSub    = XLColor.FromHtml("#80DEEA");
            var pqmHdr     = XLColor.FromHtml("#6A1B9A");
            var pqmSub     = XLColor.FromHtml("#CE93D8");
            var passRow    = XLColor.FromHtml("#F3FFF3");
            var failRow    = XLColor.FromHtml("#FFF3F3");
            var manualBg   = XLColor.FromHtml("#FFFDE7"); // soft yellow = needs manual fill

            // ── local helpers ─────────────────────────────────────────────────
            static void WpCell(IXLWorksheet ws, int r, int c, object? val,
                XLColor? bg = null, bool bold = false, bool italic = false,
                bool wrapText = false, XLAlignmentHorizontalValues halign = XLAlignmentHorizontalValues.Left)
            {
                var cell = ws.Cell(r, c);
                if (val != null) cell.Value = XLCellValue.FromObject(val);
                if (bg != null) cell.Style.Fill.BackgroundColor = bg;
                cell.Style.Font.Bold = bold;
                cell.Style.Font.Italic = italic;
                cell.Style.Alignment.WrapText = wrapText;
                cell.Style.Alignment.Horizontal = halign;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            static void SectionBanner(IXLWorksheet ws, int row, int cols, string text)
            {
                ws.Range(row, 1, row, cols).Merge();
                WpCell(ws, row, 1, text, XLColor.FromHtml("#6A1B9A"), bold: true,
                    halign: XLAlignmentHorizontalValues.Left);
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                ws.Row(row).Height = 20;
            }

            // ── column layout (13 cols): #|CRED×4|QUAL×2|PQM×3|Results|TM-a|TM-b
            const int TOTAL_COLS = 13;

            void BuildWorkingPaperHeader(IXLWorksheet ws, int hRow)
            {
                // Row hRow  = group labels
                // Row hRow+1 = sub-column labels

                // col 1 = # (merges 2 rows)
                ws.Range(hRow, 1, hRow + 1, 1).Merge();
                WpCell(ws, hRow, 1, "#", XLColor.FromHtml("#424242"), bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 1).Style.Font.FontColor = XLColor.White;

                // CRED group cols 2-5
                ws.Range(hRow, 2, hRow, 5).Merge();
                WpCell(ws, hRow, 2, "dbo_CRED", credHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 2).Style.Font.FontColor = XLColor.White;

                // QUAL group cols 6-7
                ws.Range(hRow, 6, hRow, 7).Merge();
                WpCell(ws, hRow, 6, "dbo_QUAL", qualHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 6).Style.Font.FontColor = XLColor.White;

                // PQM group cols 8-10
                ws.Range(hRow, 8, hRow, 10).Merge();
                WpCell(ws, hRow, 8, "Details as per PQM / Prospectus", pqmHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 8).Style.Font.FontColor = XLColor.White;

                // Results col 11 (merges 2 rows)
                ws.Range(hRow, 11, hRow + 1, 11).Merge();
                WpCell(ws, hRow, 11, "Results", XLColor.FromHtml("#37474F"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 11).Style.Font.FontColor = XLColor.White;

                // Tickmarks group cols 12-13
                ws.Range(hRow, 12, hRow, 13).Merge();
                WpCell(ws, hRow, 12, "Tickmarks", XLColor.FromHtml("#4A148C"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 12).Style.Font.FontColor = XLColor.White;

                // Sub-headers row hRow+1
                foreach (var (col, lbl, bg) in new (int, string, XLColor)[]
                {
                    (2,  "_001", credSub), (3, "_030", credSub), (4, "_036", credSub), (5, "_050", credSub),
                    (6,  "_001", qualSub), (7, "_003", qualSub),
                    (8,  "Qualification_name_Designator\n(Authorised_Qualification_Name)", pqmSub),
                    (9,  "Course_credit_value Recalculation\nE.g Credit/Total credits from Prospectus in the PDF", pqmSub),
                    (10, "Completed_research_course_credit_value\n(Research_1)", pqmSub),
                    (12, "a", XLColor.FromHtml("#7B1FA2")),
                    (13, "b", XLColor.FromHtml("#7B1FA2"))
                })
                {
                    WpCell(ws, hRow + 1, col, lbl, bg, bold: true, wrapText: true,
                        halign: XLAlignmentHorizontalValues.Center);
                    if (col is 12 or 13) ws.Cell(hRow + 1, col).Style.Font.FontColor = XLColor.White;
                }

                ws.Row(hRow).Height     = 22;
                ws.Row(hRow + 1).Height = 46;
            }

            void SetColWidths(IXLWorksheet ws)
            {
                ws.Column(1).Width  = 6;
                ws.Column(2).Width  = 14;
                ws.Column(3).Width  = 14;
                ws.Column(4).Width  = 12;
                ws.Column(5).Width  = 10;
                ws.Column(6).Width  = 14;
                ws.Column(7).Width  = 38;
                ws.Column(8).Width  = 42;
                ws.Column(9).Width  = 42;
                ws.Column(10).Width = 42;
                ws.Column(11).Width = 10;
                ws.Column(12).Width = 8;
                ws.Column(13).Width = 8;
            }

            void WriteDataRow(IXLWorksheet ws, int r,
                int no, string credId, string credCourse, string credCredit, string credRes1,
                string qualId, string qualName,
                string? pqmName, string? pqmRes1,
                string result, bool isPass)
            {
                var rowBg   = isPass ? passRow : failRow;
                var tmColor = XLColor.FromHtml("#4A148C");

                WpCell(ws, r,  1, no,         rowBg, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r,  2, credId,      rowBg);
                WpCell(ws, r,  3, credCourse,  rowBg);
                WpCell(ws, r,  4, credCredit,  rowBg);
                WpCell(ws, r,  5, credRes1,    rowBg);
                WpCell(ws, r,  6, qualId,      rowBg);
                WpCell(ws, r,  7, qualName,    rowBg, wrapText: true);

                // PQM Qual Name — show value if matched, else leave blank (manual fill)
                if (!string.IsNullOrEmpty(pqmName))
                    WpCell(ws, r, 8, pqmName, rowBg, wrapText: true);
                else
                    WpCell(ws, r, 8, null, manualBg);

                // Credit Recalc — always manual
                WpCell(ws, r, 9, null, manualBg);

                // PQM Research_1 — show value if matched, else leave blank (manual fill)
                if (!string.IsNullOrEmpty(pqmRes1))
                    WpCell(ws, r, 10, pqmRes1, rowBg);
                else
                    WpCell(ws, r, 10, null, manualBg);

                // Results
                WpCell(ws, r, 11, result, rowBg, bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(r, 11).Style.Font.FontColor =
                    isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");

                // Tickmarks a / b — pre-filled for PASS rows, blank for FAIL
                if (isPass)
                {
                    WpCell(ws, r, 12, "a", passRow, bold: true, halign: XLAlignmentHorizontalValues.Center);
                    WpCell(ws, r, 13, "b", passRow, bold: true, halign: XLAlignmentHorizontalValues.Center);
                    ws.Cell(r, 12).Style.Font.FontColor = tmColor;
                    ws.Cell(r, 13).Style.Font.FontColor = tmColor;
                }
                else
                {
                    WpCell(ws, r, 12, null, failRow);
                    WpCell(ws, r, 13, null, failRow);
                }

                ws.Row(r).Height = 18;
            }

            void AppendProceduresAndTickmarks(IXLWorksheet ws, int startRow)
            {
                int r = startRow + 1; // blank spacer
                r++;

                SectionBanner(ws, r, TOTAL_COLS, "Procedures");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a.\tCourse credit value assigned agrees with the credit value defined in the PQM/Prospectus,",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Row(r).Height = 18;
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "b.\tCompleted research course credit value agrees with the fractional time for completed research courses in the PQM/Prospectus.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Row(r).Height = 18;
                r++;

                r++; // blank spacer

                SectionBanner(ws, r, TOTAL_COLS, "Tickmark");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a\tAgreed the completed credit value as per the detail extract to the prospectus.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#4A148C");
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "b\tAgreed the Completed research course credit value with the fractional time for completed research courses in the PQM/Prospectus.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#4A148C");
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
            }

            // ── Sheet 1: Working Paper ────────────────────────────────────────
            var wsResults = wb.Worksheets.Add("Working Paper");

            wsResults.Range(1, 1, 1, TOTAL_COLS).Merge();
            WpCell(wsResults, 1, 1, "HEMIS RULE 54 — CRED vs QUAL vs PQM VALIDATION",
                XLColor.FromHtml("#1A237E"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsResults.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsResults.Cell(1, 1).Style.Font.FontSize  = 13;
            wsResults.Row(1).Height = 24;

            BuildWorkingPaperHeader(wsResults, 2);
            SetColWidths(wsResults);

            int rowIdx = 4;
            foreach (var row in summary.ValidationRows)
            {
                WriteDataRow(wsResults, rowIdx,
                    row.ValidationNumber,
                    row.RecordId, row.CourseCode, row.CreditValue, row.HemisResearch1,
                    row.QualRecordId.Length > 0 ? row.QualRecordId : row.RecordId, row.HemisQualName,
                    row.PqmQualName, row.PqmResearch1,
                    row.ValidationResult, row.ValidationResult == "PASS");
                rowIdx++;
            }

            AppendProceduresAndTickmarks(wsResults, rowIdx);

            // ── Sheet 2: Exceptions ───────────────────────────────────────────
            var wsEx = wb.Worksheets.Add("Exceptions");

            wsEx.Range(1, 1, 1, TOTAL_COLS + 1).Merge();
            WpCell(wsEx, 1, 1, "HEMIS RULE 54 — EXCEPTIONS (CRED vs QUAL vs PQM)",
                XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsEx.Cell(1, 1).Style.Font.FontSize  = 13;
            wsEx.Row(1).Height = 24;

            BuildWorkingPaperHeader(wsEx, 2);
            // Add Exception Reason column (col 14)
            wsEx.Range(2, 14, 3, 14).Merge();
            WpCell(wsEx, 2, 14, "Exception Reason", XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(2, 14).Style.Font.FontColor = XLColor.White;

            SetColWidths(wsEx);
            wsEx.Column(14).Width = 55;

            int exRow = 4;
            foreach (var ex in summary.Exceptions)
            {
                WriteDataRow(wsEx, exRow,
                    ex.ValidationNumber,
                    ex.RecordId, ex.CourseCode, ex.CreditValue, ex.HemisResearch1,
                    ex.QualRecordId.Length > 0 ? ex.QualRecordId : ex.RecordId, ex.HemisQualName,
                    ex.PqmQualName, ex.PqmResearch1,
                    ex.ValidationResult, false);
                WpCell(wsEx, exRow, 14, ex.ExceptionReason, failRow, wrapText: true);
                wsEx.Row(exRow).Height = 18;
                exRow++;
            }

            AppendProceduresAndTickmarks(wsEx, exRow);

            // ── Sheet 3: Summary ──────────────────────────────────────────────
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 54: CRED vs QUAL vs PQM VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",           summary.Database),
                ("CRED Table",         $"{summary.CredTable} (ID: {summary.CredIdCol}, Course: {summary.CredCourseCol}, Credit: {summary.CredCreditCol}, Research_1: {summary.CredResearch1Col})"),
                ("QUAL Table",         $"{summary.QualTable} (ID: {summary.QualIdCol}, Name: {summary.QualNameCol})"),
                ("PQM Table",          $"{summary.PqmTable} (Name: {summary.PqmNameCol}, Research_1: {summary.PqmResearch1Col})"),
                ("Validation Date",    summary.Timestamp),
                ("",                   ""),
                ("VALIDATION RESULTS", ""),
                ("Total Validated",    summary.TotalValidated.ToString("N0")),
                ("PASS (Both matched)", summary.PassCount.ToString("N0")),
                ("FAIL (Mismatch)",    summary.FailCount.ToString("N0")),
                ("Exception Rate",     $"{summary.ExceptionRate:F2}%"),
                ("Status",             summary.Status),
                ("",                   ""),
                ("MATCHING RULES",     ""),
                ("Qual Name Check",    $"QUAL.{summary.QualNameCol} (case-insensitive) must match PQM.{summary.PqmNameCol}"),
                ("Research_1 Check",   $"CRED.{summary.CredResearch1Col} must match PQM.{summary.PqmResearch1Col} on the same PQM row"),
                ("Credit Value (a)",   $"CRED.{summary.CredCreditCol} — manual verification against PDF prospectus required (Tickmark a)"),
                ("Research_1 (b)",     $"CRED.{summary.CredResearch1Col} vs PQM.{summary.PqmResearch1Col} — auto-validated (Tickmark b)")
            };

            int summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label is "VALIDATION RESULTS" or "MATCHING RULES")
                {
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#1A237E"), bold: true);
                    wsSummary.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.White;
                }
                else if (label != "")
                {
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#F5F5F5"), bold: true);
                    WpCell(wsSummary, summaryRow, 2, value);
                    if (label == "Status")
                    {
                        var statusBg = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = statusBg;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }
                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 80;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule54Csv(Rule54ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            const string header = "Validation_Number,CRED_001,CRED_030,CRED_036,CRED_050,QUAL_001,QUAL_003,PQM_Authorised_Qual_Name,PQM_Research_1,Validation_Result,Exception_Reason";

            sb.AppendLine(header);

            if (exceptionsOnly)
            {
                foreach (var ex in summary.Exceptions)
                {
                    sb.AppendLine(string.Join(",",
                        ex.ValidationNumber,
                        CsvEscape(ex.RecordId),
                        CsvEscape(ex.CourseCode),
                        CsvEscape(ex.CreditValue),
                        CsvEscape(ex.HemisResearch1),
                        CsvEscape(ex.QualRecordId.Length > 0 ? ex.QualRecordId : ex.RecordId),
                        CsvEscape(ex.HemisQualName),
                        CsvEscape(ex.PqmQualName),
                        CsvEscape(ex.PqmResearch1),
                        ex.ValidationResult,
                        CsvEscape(ex.ExceptionReason)));
                }
            }
            else
            {
                foreach (var row in summary.ValidationRows)
                {
                    sb.AppendLine(string.Join(",",
                        row.ValidationNumber,
                        CsvEscape(row.RecordId),
                        CsvEscape(row.CourseCode),
                        CsvEscape(row.CreditValue),
                        CsvEscape(row.HemisResearch1),
                        CsvEscape(row.QualRecordId.Length > 0 ? row.QualRecordId : row.RecordId),
                        CsvEscape(row.HemisQualName),
                        CsvEscape(row.PqmQualName),
                        CsvEscape(row.PqmResearch1),
                        row.ValidationResult,
                        CsvEscape(row.ExceptionReason)));
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 55: Graduate W-Code Validation ─────────────────────────────

        public byte[] ExportRule55Excel(Rule55ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var studHdr  = XLColor.FromHtml("#558B2F");
            var studSub  = XLColor.FromHtml("#AED581");
            var qualHdr  = XLColor.FromHtml("#00838F");
            var qualSub  = XLColor.FromHtml("#80DEEA");
            var passRow  = XLColor.FromHtml("#F3FFF3");
            var failRow  = XLColor.FromHtml("#FFF3F3");
            var manualBg = XLColor.FromHtml("#FFFDE7");

            static void WpCell(IXLWorksheet ws, int r, int c, object? val,
                XLColor? bg = null, bool bold = false, bool italic = false,
                bool wrapText = false, XLAlignmentHorizontalValues halign = XLAlignmentHorizontalValues.Left)
            {
                var cell = ws.Cell(r, c);
                if (val != null) cell.Value = XLCellValue.FromObject(val);
                if (bg != null) cell.Style.Fill.BackgroundColor = bg;
                cell.Style.Font.Bold = bold;
                cell.Style.Font.Italic = italic;
                cell.Style.Alignment.WrapText = wrapText;
                cell.Style.Alignment.Horizontal = halign;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            static void SectionBanner(IXLWorksheet ws, int row, int cols, string text)
            {
                ws.Range(row, 1, row, cols).Merge();
                WpCell(ws, row, 1, text, XLColor.FromHtml("#00838F"), bold: true,
                    halign: XLAlignmentHorizontalValues.Left);
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                ws.Row(row).Height = 20;
            }

            // col layout (10 cols): # | STUD×3 | QUAL×4 | Results | TM-a
            const int TOTAL_COLS = 10;

            void BuildHeader(IXLWorksheet ws, int hRow)
            {
                // # col merges 2 rows
                ws.Range(hRow, 1, hRow + 1, 1).Merge();
                WpCell(ws, hRow, 1, "#", XLColor.FromHtml("#424242"), bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 1).Style.Font.FontColor = XLColor.White;

                // STUD group cols 2-4
                ws.Range(hRow, 2, hRow, 4).Merge();
                WpCell(ws, hRow, 2, "dbo_STUD", studHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 2).Style.Font.FontColor = XLColor.White;

                // QUAL group cols 5-8
                ws.Range(hRow, 5, hRow, 8).Merge();
                WpCell(ws, hRow, 5, "dbo_QUAL", qualHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 5).Style.Font.FontColor = XLColor.White;

                // Results col 9 merges 2 rows
                ws.Range(hRow, 9, hRow + 1, 9).Merge();
                WpCell(ws, hRow, 9, "Results", XLColor.FromHtml("#37474F"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 9).Style.Font.FontColor = XLColor.White;

                // Tickmarks col 10 merges 2 rows
                ws.Range(hRow, 10, hRow + 1, 10).Merge();
                WpCell(ws, hRow, 10, "Tickmarks", XLColor.FromHtml("#4A148C"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 10).Style.Font.FontColor = XLColor.White;

                // Sub-headers
                foreach (var (col, lbl, bg) in new (int, string, XLColor)[]
                {
                    (2, "Student_ID (_007)", studSub),
                    (3, "Qual_Code (_001)", studSub),
                    (4, "_025 Value", studSub),
                    (5, "Qual_Code (_001)", qualSub),
                    (6, "Qual_Name (_003)", qualSub),
                    (7, "Qual_Type (_005)", qualSub),
                    (8, "Approval (_004)", qualSub)
                })
                {
                    WpCell(ws, hRow + 1, col, lbl, bg, bold: true, wrapText: true,
                        halign: XLAlignmentHorizontalValues.Center);
                }

                ws.Row(hRow).Height     = 22;
                ws.Row(hRow + 1).Height = 40;
            }

            void SetColWidths(IXLWorksheet ws)
            {
                ws.Column(1).Width  = 6;
                ws.Column(2).Width  = 18;
                ws.Column(3).Width  = 14;
                ws.Column(4).Width  = 12;
                ws.Column(5).Width  = 14;
                ws.Column(6).Width  = 42;
                ws.Column(7).Width  = 20;
                ws.Column(8).Width  = 14;
                ws.Column(9).Width  = 10;
                ws.Column(10).Width = 8;
            }

            void WriteDataRow(IXLWorksheet ws, int r,
                int no, string studentId, string qualCode, string fulfilledStatus,
                string? qualName, string? qualType, string? qualApproval,
                string result, bool isPass)
            {
                var rowBg   = isPass ? passRow : failRow;
                var tmColor = XLColor.FromHtml("#4A148C");

                WpCell(ws, r,  1, no,              rowBg, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r,  2, studentId,        rowBg);
                WpCell(ws, r,  3, qualCode,          rowBg);
                WpCell(ws, r,  4, fulfilledStatus,   rowBg);

                // QUAL cols — null means not found in dbo_QUAL → manualBg
                if (qualName != null)
                    WpCell(ws, r, 5, qualCode, rowBg);
                else
                    WpCell(ws, r, 5, null, manualBg);

                if (qualName != null)
                    WpCell(ws, r, 6, qualName, rowBg, wrapText: true);
                else
                    WpCell(ws, r, 6, null, manualBg);

                if (qualType != null)
                    WpCell(ws, r, 7, qualType, rowBg);
                else
                    WpCell(ws, r, 7, null, manualBg);

                if (qualApproval != null)
                    WpCell(ws, r, 8, qualApproval, rowBg);
                else
                    WpCell(ws, r, 8, null, manualBg);

                WpCell(ws, r, 9, result, rowBg, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(r, 9).Style.Font.FontColor =
                    isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");

                if (isPass)
                {
                    WpCell(ws, r, 10, "a", passRow, bold: true, halign: XLAlignmentHorizontalValues.Center);
                    ws.Cell(r, 10).Style.Font.FontColor = tmColor;
                }
                else
                {
                    WpCell(ws, r, 10, null, failRow);
                }

                ws.Row(r).Height = 18;
            }

            void AppendProceduresAndTickmarks(IXLWorksheet ws, int startRow)
            {
                int r = startRow + 1;
                r++;

                SectionBanner(ws, r, TOTAL_COLS, "Procedures");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a.\tAgreed student W-coded qualification to dbo_QUAL approved register (_004='A') per DHET §1.5.",
                    XLColor.FromHtml("#E0F7FA"), wrapText: true);
                ws.Row(r).Height = 18;
                r++;

                r++;

                SectionBanner(ws, r, TOTAL_COLS, "Tickmark");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a\tAgreed the W-coded student's qualification as approved for state funding in the dbo_QUAL register.",
                    XLColor.FromHtml("#E0F7FA"), wrapText: true);
                ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#4A148C");
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
            }

            // ── Sheet 1: Working Paper ────────────────────────────────────────
            var wsResults = wb.Worksheets.Add("Working Paper");

            wsResults.Range(1, 1, 1, TOTAL_COLS).Merge();
            WpCell(wsResults, 1, 1, "HEMIS RULE 55 — GRADUATE W-CODE VALIDATION",
                XLColor.FromHtml("#1A237E"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsResults.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsResults.Cell(1, 1).Style.Font.FontSize  = 13;
            wsResults.Row(1).Height = 24;

            BuildHeader(wsResults, 2);
            SetColWidths(wsResults);

            int rowIdx = 4;
            foreach (var row in summary.ValidationRows)
            {
                WriteDataRow(wsResults, rowIdx,
                    row.ValidationNumber,
                    row.StudentId, row.QualCode, row.FulfilledStatus,
                    row.QualName, row.QualType, row.QualApprovalStatus,
                    row.ValidationResult, row.ValidationResult == "PASS");
                rowIdx++;
            }

            AppendProceduresAndTickmarks(wsResults, rowIdx);

            // ── Sheet 2: Exceptions ───────────────────────────────────────────
            var wsEx = wb.Worksheets.Add("Exceptions");

            wsEx.Range(1, 1, 1, TOTAL_COLS + 1).Merge();
            WpCell(wsEx, 1, 1, "HEMIS RULE 55 — EXCEPTIONS (W-CODE VALIDATION)",
                XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsEx.Cell(1, 1).Style.Font.FontSize  = 13;
            wsEx.Row(1).Height = 24;

            BuildHeader(wsEx, 2);
            // Exception Reason col 11
            wsEx.Range(2, 11, 3, 11).Merge();
            WpCell(wsEx, 2, 11, "Exception Reason", XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(2, 11).Style.Font.FontColor = XLColor.White;

            SetColWidths(wsEx);
            wsEx.Column(11).Width = 55;

            int exRow = 4;
            foreach (var ex in summary.Exceptions)
            {
                WriteDataRow(wsEx, exRow,
                    ex.ValidationNumber,
                    ex.StudentId, ex.QualCode, ex.FulfilledStatus,
                    ex.QualName, ex.QualType, ex.QualApprovalStatus,
                    ex.ValidationResult, false);
                WpCell(wsEx, exRow, 11, ex.ExceptionReason, failRow, wrapText: true);
                wsEx.Row(exRow).Height = 18;
                exRow++;
            }

            AppendProceduresAndTickmarks(wsEx, exRow);

            // ── Sheet 3: Summary ──────────────────────────────────────────────
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 55: GRADUATE W-CODE VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",         summary.Database),
                ("STUD Table",       $"{summary.StudTable} (ID: {summary.StudIdCol}, Qual Code: {summary.StudQualCodeCol}, Fulfilled: {summary.StudFulfilledCol})"),
                ("QUAL Table",       $"{summary.QualTable} (Code: {summary.QualCodeCol}, Name: {summary.QualNameCol}, Type: {summary.QualTypeCol}, Approval: {summary.QualApprovalCol})"),
                ("Validation Date",  summary.Timestamp),
                ("",                 ""),
                ("VALIDATION RESULTS", ""),
                ("W-Coded Students (pop)", summary.TotalValidated.ToString("N0")),
                ("PASS (Qual & Approved)", summary.PassCount.ToString("N0")),
                ("FAIL (Exception)",       summary.FailCount.ToString("N0")),
                ("Exception Rate",         $"{summary.ExceptionRate:F2}%"),
                ("Status",                 summary.Status),
                ("",                       ""),
                ("VALIDATION RULE",        ""),
                ("Population",             $"dbo_STUD WHERE {summary.StudFulfilledCol} = 'W' (certificate withheld)"),
                ("Join",                   $"LEFT JOIN {summary.QualTable} on {summary.StudQualCodeCol} = {summary.QualCodeCol}"),
                ("PASS Condition",         $"QUAL row found AND {summary.QualApprovalCol} = 'A' (approved for state funding)"),
                ("DHET Reference",         "DHET §1.5: Element 025='W' graduates treated identically to 'F' graduates")
            };

            int summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label is "VALIDATION RESULTS" or "VALIDATION RULE")
                {
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#00838F"), bold: true);
                    wsSummary.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.White;
                }
                else if (label != "")
                {
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#F5F5F5"), bold: true);
                    WpCell(wsSummary, summaryRow, 2, value);
                    if (label == "Status")
                    {
                        var statusBg = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = statusBg;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }
                summaryRow++;
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 80;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule55Csv(Rule55ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            const string header = "Validation_Number,Student_ID,Qual_Code,Fulfilled_Status,Qual_Name,Qual_Type,Qual_Approval_Status,Validation_Result,Exception_Reason";

            sb.AppendLine(header);

            if (exceptionsOnly)
            {
                foreach (var ex in summary.Exceptions)
                {
                    sb.AppendLine(string.Join(",",
                        ex.ValidationNumber,
                        CsvEscape(ex.StudentId),
                        CsvEscape(ex.QualCode),
                        CsvEscape(ex.FulfilledStatus),
                        CsvEscape(ex.QualName),
                        CsvEscape(ex.QualType),
                        CsvEscape(ex.QualApprovalStatus),
                        ex.ValidationResult,
                        CsvEscape(ex.ExceptionReason)));
                }
            }
            else
            {
                foreach (var row in summary.ValidationRows)
                {
                    sb.AppendLine(string.Join(",",
                        row.ValidationNumber,
                        CsvEscape(row.StudentId),
                        CsvEscape(row.QualCode),
                        CsvEscape(row.FulfilledStatus),
                        CsvEscape(row.QualName),
                        CsvEscape(row.QualType),
                        CsvEscape(row.QualApprovalStatus),
                        row.ValidationResult,
                        CsvEscape(row.ExceptionReason)));
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportRule57Excel(Rule57ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var studHdr = XLColor.FromHtml("#1565C0");
            var studSub = XLColor.FromHtml("#90CAF9");
            var cregHdr = XLColor.FromHtml("#E65100");
            var cregSub = XLColor.FromHtml("#FFCC80");
            var passRow = XLColor.FromHtml("#F3FFF3");
            var failRow = XLColor.FromHtml("#FFF3F3");

            static void WpCell(IXLWorksheet ws, int r, int c, object? val,
                XLColor? bg = null, bool bold = false, bool italic = false,
                bool wrapText = false, XLAlignmentHorizontalValues halign = XLAlignmentHorizontalValues.Left)
            {
                var cell = ws.Cell(r, c);
                if (val != null) cell.Value = XLCellValue.FromObject(val);
                if (bg != null) cell.Style.Fill.BackgroundColor = bg;
                cell.Style.Font.Bold = bold;
                cell.Style.Font.Italic = italic;
                cell.Style.Alignment.WrapText = wrapText;
                cell.Style.Alignment.Horizontal = halign;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            static void SectionBanner(IXLWorksheet ws, int row, int cols, string text)
            {
                ws.Range(row, 1, row, cols).Merge();
                WpCell(ws, row, 1, text, XLColor.FromHtml("#1565C0"), bold: true,
                    halign: XLAlignmentHorizontalValues.Left);
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                ws.Row(row).Height = 20;
            }

            const int TOTAL_COLS = 8;

            void BuildHeader(IXLWorksheet ws, int hRow)
            {
                ws.Range(hRow, 1, hRow + 1, 1).Merge();
                WpCell(ws, hRow, 1, "#", XLColor.FromHtml("#424242"), bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 1).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 2, hRow, 4).Merge();
                WpCell(ws, hRow, 2, "dbo_CREG", cregHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 2).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 5, hRow, 6).Merge();
                WpCell(ws, hRow, 5, "dbo_STUD", studHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 5).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 7, hRow + 1, 7).Merge();
                WpCell(ws, hRow, 7, "Results", XLColor.FromHtml("#37474F"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 7).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 8, hRow + 1, 8).Merge();
                WpCell(ws, hRow, 8, "Tickmarks", XLColor.FromHtml("#4A148C"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 8).Style.Font.FontColor = XLColor.White;

                foreach (var (col, lbl, bg) in new (int, string, XLColor)[]
                {
                    (2, "Student_ID (_007)", cregSub),
                    (3, "CREG_Code (_001)", cregSub),
                    (4, "Reg_Type (_064)", cregSub),
                    (5, "STUD_Code (_001)", studSub),
                    (6, "Reg_Type (_024)", studSub)
                })
                {
                    WpCell(ws, hRow + 1, col, lbl, bg, bold: true, wrapText: true,
                        halign: XLAlignmentHorizontalValues.Center);
                }

                ws.Row(hRow).Height     = 22;
                ws.Row(hRow + 1).Height = 40;
            }

            void SetColWidths(IXLWorksheet ws)
            {
                ws.Column(1).Width = 6;
                ws.Column(2).Width = 18;
                ws.Column(3).Width = 14;
                ws.Column(4).Width = 14;
                ws.Column(5).Width = 14;
                ws.Column(6).Width = 14;
                ws.Column(7).Width = 10;
                ws.Column(8).Width = 8;
            }

            void WriteDataRow(IXLWorksheet ws, int r,
                int no, string studentId, string? cregCode, string? cregRegType,
                string studCode, string studRegType,
                string result, bool isPass)
            {
                var rowBg   = isPass ? passRow : failRow;
                var tmColor = XLColor.FromHtml("#4A148C");

                WpCell(ws, r, 1, no,          rowBg, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r, 2, studentId,   rowBg);
                WpCell(ws, r, 3, cregCode,    rowBg);
                WpCell(ws, r, 4, cregRegType, rowBg);
                WpCell(ws, r, 5, studCode,    rowBg);
                WpCell(ws, r, 6, studRegType, rowBg);
                WpCell(ws, r, 7, result, rowBg, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(r, 7).Style.Font.FontColor = isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");

                if (isPass)
                {
                    WpCell(ws, r, 8, "a", passRow, bold: true, halign: XLAlignmentHorizontalValues.Center);
                    ws.Cell(r, 8).Style.Font.FontColor = tmColor;
                }
                else
                {
                    WpCell(ws, r, 8, null, failRow);
                }
                ws.Row(r).Height = 18;
            }

            void AppendProceduresAndTickmarks(IXLWorksheet ws, int startRow)
            {
                int r = startRow + 2;
                SectionBanner(ws, r, TOTAL_COLS, "Procedures");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a.\tAgreed the student's registration type (STUD._024) to the registration documentation (CREG._064) per the signed application.",
                    XLColor.FromHtml("#E3F2FD"), wrapText: true);
                ws.Row(r).Height = 18;
                r += 2;
                SectionBanner(ws, r, TOTAL_COLS, "Tickmark");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a\tAgreed STUD._024 = CREG._064 — registration type agrees with signed application and/or registration documentation.",
                    XLColor.FromHtml("#E3F2FD"), wrapText: true);
                ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#4A148C");
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
            }

            var wsResults = wb.Worksheets.Add("Working Paper");
            wsResults.Range(1, 1, 1, TOTAL_COLS).Merge();
            WpCell(wsResults, 1, 1, "HEMIS RULE 57 — REGISTRATION DOCUMENTATION AGREEMENT",
                XLColor.FromHtml("#1A237E"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsResults.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsResults.Cell(1, 1).Style.Font.FontSize  = 13;
            wsResults.Row(1).Height = 24;
            BuildHeader(wsResults, 2);
            SetColWidths(wsResults);

            int rowIdx = 4;
            foreach (var row in summary.ValidationRows)
            {
                WriteDataRow(wsResults, rowIdx,
                    row.ValidationNumber,
                    row.StudentId, row.CregCode, row.CregRegType,
                    row.StudCode, row.StudRegType,
                    row.ValidationResult, row.ValidationResult == "PASS");
                rowIdx++;
            }
            AppendProceduresAndTickmarks(wsResults, rowIdx);

            var wsEx = wb.Worksheets.Add("Exceptions");
            wsEx.Range(1, 1, 1, TOTAL_COLS + 1).Merge();
            WpCell(wsEx, 1, 1, "HEMIS RULE 57 — EXCEPTIONS (REGISTRATION DOCUMENTATION AGREEMENT)",
                XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsEx.Cell(1, 1).Style.Font.FontSize  = 13;
            wsEx.Row(1).Height = 24;
            BuildHeader(wsEx, 2);
            wsEx.Range(2, TOTAL_COLS + 1, 3, TOTAL_COLS + 1).Merge();
            WpCell(wsEx, 2, TOTAL_COLS + 1, "Exception Reason", XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(2, TOTAL_COLS + 1).Style.Font.FontColor = XLColor.White;
            SetColWidths(wsEx);
            wsEx.Column(TOTAL_COLS + 1).Width = 55;

            int exRow = 4;
            foreach (var ex in summary.Exceptions)
            {
                WriteDataRow(wsEx, exRow,
                    ex.ValidationNumber,
                    ex.StudentId, ex.CregCode, ex.CregRegType,
                    ex.StudCode, ex.StudRegType,
                    ex.ValidationResult, false);
                WpCell(wsEx, exRow, TOTAL_COLS + 1, ex.ExceptionReason, failRow, wrapText: true);
                wsEx.Row(exRow).Height = 18;
                exRow++;
            }
            AppendProceduresAndTickmarks(wsEx, exRow);

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 57: REGISTRATION DOCUMENTATION AGREEMENT", 2);
            var summaryData = new[]
            {
                ("Database",          summary.Database),
                ("STUD Table",        $"{summary.StudTable} (ID: {summary.StudIdCol}, Code: {summary.StudCodeCol}, Reg Type: {summary.StudRegTypeCol})"),
                ("CREG Table",        $"{summary.CregTable} (ID: {summary.CregIdCol}, Code: {summary.CregCodeCol}, Reg Type: {summary.CregRegTypeCol})"),
                ("CREG Filter (_064)", summary.CregRegTypeFilterValue),
                ("Validation Date",   summary.Timestamp),
                ("", ""),
                ("VALIDATION RESULTS", ""),
                ("Filtered Records",  summary.TotalValidated.ToString("N0")),
                ("PASS",              summary.PassCount.ToString("N0")),
                ("FAIL (Exception)",  summary.FailCount.ToString("N0")),
                ("Exception Rate",    $"{summary.ExceptionRate:F2}%"),
                ("Status",            summary.Status),
                ("", ""),
                ("VALIDATION RULE",   ""),
                ("Population",        $"dbo_CREG WHERE {summary.CregRegTypeCol} = '{summary.CregRegTypeFilterValue}'"),
                ("Join",              $"LEFT JOIN {summary.StudTable} on STUD.{summary.StudIdCol} = CREG.{summary.CregIdCol}"),
                ("PASS Condition",    $"STUD.{summary.StudRegTypeCol} = CREG.{summary.CregRegTypeCol}"),
                ("Description",       "Agrees to the student's signed application and/or registration documentation.")
            };

            int summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label is "VALIDATION RESULTS" or "VALIDATION RULE")
                {
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#1565C0"), bold: true);
                    wsSummary.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.White;
                }
                else if (label != "")
                {
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#F5F5F5"), bold: true);
                    WpCell(wsSummary, summaryRow, 2, value);
                    if (label == "Status")
                    {
                        var statusBg = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = statusBg;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }
                summaryRow++;
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 80;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule57Csv(Rule57ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            const string header = "Validation_Number,Student_ID,CREG_Code,CREG_Reg_Type,STUD_Code,STUD_Reg_Type,Validation_Result,Exception_Reason";

            sb.AppendLine(header);

            if (exceptionsOnly)
            {
                foreach (var ex in summary.Exceptions)
                {
                    sb.AppendLine(string.Join(",",
                        ex.ValidationNumber,
                        CsvEscape(ex.StudentId),
                        CsvEscape(ex.CregCode),
                        CsvEscape(ex.CregRegType),
                        CsvEscape(ex.StudCode),
                        CsvEscape(ex.StudRegType),
                        ex.ValidationResult,
                        CsvEscape(ex.ExceptionReason)));
                }
            }
            else
            {
                foreach (var row in summary.ValidationRows)
                {
                    sb.AppendLine(string.Join(",",
                        row.ValidationNumber,
                        CsvEscape(row.StudentId),
                        CsvEscape(row.CregCode),
                        CsvEscape(row.CregRegType),
                        CsvEscape(row.StudCode),
                        CsvEscape(row.StudRegType),
                        row.ValidationResult,
                        CsvEscape(row.ExceptionReason)));
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportRule58Excel(Rule58ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Working Paper");

            ws.Range(1, 1, 1, 6).Merge();
            var title = ws.Cell(1, 1);
            title.Value = "HEMIS RULE 58 — STAFF VALPAC DATA IN PRODUCTION";
            title.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A237E");
            title.Style.Font.FontColor = XLColor.White;
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 13;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(1).Height = 24;

            string[] headers = { "#", "Control Type", "Control Label", "Result", "Explanation", "Display Values" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(2, c + 1);
                cell.Value = headers[c];
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#37474F");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.WrapText = true;
            }
            ws.Row(2).Height = 22;

            var passRow = XLColor.FromHtml("#F3FFF3");
            var failRow = XLColor.FromHtml("#FFF3F3");

            int r = 3;
            foreach (var row in summary.ReviewRows)
            {
                bool isPass = row.ValidationResult == "PASS";
                var bg = isPass ? passRow : failRow;
                ws.Cell(r, 1).Value = row.ValidationNumber;
                ws.Cell(r, 2).Value = row.ControlType;
                ws.Cell(r, 3).Value = row.ControlLabel;
                ws.Cell(r, 4).Value = row.ValidationResult;
                ws.Cell(r, 5).Value = row.ValidationExplanation;
                ws.Cell(r, 6).Value = string.Join("; ", row.DisplayValues.Select(kv => $"{kv.Key}={kv.Value}"));
                for (int c = 1; c <= 6; c++)
                    ws.Cell(r, c).Style.Fill.BackgroundColor = bg;
                ws.Cell(r, 4).Style.Font.FontColor = isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");
                ws.Cell(r, 4).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
                r++;
            }

            ws.Column(1).Width = 6; ws.Column(2).Width = 18; ws.Column(3).Width = 24;
            ws.Column(4).Width = 10; ws.Column(5).Width = 50; ws.Column(6).Width = 40;

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 58: STAFF VALPAC DATA IN PRODUCTION", 2);
            var rows = new[]
            {
                ("Database",       summary.Database),
                ("VALPAC Table",   summary.ValpacTable),
                ("Production Table", summary.ProdTable),
                ("Validation Date", summary.Timestamp),
                ("",               ""),
                ("RESULTS",        ""),
                ("Total Validated", summary.TotalValidated.ToString("N0")),
                ("Pass",           summary.PassCount.ToString("N0")),
                ("Fail",           summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status",         summary.Status)
            };
            int sr = 2;
            foreach (var (lbl, val) in rows)
            {
                if (lbl == "RESULTS")
                {
                    wsSummary.Range(sr, 1, sr, 2).Merge();
                    var hdr = wsSummary.Cell(sr, 1);
                    hdr.Value = lbl; hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#37474F");
                    hdr.Style.Font.FontColor = XLColor.White; hdr.Style.Font.Bold = true;
                }
                else if (lbl != "")
                {
                    wsSummary.Cell(sr, 1).Value = lbl;
                    wsSummary.Cell(sr, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sr, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sr, 2).Value = val;
                    if (lbl == "Status")
                    {
                        wsSummary.Cell(sr, 2).Style.Fill.BackgroundColor = val == "PASS"
                            ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(sr, 2).Style.Font.Bold = true;
                    }
                }
                sr++;
            }
            wsSummary.Column(1).Width = 26; wsSummary.Column(2).Width = 60;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule58Csv(Rule58ValidationSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Validation_Number,Control_Type,Control_Label,Validation_Result,Explanation");
            foreach (var row in summary.ReviewRows)
            {
                sb.AppendLine(string.Join(",",
                    row.ValidationNumber,
                    CsvEscape(row.ControlType),
                    CsvEscape(row.ControlLabel),
                    row.ValidationResult,
                    CsvEscape(row.ValidationExplanation)));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportRule59Excel(Rule59ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            static string GetRule59Value(Rule59ValidationRowRecord row, string key) =>
                row.DisplayValues.TryGetValue(key, out var value) ? value ?? "" : "";

            var passRows = summary.ReviewRows
                .Where(row => string.Equals(row.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var failRows = summary.ReviewRows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var resultsSheet = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(resultsSheet, 1, "RULE 59 VALIDATION RESULTS", 5);

            var resultHeaders = new[]
            {
                "Validation #",
                $"{summary.ValpacCol037} (VALPAC)",
                $"{summary.ProdColPersonelNumber} (PRODUCTION)",
                "Validation Result",
                "Explanation"
            };

            for (int i = 0; i < resultHeaders.Length; i++)
            {
                var headerCell = resultsSheet.Cell(2, i + 1);
                headerCell.Value = resultHeaders[i];
                headerCell.Style.Font.Bold = true;
                headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                headerCell.Style.Font.FontColor = XLColor.White;
            }

            var currentRowIndex = 3;
            foreach (var reviewRow in summary.ReviewRows)
            {
                var isPassResult = string.Equals(reviewRow.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase);
                resultsSheet.Cell(currentRowIndex, 1).Value = reviewRow.ValidationNumber;
                resultsSheet.Cell(currentRowIndex, 2).Value = GetRule59Value(reviewRow, "VALPAC__037");
                resultsSheet.Cell(currentRowIndex, 3).Value = GetRule59Value(reviewRow, "PROD_PERSONEL_NUMBER");
                resultsSheet.Cell(currentRowIndex, 4).Value = reviewRow.ValidationResult;
                resultsSheet.Cell(currentRowIndex, 5).Value = reviewRow.ValidationExplanation;

                resultsSheet.Range(currentRowIndex, 1, currentRowIndex, 5).Style.Fill.BackgroundColor =
                    isPassResult ? XLColor.FromHtml("#F3FFF3") : XLColor.FromHtml("#FFF3F3");
                resultsSheet.Cell(currentRowIndex, 4).Style.Font.Bold = true;
                resultsSheet.Cell(currentRowIndex, 4).Style.Font.FontColor =
                    isPassResult ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");

                currentRowIndex++;
            }

            for (int c = 1; c <= 5; c++)
                resultsSheet.Column(c).AdjustToContents();

            var summarySheet59 = wb.Worksheets.Add("Summary");
            StyleHeaderRow(summarySheet59, 1, "HEMIS RULE 59: SFTE VALPAC DATA IN STAFF PRODUCTION", 2);

            var summaryData59 = new[]
            {
                ("Database", summary.Database),
                ("VALPAC Table", summary.ValpacTable),
                ("Production Table", summary.ProdTable),
                ("VALPAC Column", summary.ValpacCol037),
                ("Production Column", summary.ProdColPersonelNumber),
                ("Column Mapping", summary.TableLinkageText),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("VALIDATION RESULTS", ""),
                ("Total Validated", summary.TotalValidated.ToString("N0")),
                ("PASS (Found)", summary.PassCount.ToString("N0")),
                ("FAIL (Missing)", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRowIndex59 = 2;
            foreach (var (label, value) in summaryData59)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var summaryHeaderCell = summarySheet59.Cell(summaryRowIndex59, 1);
                    summaryHeaderCell.Value = label;
                    summaryHeaderCell.Style.Font.Bold = true;
                    summaryHeaderCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    summaryHeaderCell.Style.Font.FontColor = XLColor.White;
                    summarySheet59.Range(summaryRowIndex59, 1, summaryRowIndex59, 2).Merge();
                }
                else if (label != "")
                {
                    summarySheet59.Cell(summaryRowIndex59, 1).Value = label;
                    summarySheet59.Cell(summaryRowIndex59, 1).Style.Font.Bold = true;
                    summarySheet59.Cell(summaryRowIndex59, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    summarySheet59.Cell(summaryRowIndex59, 2).Value = value;

                    if (label == "Status")
                    {
                        summarySheet59.Cell(summaryRowIndex59, 2).Style.Fill.BackgroundColor =
                            string.Equals(value, "PASS", StringComparison.OrdinalIgnoreCase)
                                ? XLColor.FromHtml("#C8E6C9")
                                : XLColor.FromHtml("#FFCDD2");
                        summarySheet59.Cell(summaryRowIndex59, 2).Style.Font.Bold = true;
                    }
                }

                summaryRowIndex59++;
            }

            summarySheet59.Column(1).Width = 30;
            summarySheet59.Column(2).Width = 60;

            if (failRows.Any())
            {
                var exceptionsSheet59 = wb.Worksheets.Add("Exceptions");
                StyleHeaderRow(exceptionsSheet59, 1, "RULE 59 EXCEPTIONS - SFTE VALPAC NOT IN STAFF PRODUCTION", 5);

                for (int i = 0; i < resultHeaders.Length; i++)
                {
                    var exceptionHeaderCell = exceptionsSheet59.Cell(2, i + 1);
                    exceptionHeaderCell.Value = resultHeaders[i];
                    exceptionHeaderCell.Style.Font.Bold = true;
                    exceptionHeaderCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    exceptionHeaderCell.Style.Font.FontColor = XLColor.White;
                }

                var exceptionRowIndex59 = 3;
                foreach (var failReviewRow in failRows)
                {
                    exceptionsSheet59.Cell(exceptionRowIndex59, 1).Value = failReviewRow.ValidationNumber;
                    exceptionsSheet59.Cell(exceptionRowIndex59, 2).Value = GetRule59Value(failReviewRow, "VALPAC__037");
                    exceptionsSheet59.Cell(exceptionRowIndex59, 3).Value = GetRule59Value(failReviewRow, "PROD_PERSONEL_NUMBER");
                    exceptionsSheet59.Cell(exceptionRowIndex59, 4).Value = failReviewRow.ValidationResult;
                    exceptionsSheet59.Cell(exceptionRowIndex59, 5).Value = failReviewRow.ValidationExplanation;

                    exceptionsSheet59.Range(exceptionRowIndex59, 1, exceptionRowIndex59, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                    exceptionsSheet59.Cell(exceptionRowIndex59, 4).Style.Font.Bold = true;
                    exceptionsSheet59.Cell(exceptionRowIndex59, 4).Style.Font.FontColor = XLColor.FromHtml("#B71C1C");
                    exceptionRowIndex59++;
                }

                for (int c = 1; c <= 5; c++)
                    exceptionsSheet59.Column(c).AdjustToContents();
            }

            var statsSheet59 = wb.Worksheets.Add("Statistics");
            StyleHeaderRow(statsSheet59, 1, "VALIDATION STATISTICS", 2);

            var statsData59 = new[]
            {
                ("Total Records Validated", (object)summary.TotalValidated),
                ("PASS Count", (object)summary.PassCount),
                ("FAIL Count", (object)summary.FailCount),
                ("Exception Rate (%)", (object)(double)summary.ExceptionRate),
                ("Pass Rate (%)", (object)(summary.TotalValidated > 0
                    ? Math.Round((decimal)summary.PassCount / summary.TotalValidated * 100, 2)
                    : 0))
            };

            var statsRowIndex59 = 2;
            foreach (var (label, value) in statsData59)
            {
                statsSheet59.Cell(statsRowIndex59, 1).Value = label;
                statsSheet59.Cell(statsRowIndex59, 1).Style.Font.Bold = true;
                statsSheet59.Cell(statsRowIndex59, 2).SetValue(value?.ToString());
                statsRowIndex59++;
            }

            statsSheet59.Column(1).Width = 30;
            statsSheet59.Column(2).Width = 20;

            using var exportStream59 = new MemoryStream();
            wb.SaveAs(exportStream59);
            return exportStream59.ToArray();

            var ws = wb.Worksheets.Add("Working Paper");

            ws.Range(1, 1, 1, 6).Merge();
            var title = ws.Cell(1, 1);
            title.Value = "HEMIS RULE 59 — SFTE VALPAC DATA IN STAFF PRODUCTION";
            title.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A237E");
            title.Style.Font.FontColor = XLColor.White;
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 13;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(1).Height = 24;

            string[] headers = { "#", "Control Type", "Control Label", "Result", "Explanation", "Display Values" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(2, c + 1);
                cell.Value = headers[c];
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#37474F");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.WrapText = true;
            }
            ws.Row(2).Height = 22;

            var passRow = XLColor.FromHtml("#F3FFF3");
            var failRow = XLColor.FromHtml("#FFF3F3");

            int r = 3;
            foreach (var row in summary.ReviewRows)
            {
                bool isPass = row.ValidationResult == "PASS";
                var bg = isPass ? passRow : failRow;
                ws.Cell(r, 1).Value = row.ValidationNumber;
                ws.Cell(r, 2).Value = row.ControlType;
                ws.Cell(r, 3).Value = row.ControlLabel;
                ws.Cell(r, 4).Value = row.ValidationResult;
                ws.Cell(r, 5).Value = row.ValidationExplanation;
                ws.Cell(r, 6).Value = string.Join("; ", row.DisplayValues.Select(kv => $"{kv.Key}={kv.Value}"));
                for (int c = 1; c <= 6; c++)
                    ws.Cell(r, c).Style.Fill.BackgroundColor = bg;
                ws.Cell(r, 4).Style.Font.FontColor = isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");
                ws.Cell(r, 4).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
                r++;
            }

            ws.Column(1).Width = 6; ws.Column(2).Width = 18; ws.Column(3).Width = 24;
            ws.Column(4).Width = 10; ws.Column(5).Width = 50; ws.Column(6).Width = 40;

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 59: SFTE VALPAC DATA IN STAFF PRODUCTION", 2);
            var rows = new[]
            {
                ("Database",         summary.Database),
                ("VALPAC Table",     summary.ValpacTable),
                ("Production Table", summary.ProdTable),
                ("Column Mapping",   summary.TableLinkageText),
                ("Validation Date",  summary.Timestamp),
                ("", ""),
                ("RESULTS",          ""),
                ("Total Validated",  summary.TotalValidated.ToString("N0")),
                ("Pass",             summary.PassCount.ToString("N0")),
                ("Fail",             summary.FailCount.ToString("N0")),
                ("Exception Rate",   $"{summary.ExceptionRate:F2}%"),
                ("Status",           summary.Status)
            };
            int sr = 2;
            foreach (var (lbl, val) in rows)
            {
                if (lbl == "RESULTS")
                {
                    wsSummary.Range(sr, 1, sr, 2).Merge();
                    var hdr = wsSummary.Cell(sr, 1);
                    hdr.Value = lbl; hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#37474F");
                    hdr.Style.Font.FontColor = XLColor.White; hdr.Style.Font.Bold = true;
                }
                else if (lbl != "")
                {
                    wsSummary.Cell(sr, 1).Value = lbl;
                    wsSummary.Cell(sr, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sr, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sr, 2).Value = val;
                    if (lbl == "Status")
                    {
                        wsSummary.Cell(sr, 2).Style.Fill.BackgroundColor = val == "PASS"
                            ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(sr, 2).Style.Font.Bold = true;
                    }
                }
                sr++;
            }
            wsSummary.Column(1).Width = 26; wsSummary.Column(2).Width = 60;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule59Csv(Rule59ValidationSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Validation_Number,Control_Type,Control_Label,Validation_Result,Explanation");
            foreach (var row in summary.ReviewRows)
            {
                sb.AppendLine(string.Join(",",
                    row.ValidationNumber,
                    CsvEscape(row.ControlType),
                    CsvEscape(row.ControlLabel),
                    row.ValidationResult,
                    CsvEscape(row.ValidationExplanation)));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportRule60Excel(Rule41ValidationSummary summary)
        {
            using var workbook = new XLWorkbook();

            var summarySheet = workbook.Worksheets.Add("Summary");
            summarySheet.Cell(1, 1).Value = "HEMIS RULE 60 - CRSE vs H16CRSE Agreement";
            summarySheet.Range(1, 1, 1, 2).Merge();
            summarySheet.Cell(1, 1).Style.Font.Bold = true;
            summarySheet.Cell(1, 1).Style.Font.FontSize = 14;

            var summaryRows = new (string Label, string Value)[]
            {
                ("Database", summary.Database),
                ("Timestamp", summary.Timestamp),
                ("Status", summary.Status),
                ("CRSE Table", summary.Reconc.StudTable),
                ("H16CRSE Table", summary.Reconc.AuditTable),
                ("CRSE Join Key", summary.Reconc.StudKey),
                ("H16CRSE Join Key", summary.Reconc.AuditKey),
                ("Total Count", summary.Reconc.TotalCount.ToString()),
                ("Agree Count", summary.Reconc.AgreeCount.ToString()),
                ("Disagree Count", summary.Reconc.DisagreeCount.ToString()),
                ("Missing Count", summary.Reconc.MissingCount.ToString()),
                ("Exception Rate", $"{summary.Reconc.ExceptionRate:F2}%")
            };

            summarySheet.Cell(3, 1).Value = "Field";
            summarySheet.Cell(3, 2).Value = "Value";
            summarySheet.Range(3, 1, 3, 2).Style.Font.Bold = true;
            summarySheet.Range(3, 1, 3, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");

            var summaryRowIndex = 4;
            foreach (var item in summaryRows)
            {
                summarySheet.Cell(summaryRowIndex, 1).Value = item.Label;
                summarySheet.Cell(summaryRowIndex, 2).Value = item.Value;
                summaryRowIndex++;
            }

            var mappingStartRow = summaryRowIndex + 2;
            summarySheet.Cell(mappingStartRow, 1).Value = "Field Mappings";
            summarySheet.Cell(mappingStartRow, 1).Style.Font.Bold = true;
            summarySheet.Cell(mappingStartRow + 1, 1).Value = "Label";
            summarySheet.Cell(mappingStartRow + 1, 2).Value = "CRSE Column";
            summarySheet.Cell(mappingStartRow + 1, 3).Value = "H16CRSE Column";
            summarySheet.Range(mappingStartRow + 1, 1, mappingStartRow + 1, 3).Style.Font.Bold = true;
            summarySheet.Range(mappingStartRow + 1, 1, mappingStartRow + 1, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");

            var mappingRowIndex = mappingStartRow + 2;
            foreach (var pair in summary.Reconc.Pairs)
            {
                summarySheet.Cell(mappingRowIndex, 1).Value = pair.Label;
                summarySheet.Cell(mappingRowIndex, 2).Value = pair.StudCol;
                summarySheet.Cell(mappingRowIndex, 3).Value = pair.AuditCol;
                mappingRowIndex++;
            }

            summarySheet.Columns().AdjustToContents();

            WriteRule60ReconciliationWorksheet(
                workbook,
                "All Results",
                summary.Reconc,
                summary.Reconc.ExceptionRows.Concat(summary.Reconc.Rows));
            WriteRule60ReconciliationWorksheet(
                workbook,
                "Exceptions",
                summary.Reconc,
                summary.Reconc.ExceptionRows);

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteRule60ReconciliationWorksheet(
            XLWorkbook workbook,
            string sheetName,
            Rule41ReconciliationSummary reconc,
            IEnumerable<Rule41ReconcRow> rows)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);
            worksheet.Cell(1, 1).Value = "CRSE vs H16CRSE Reconciliation";
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 13;

            worksheet.Cell(2, 1).Value = "CRSE Table";
            worksheet.Cell(2, 2).Value = reconc.StudTable;
            worksheet.Cell(2, 3).Value = "H16CRSE Table";
            worksheet.Cell(2, 4).Value = reconc.AuditTable;

            worksheet.Cell(3, 1).Value = "Total";
            worksheet.Cell(3, 2).Value = reconc.TotalCount;
            worksheet.Cell(3, 3).Value = "Agree";
            worksheet.Cell(3, 4).Value = reconc.AgreeCount;
            worksheet.Cell(3, 5).Value = "Disagree";
            worksheet.Cell(3, 6).Value = reconc.DisagreeCount;
            worksheet.Cell(3, 7).Value = "Missing";
            worksheet.Cell(3, 8).Value = reconc.MissingCount;
            worksheet.Cell(3, 9).Value = "Exception Rate";
            worksheet.Cell(3, 10).Value = $"{reconc.ExceptionRate:F2}%";

            var headerRow = 5;
            var columnIndex = 1;
            worksheet.Cell(headerRow, columnIndex++).Value = "Row No";
            worksheet.Cell(headerRow, columnIndex++).Value = "CRSE Ref";

            foreach (var pair in reconc.Pairs)
            {
                worksheet.Cell(headerRow, columnIndex++).Value = $"CRSE_{pair.Label}";
                worksheet.Cell(headerRow, columnIndex++).Value = $"H16CRSE_{pair.Label}";
                worksheet.Cell(headerRow, columnIndex++).Value = $"MATCH_{pair.Label}";
            }

            worksheet.Cell(headerRow, columnIndex++).Value = "Overall Result";
            worksheet.Cell(headerRow, columnIndex).Value = "Disagree Detail";

            worksheet.Range(headerRow, 1, headerRow, columnIndex).Style.Font.Bold = true;
            worksheet.Range(headerRow, 1, headerRow, columnIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");

            var rowIndex = headerRow + 1;
            foreach (var row in rows)
            {
                var cellIndex = 1;
                worksheet.Cell(rowIndex, cellIndex++).Value = row.RowNumber;
                worksheet.Cell(rowIndex, cellIndex++).Value = row.StudentRef;

                foreach (var pair in reconc.Pairs)
                {
                    if (row.Fields.TryGetValue(pair.Label, out var field))
                    {
                        worksheet.Cell(rowIndex, cellIndex++).Value = field.StudValue;
                        worksheet.Cell(rowIndex, cellIndex++).Value = field.AuditValue;
                        worksheet.Cell(rowIndex, cellIndex++).Value = field.Match;
                    }
                    else
                    {
                        worksheet.Cell(rowIndex, cellIndex++).Value = "-";
                        worksheet.Cell(rowIndex, cellIndex++).Value = "-";
                        worksheet.Cell(rowIndex, cellIndex++).Value = "-";
                    }
                }

                worksheet.Cell(rowIndex, cellIndex++).Value = row.OverallResult;
                worksheet.Cell(rowIndex, cellIndex).Value = row.DisagreeDetail;
                rowIndex++;
            }

            worksheet.SheetView.FreezeRows(headerRow);
            worksheet.Range(headerRow, 1, Math.Max(headerRow, rowIndex - 1), columnIndex).SetAutoFilter();
            worksheet.Columns().AdjustToContents();
        }
    }
}

