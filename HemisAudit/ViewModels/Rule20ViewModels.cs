using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule20ColumnMapping
    {
        // STUD table column roles
        public string StudStudentNo { get; set; } = "_007";
        public string StudColumn008 { get; set; } = "_008";
        public string StudColumn066 { get; set; } = "_066";
        public string StudColumn067 { get; set; } = "_067";
        public string StudColumn068 { get; set; } = "_068";
        public string StudColumn012 { get; set; } = "_012";
        public string StudColumn013 { get; set; } = "_013";
        public string StudColumn014 { get; set; } = "_014";
        public string StudColumn015 { get; set; } = "_015";
        public string StudColumn010 { get; set; } = "_010";
        public string StudColumn026 { get; set; } = "_026";
        public string StudColumn025 { get; set; } = "_025";
        public string StudQualCode { get; set; } = "_001";
        public string StudName { get; set; } = "_019";
        public string StudIdNo { get; set; } = "_024";
        public string StudFoundationFlag { get; set; } = "_106";
        public string StudFoundationValue { get; set; } = "Y";
        // QUAL table column roles
        public string QualQualCode { get; set; } = "_001";
        public string QualDescription { get; set; } = "_003";
        public string QualType { get; set; } = "_005";
        // CRED/bridge table column roles
        public string CregQualCode { get; set; } = "_001";
        public string CregCourseCode { get; set; } = "_030";
        // CRSE table column roles
        public string CrseCourseCode { get; set; } = "_030";
        public string CrseCourseName { get; set; } = "_058";
        public string CrseFoundationFlag { get; set; } = "_091";
        public string CrseFoundationValue { get; set; } = "Y";
    }

    public class Rule20ColumnDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule20TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? AutoCregTable { get; set; }
        public string? AutoCrseTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule20VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_CRED";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string PgTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
        public Rule20ColumnMapping ColumnMapping { get; set; } = new();
    }

    public class Rule20VerifyResult
    {
        public bool Success { get; set; }
        public int FoundationStudentCount { get; set; }
        public int ValidatedRowCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule20ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_CRED";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string PgTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
        public Rule20ColumnMapping ColumnMapping { get; set; } = new();
    }

    public class Rule20PartSummaryItemViewModel
    {
        public string PartCode { get; set; } = "";
        public string PartTitle { get; set; } = "";
        public string PartDescription { get; set; } = "";
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string Status { get; set; } = "";
    }

    public class Rule20ReviewRowViewModel
    {
        public int ValidationNumber { get; set; }
        public string PartCode { get; set; } = "";
        public string PartTitle { get; set; } = "";
        public string PartDescription { get; set; } = "";
        public string StudentNumber007 { get; set; } = "";
        public string StudentColumn008 { get; set; } = "";
        public string StudentColumn066 { get; set; } = "";
        public string StudentColumn067 { get; set; } = "";
        public string StudentColumn068 { get; set; } = "";
        public string StudentColumn012 { get; set; } = "";
        public string StudentColumn013 { get; set; } = "";
        public string StudentColumn014 { get; set; } = "";
        public string StudentColumn015 { get; set; } = "";
        public string StudentColumn010 { get; set; } = "";
        public string StudentColumn026 { get; set; } = "";
        public string StudentColumn025 { get; set; } = "";
        public string QualificationCode001 { get; set; } = "";
        public string Name019 { get; set; } = "";
        public string IdNumber024 { get; set; } = "";
        public string FoundationFlag106 { get; set; } = "";
        public string QualificationDescription003 { get; set; } = "";
        public string QualificationType005 { get; set; } = "";
        public string BridgeQualificationCode001 { get; set; } = "";
        public string CourseCode030 { get; set; } = "";
        public string CourseName058 { get; set; } = "";
        public string CrseCourseCode030 { get; set; } = "";
        public string FoundationCourse091 { get; set; } = "";
        public string StudentType { get; set; } = "";
        public string NotebookStatus { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
    }

    public class Rule20ValidationSummary
    {
        public bool Success { get; set; }
        public int FoundationStudentCount { get; set; }
        public int TotalValidated { get; set; }
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
        public string CregTable { get; set; } = "dbo_CRED";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public Rule20ColumnMapping ColumnMapping { get; set; } = new();
        public string PgTypesText { get; set; } = "";
        public List<string> PgTypes { get; set; } = new();
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
        public string GoverningPartCodesText { get; set; } = "All Students";
        public string OverallStatusRuleText { get; set; } = "Overall PASS requires the filtered all-student population to PASS.";
        public string TableLinkageText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule20PartSummaryItemViewModel> PartSummaries { get; set; } = new();
        public List<Rule20ReviewRowViewModel> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule20RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule20ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule20WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_CRED";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public Rule20ColumnMapping ColumnMapping { get; set; } = new();
        public string PgTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule20ValidationSummary? Summary { get; set; }
    }

    public class Rule20WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule20WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule20RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule20WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule20SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }

    public class Rule20GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
    }
}


