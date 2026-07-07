using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule22TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoProfTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule22ColumnRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string ProfTable { get; set; } = "";
    }

    public class Rule22ColumnResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoColumn037 { get; set; }
        public string? AutoColumn038 { get; set; }
        public string? AutoColumn039 { get; set; }
        public string? AutoColumn040 { get; set; }
        public string? AutoColumn011 { get; set; }
        public string? AutoColumn012 { get; set; }
        public string? AutoColumn013 { get; set; }
        public string? AutoColumn014 { get; set; }
        public string? AutoColumn041 { get; set; }
        public string? AutoColumn042 { get; set; }
        public string? AutoColumn046 { get; set; }
        public string? AutoColumn047 { get; set; }
        public string? AutoColumn048 { get; set; }
        public string? AutoColumn094 { get; set; }
        public string? Error { get; set; }
    }

    public class Rule22VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string ProfTable { get; set; } = "";
        public string Column037 { get; set; } = "_037";
        public string Column038 { get; set; } = "_038";
        public string Column039 { get; set; } = "_039";
        public string Column040 { get; set; } = "_040";
        public string Column011 { get; set; } = "_011";
        public string Column012 { get; set; } = "_012";
        public string Column013 { get; set; } = "_013";
        public string Column014 { get; set; } = "_014";
        public string Column041 { get; set; } = "_041";
        public string Column042 { get; set; } = "_042";
        public string Column046 { get; set; } = "_046";
        public string Column047 { get; set; } = "_047";
        public string Column048 { get; set; } = "_048";
        public string Column094 { get; set; } = "_094";
        public string FilterValue041 { get; set; } = "PE";
        public string FilterValue039 { get; set; } = "01";
        public int Control1SampleSize { get; set; }
        public int Control2SampleSize { get; set; }
        public int Control3SampleSize { get; set; }
    }

    public class Rule22VerifyResult
    {
        public bool Success { get; set; }
        public int TotalCount { get; set; }
        public int Control1Count { get; set; }
        public int Control2Count { get; set; }
        public int Control3Count { get; set; }
        public int UnclassifiedCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule22ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ProfTable { get; set; } = "";
        public string Column037 { get; set; } = "_037";
        public string Column038 { get; set; } = "_038";
        public string Column039 { get; set; } = "_039";
        public string Column040 { get; set; } = "_040";
        public string Column011 { get; set; } = "_011";
        public string Column012 { get; set; } = "_012";
        public string Column013 { get; set; } = "_013";
        public string Column014 { get; set; } = "_014";
        public string Column041 { get; set; } = "_041";
        public string Column042 { get; set; } = "_042";
        public string Column046 { get; set; } = "_046";
        public string Column047 { get; set; } = "_047";
        public string Column048 { get; set; } = "_048";
        public string Column094 { get; set; } = "_094";
        public string FilterValue041 { get; set; } = "PE";
        public string FilterValue039 { get; set; } = "01";
        public int Control1SampleSize { get; set; }
        public int Control2SampleSize { get; set; }
        public int Control3SampleSize { get; set; }
    }

    public class Rule22ControlSummaryItemViewModel
    {
        public string ControlType { get; set; } = "";
        public string ControlDefinition { get; set; } = "";
        public int AvailableCount { get; set; }
        public int RequestedCount { get; set; }
        public int SampleCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
    }

    public class Rule22ReviewRowViewModel
    {
        public int ValidationNumber { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlDefinition { get; set; } = "";
        public int SampleNumber { get; set; }
        public string StaffNumber037 { get; set; } = "";
        public string Year038 { get; set; } = "";
        public string Col011 { get; set; } = "";
        public string Col012 { get; set; } = "";
        public string Col013 { get; set; } = "";
        public string Col039 { get; set; } = "";
        public string Col040 { get; set; } = "";
        public string Col014 { get; set; } = "";
        public string Col041 { get; set; } = "";
        public string Col042 { get; set; } = "";
        public string Col046 { get; set; } = "";
        public string Col047 { get; set; } = "";
        public string Col048 { get; set; } = "";
        public string Col094 { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ExceptionReason { get; set; } = "";
    }

    public class Rule22MappedColumnViewModel
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public string ValueField { get; set; } = "";
    }

    public static class Rule22ColumnMappingHelper
    {
        public static List<Rule22MappedColumnViewModel> Build(
            string? column037,
            string? column038,
            string? column039,
            string? column040,
            string? column011,
            string? column012,
            string? column013,
            string? column014,
            string? column041,
            string? column042,
            string? column046,
            string? column047,
            string? column048,
            string? column094) =>
            new()
            {
                Create("column037", "Staff_Number",                 column037, "_037", "staffNumber037"),
                Create("column038", "Employment_Commencement_Year", column038, "_038", "year038"),
                Create("column039", "Personnel_Category",           column039, "_039", "col039"),
                Create("column040", "Rank_of_Staff_Member",         column040, "_040", "col040"),
                Create("column011", "Date_of_Birth",                column011, "_011", "col011"),
                Create("column012", "Gender",                       column012, "_012", "col012"),
                Create("column013", "Race",                         column013, "_013", "col013"),
                Create("column014", "Nationality",                  column014, "_014", "col014"),
                Create("column041", "Permanent_Temporary_Status",   column041, "_041", "col041"),
                Create("column042", "Full_Time_Part_Time_Status",   column042, "_042", "col042"),
                Create("column046", "Staff_Qualification",          column046, "_046", "col046"),
                Create("column047", "Joint_Appointment",            column047, "_047", "col047"),
                Create("column048", "On_Payroll_Code",              column048, "_048", "col048"),
                Create("column094", "Research_Fellow",              column094, "_094", "col094")
            };

        public static List<Rule22MappedColumnViewModel> Build(Rule22ValidationRequest request) =>
            Build(
                request.Column037,
                request.Column038,
                request.Column039,
                request.Column040,
                request.Column011,
                request.Column012,
                request.Column013,
                request.Column014,
                request.Column041,
                request.Column042,
                request.Column046,
                request.Column047,
                request.Column048,
                request.Column094);

        public static List<Rule22MappedColumnViewModel> Build(Rule22ValidationSummary summary) =>
            Build(
                summary.Column037,
                summary.Column038,
                summary.Column039,
                summary.Column040,
                summary.Column011,
                summary.Column012,
                summary.Column013,
                summary.Column014,
                summary.Column041,
                summary.Column042,
                summary.Column046,
                summary.Column047,
                summary.Column048,
                summary.Column094);

        public static IEnumerable<string> GetSelectedColumns(Rule22VerifyRequest request) =>
            Build(
                request.Column037,
                request.Column038,
                request.Column039,
                request.Column040,
                request.Column011,
                request.Column012,
                request.Column013,
                request.Column014,
                request.Column041,
                request.Column042,
                request.Column046,
                request.Column047,
                request.Column048,
                request.Column094)
            .Select(item => item.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        public static string GetRowValue(Rule22ReviewRowViewModel row, string? valueField) =>
            valueField switch
            {
                "staffNumber037" => row.StaffNumber037,
                "year038"        => row.Year038,
                "col039"         => row.Col039,
                "col040"         => row.Col040,
                "col011"         => row.Col011,
                "col012"         => row.Col012,
                "col013"         => row.Col013,
                "col014"         => row.Col014,
                "col041"         => row.Col041,
                "col042"         => row.Col042,
                "col046"         => row.Col046,
                "col047"         => row.Col047,
                "col048"         => row.Col048,
                "col094"         => row.Col094,
                _ => ""
            };

        public static string Normalize(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static Rule22MappedColumnViewModel Create(string key, string label, string? columnName, string fallback, string valueField) =>
            new()
            {
                Key = key,
                Label = label,
                ColumnName = Normalize(columnName, fallback),
                ValueField = valueField
            };
    }

    public class Rule22ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string ProfTable { get; set; } = "";
        public string Column037 { get; set; } = "_037";
        public string Column038 { get; set; } = "_038";
        public string Column039 { get; set; } = "_039";
        public string Column040 { get; set; } = "_040";
        public string Column011 { get; set; } = "_011";
        public string Column012 { get; set; } = "_012";
        public string Column013 { get; set; } = "_013";
        public string Column014 { get; set; } = "_014";
        public string Column041 { get; set; } = "_041";
        public string Column042 { get; set; } = "_042";
        public string Column046 { get; set; } = "_046";
        public string Column047 { get; set; } = "_047";
        public string Column048 { get; set; } = "_048";
        public string Column094 { get; set; } = "_094";
        public string FilterValue041 { get; set; } = "PE";
        public string FilterValue039 { get; set; } = "01";
        public int Control1SampleSize { get; set; }
        public int Control2SampleSize { get; set; }
        public int Control3SampleSize { get; set; }
        public int Control1Count { get; set; }
        public int Control2Count { get; set; }
        public int Control3Count { get; set; }
        public int UnclassifiedCount { get; set; }
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule22ControlSummaryItemViewModel> ControlSummaries { get; set; } = new();
        public List<Rule22MappedColumnViewModel> MappedColumns { get; set; } = new();
        public List<Rule22ReviewRowViewModel> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule22RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule22ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule22WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ProfTable { get; set; } = "";
        public string Column037 { get; set; } = "_037";
        public string Column038 { get; set; } = "_038";
        public string Column039 { get; set; } = "_039";
        public string Column040 { get; set; } = "_040";
        public string Column011 { get; set; } = "_011";
        public string Column012 { get; set; } = "_012";
        public string Column013 { get; set; } = "_013";
        public string Column014 { get; set; } = "_014";
        public string Column041 { get; set; } = "_041";
        public string Column042 { get; set; } = "_042";
        public string Column046 { get; set; } = "_046";
        public string Column047 { get; set; } = "_047";
        public string Column048 { get; set; } = "_048";
        public string Column094 { get; set; } = "_094";
        public string FilterValue041 { get; set; } = "PE";
        public string FilterValue039 { get; set; } = "01";
        public int Control1SampleSize { get; set; }
        public int Control2SampleSize { get; set; }
        public int Control3SampleSize { get; set; }
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule22ValidationSummary? Summary { get; set; }
    }

    public class Rule22WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule22WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule22RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule22WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule22SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}


