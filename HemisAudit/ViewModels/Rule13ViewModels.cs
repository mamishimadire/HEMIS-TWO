using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule13TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? AutoCregTable { get; set; }
        public string? AutoCrseTable { get; set; }
        public string? AutoPqmTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule13VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_CESM";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_STUD";
        public string CrseTable { get; set; } = "dbo_STUD";
        public string PgTypesText { get; set; } = "";
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
        public string CesmLinkCol { get; set; } = "_001";
        public string QualLinkCol { get; set; } = "_001";
        public string StudLinkCol { get; set; } = "_001";
        public string CesmFilterCol { get; set; } = "_006";
        public string CesmFilterVal { get; set; } = "ZZZZZZ";
        public string PqmTable { get; set; } = "";
        public string CesmIdCol { get; set; } = "_001";
        public string CesmCodeCol { get; set; } = "_006";
        public string QualIdCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string StudIdCol { get; set; } = "_001";
        public string PqmNameCol { get; set; } = "Authorised_Qualification_Name";
        public string PqmCode1Col { get; set; } = "CESM_Code";
        public string PqmCode2Col { get; set; } = "CESM_Code2";
    }

    public class Rule13VerifyResult
    {
        public bool Success { get; set; }
        public int FoundationStudentCount { get; set; }
        public int ValidatedRowCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule13ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_CESM";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_STUD";
        public string CrseTable { get; set; } = "dbo_STUD";
        public string PgTypesText { get; set; } = "";
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
        public string PqmTable { get; set; } = "";
        public string CesmIdCol { get; set; } = "_001";
        public string CesmCodeCol { get; set; } = "_006";
        public string QualIdCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string StudIdCol { get; set; } = "_001";
        public string PqmNameCol { get; set; } = "Authorised_Qualification_Name";
        public string PqmCode1Col { get; set; } = "CESM_Code";
        public string PqmCode2Col { get; set; } = "CESM_Code2";
    }

    public class Rule13PartSummaryItemViewModel
    {
        public string PartCode { get; set; } = "";
        public string PartTitle { get; set; } = "";
        public string PartDescription { get; set; } = "";
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string Status { get; set; } = "";
    }

    public class Rule13ReviewRowViewModel
    {
        public int ValidationNumber { get; set; }
        public string PartCode { get; set; } = "";
        public string PartTitle { get; set; } = "";
        public string PartDescription { get; set; } = "";
        public string StudentNumber007 { get; set; } = "";
        public string QualificationCode001 { get; set; } = "";
        public string Name019 { get; set; } = "";
        public string IdNumber024 { get; set; } = "";
        public string FoundationFlag106 { get; set; } = "";
        public string QualificationDescription003 { get; set; } = "";
        public string QualificationType005 { get; set; } = "";
        public string BridgeQualificationCode001 { get; set; } = "";
        public string CourseCode030 { get; set; } = "";
        public string CrseCourseCode030 { get; set; } = "";
        public string FoundationCourse091 { get; set; } = "";
        public string StudentType { get; set; } = "";
        public int StudLinkCount { get; set; }
        public string StudLinkResult { get; set; } = "";
        public string NotebookStatus { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
        public string PqmCode { get; set; } = "";
        public string PqmName { get; set; } = "";
        public bool PqmCodeMatch { get; set; }
        public bool PqmNameMatch { get; set; }
        public bool PqmNeedsReview { get; set; }
        public string PqmResult { get; set; } = "";
        public string PqmExceptionReason { get; set; } = "";
    }

    public class Rule13ValidationSummary
    {
        public bool Success { get; set; }
        public int FoundationStudentCount { get; set; }
        public int TotalValidated { get; set; }
        public int DisplayedCount { get; set; }
        public bool IsPreviewOnly { get; set; }
        public int PreviewLimit { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int ReviewCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string StudTable { get; set; } = "dbo_CESM";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_STUD";
        public string CrseTable { get; set; } = "dbo_STUD";
        public string PgTypesText { get; set; } = "";
        public List<string> PgTypes { get; set; } = new();
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
        public string GoverningPartCodesText { get; set; } = "100% population";
        public string OverallStatusRuleText { get; set; } = "";
        public string TableLinkageText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule13PartSummaryItemViewModel> PartSummaries { get; set; } = new();
        public List<Rule13ReviewRowViewModel> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
        public string PqmTable { get; set; } = "";
        public string CesmIdCol { get; set; } = "_001";
        public string CesmCodeCol { get; set; } = "_006";
        public string QualIdCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string StudIdCol { get; set; } = "_001";
        public string PqmNameCol { get; set; } = "Authorised_Qualification_Name";
        public string PqmCode1Col { get; set; } = "CESM_Code";
        public string PqmCode2Col { get; set; } = "CESM_Code2";
    }

    public class Rule13RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule13ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule13WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_CESM";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_STUD";
        public string CrseTable { get; set; } = "dbo_STUD";
        public string PgTypesText { get; set; } = "";
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule13ValidationSummary? Summary { get; set; }
        public string PqmTable { get; set; } = "";
        public string CesmIdCol { get; set; } = "_001";
        public string CesmCodeCol { get; set; } = "_006";
        public string QualIdCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string StudIdCol { get; set; } = "_001";
        public string PqmNameCol { get; set; } = "Authorised_Qualification_Name";
        public string PqmCode1Col { get; set; } = "CESM_Code";
        public string PqmCode2Col { get; set; } = "CESM_Code2";
    }

    public class Rule13WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule13WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule13RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule13WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule13SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}



