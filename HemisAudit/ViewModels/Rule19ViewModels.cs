using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule19GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
    }

    public class Rule19TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? AutoPqmTable { get; set; }
        public string? AutoCredTable { get; set; }
        public string? AutoCrseTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule19ColumnSelectionResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoQualCodeColumn { get; set; }
        public string? AutoFulfilledColumn { get; set; }
        public string? AutoQualTypeColumn { get; set; }
        public string? AutoQualNameColumn { get; set; }
        public string? AutoPqmQualNameColumn { get; set; }
        public string? AutoPqmQualTypeColumn { get; set; }
        public string? Error { get; set; }
    }

    public class Rule19FilterValueRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string StudTable { get; set; } = "";
        public string FulfilledColumn { get; set; } = "";
    }

    public class Rule19FilterValueOption
    {
        public string Value { get; set; } = "";
        public int Count { get; set; }
        public string Label { get; set; } = "";
    }

    public class Rule19FilterValueResult
    {
        public bool Success { get; set; }
        public List<Rule19FilterValueOption> Options { get; set; } = new();
        public string? DefaultValue { get; set; }
        public string? Error { get; set; }
    }

    public class Rule19VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string QualCodeColumn { get; set; } = "_001";
        public string FulfilledColumn { get; set; } = "_025";
        public string FulfilledValue { get; set; } = "F";
        public string QualTypeColumn { get; set; } = "_005";
        public string MdTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public string QualNameColumn { get; set; } = "_003";
        public string PqmTable { get; set; } = "";
        public string PqmQualNameColumn { get; set; } = "";
        public string PqmQualTypeColumn { get; set; } = "";
        public string CredTable { get; set; } = "";
        public string CredJoinCol { get; set; } = "_001";
        public string CredCourseCol { get; set; } = "_030";
        public string CrseTable { get; set; } = "";
        public string CrseCourseCol { get; set; } = "_030";
    }

    public class Rule19VerifyResult
    {
        public bool Success { get; set; }
        public int StudRecordCount { get; set; }
        public int QualRecordCount { get; set; }
        public int CredRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
        public int EligiblePopulationCount { get; set; }
        public List<Rule19ValidationRowRecord> PreviewRows { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule19ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string QualCodeColumn { get; set; } = "_001";
        public string FulfilledColumn { get; set; } = "_025";
        public string FulfilledValue { get; set; } = "F";
        public string QualTypeColumn { get; set; } = "_005";
        public string MdTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public string QualNameColumn { get; set; } = "_003";
        public bool ShowAllRecords { get; set; } = true;
        public string PqmTable { get; set; } = "";
        public string PqmQualNameColumn { get; set; } = "";
        public string PqmQualTypeColumn { get; set; } = "";
        public string CredTable { get; set; } = "";
        public string CredJoinCol { get; set; } = "_001";
        public string CredCourseCol { get; set; } = "_030";
        public string CrseTable { get; set; } = "";
        public string CrseCourseCol { get; set; } = "_030";
    }

    public class Rule19BreakdownItemViewModel
    {
        public string Value { get; set; } = "";
        public int Count { get; set; }
    }

    public class Rule19ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string QualCodeValue { get; set; } = "";
        public string FulfilledValue { get; set; } = "";
        public string QualTypeValue { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string? PqmQualName { get; set; }
        public string? PqmQualType { get; set; }
        public bool PqmNameMatch { get; set; }
        public bool PqmTypeMatch { get; set; }
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule19ValidationSummary
    {
        public bool Success { get; set; }
        public int StudRecordCount { get; set; }
        public int QualRecordCount { get; set; }
        public int TotalValidated { get; set; }
        public int MatchingCount { get; set; }
        public int DisplayedCount { get; set; }
        public bool IsPreviewOnly { get; set; }
        public int PreviewLimit { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string QualCodeColumn { get; set; } = "_001";
        public string FulfilledColumn { get; set; } = "_025";
        public string FulfilledValue { get; set; } = "F";
        public string QualTypeColumn { get; set; } = "_005";
        public string MdTypesText { get; set; } = "";
        public string QualNameColumn { get; set; } = "_003";
        public List<string> MdTypes { get; set; } = new();
        public string TableLinkageText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public bool ShowAllRecords { get; set; } = true;
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public string PqmTable { get; set; } = "";
        public string PqmQualNameColumn { get; set; } = "";
        public string PqmQualTypeColumn { get; set; } = "";
        public string CredTable { get; set; } = "";
        public string CredJoinCol { get; set; } = "_001";
        public string CredCourseCol { get; set; } = "_030";
        public string CrseTable { get; set; } = "";
        public string CrseCourseCol { get; set; } = "_030";
        public int CredRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
        public List<Rule19BreakdownItemViewModel> Breakdown { get; set; } = new();
        public List<Rule19ValidationRowRecord> MatchingRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule19RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule19ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule19WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string QualCodeColumn { get; set; } = "_001";
        public string FulfilledColumn { get; set; } = "_025";
        public string FulfilledValue { get; set; } = "F";
        public string QualTypeColumn { get; set; } = "_005";
        public string MdTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public string QualNameColumn { get; set; } = "_003";
        public string PqmTable { get; set; } = "";
        public string PqmQualNameColumn { get; set; } = "";
        public string PqmQualTypeColumn { get; set; } = "";
        public string CredTable { get; set; } = "";
        public string CredJoinCol { get; set; } = "_001";
        public string CredCourseCol { get; set; } = "_030";
        public string CrseTable { get; set; } = "";
        public string CrseCourseCol { get; set; } = "_030";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule19ValidationSummary? Summary { get; set; }
    }

    public class Rule19WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule19WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule19RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule19WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule19SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}


