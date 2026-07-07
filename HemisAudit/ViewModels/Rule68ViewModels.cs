using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule68TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoCregTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? AutoCredTable { get; set; }
        public string? AutoCrseTable { get; set; }
        public string? AutoDetailTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule68VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "";
        public string CregTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string CredTable { get; set; } = "";
        public string CrseTable { get; set; } = "";
        public string DetailTable { get; set; } = "dbo_STUD_VALIDATION_DETAIL";
        public string StudNoCol { get; set; } = "_007";
        public string StudQualCol { get; set; } = "_001";
        public string CregStudNoCol { get; set; } = "_007";
        public string CregQualCol { get; set; } = "_001";
        public string CregCourseCol { get; set; } = "_030";
        public string QualQualCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string CredQualCol { get; set; } = "_001";
        public string CredCourseCol { get; set; } = "_030";
        public string CredCreditsCol { get; set; } = "_036";
        public string CrseCourseCol { get; set; } = "_030";
        public string CrseNameCol { get; set; } = "_058";
        public string DetailErrorTypeCol { get; set; } = "";
        public string DetailErrorCol { get; set; } = "Error";
        public string DetailErrorTypeValue { get; set; } = "Fatal";
        public string DetailExclusionCodes { get; set; } = "02202,02301,02302,00708,07201,01501";
        public string DetailElementInfoCol { get; set; } = "Element_Information";
    }

    public class Rule68VerifyResult
    {
        public bool Success { get; set; }
        public int StudRecordCount { get; set; }
        public int CregRecordCount { get; set; }
        public int QualRecordCount { get; set; }
        public int CredRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule68ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "";
        public string CregTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string CredTable { get; set; } = "";
        public string CrseTable { get; set; } = "";
        public string DetailTable { get; set; } = "dbo_STUD_VALIDATION_DETAIL";
        public string StudNoCol { get; set; } = "_007";
        public string StudQualCol { get; set; } = "_001";
        public string CregStudNoCol { get; set; } = "_007";
        public string CregQualCol { get; set; } = "_001";
        public string CregCourseCol { get; set; } = "_030";
        public string QualQualCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string CredQualCol { get; set; } = "_001";
        public string CredCourseCol { get; set; } = "_030";
        public string CredCreditsCol { get; set; } = "_036";
        public string CrseCourseCol { get; set; } = "_030";
        public string CrseNameCol { get; set; } = "_058";
        public decimal MaxTotalCredits { get; set; } = 1.0m;
        public string DetailErrorTypeCol { get; set; } = "";
        public string DetailErrorCol { get; set; } = "Error";
        public string DetailErrorTypeValue { get; set; } = "Fatal";
        public string DetailExclusionCodes { get; set; } = "02202,02301,02302,00708,07201,01501";
        public string DetailElementInfoCol { get; set; } = "Element_Information";
    }

    public class Rule68ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string ValidationResult { get; set; } = "";
        public string ErrorCode { get; set; } = "";
        public string ReconciliationStatus { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule68Rule32OnlyRow
    {
        public int RowNumber { get; set; }
        public string StudentNo { get; set; } = "";
        public string QualCode { get; set; } = "";
        public string SumCredits { get; set; } = "";
        public string ConfirmedByR68 { get; set; } = "No";
    }

    public class Rule68ValidationSummary
    {
        public bool Success { get; set; }
        public int StudRecordCount { get; set; }
        public int CregRecordCount { get; set; }
        public int QualRecordCount { get; set; }
        public int CredRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
        public int DetailRecordCount { get; set; }
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int DisplayedCount { get; set; }
        public bool IsPreviewOnly { get; set; }
        public int PreviewLimit { get; set; }
        public int ConfirmedByRule32Count { get; set; }
        public int NotInRule32Count { get; set; }
        public int Rule32OnlyCount { get; set; }
        public int Rule32ConfirmedByR68Count { get; set; }
        public int Rule32NotInCregCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string StudTable { get; set; } = "";
        public string CregTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string CredTable { get; set; } = "";
        public string CrseTable { get; set; } = "";
        public string DetailTable { get; set; } = "";
        public string StudNoCol { get; set; } = "_007";
        public string StudQualCol { get; set; } = "_001";
        public string CregStudNoCol { get; set; } = "_007";
        public string CregQualCol { get; set; } = "_001";
        public string CregCourseCol { get; set; } = "_030";
        public string QualQualCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string CredQualCol { get; set; } = "_001";
        public string CredCourseCol { get; set; } = "_030";
        public string CredCreditsCol { get; set; } = "_036";
        public string CrseCourseCol { get; set; } = "_030";
        public string CrseNameCol { get; set; } = "_058";
        public decimal MaxTotalCredits { get; set; } = 1.0m;
        public string DetailErrorTypeCol { get; set; } = "";
        public string DetailErrorCol { get; set; } = "Error";
        public string DetailErrorTypeValue { get; set; } = "Fatal";
        public string DetailExclusionCodes { get; set; } = "02202,02301,02302,00708,07201,01501";
        public string DetailElementInfoCol { get; set; } = "Element_Information";
        public string TableLinkageText { get; set; } = "";
        public string RuleModeText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule68ValidationRowRecord> ReviewRows { get; set; } = new();
        public List<Rule68Rule32OnlyRow> Rule32OnlyRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule68WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "";
        public string CregTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string CredTable { get; set; } = "";
        public string CrseTable { get; set; } = "";
        public string DetailTable { get; set; } = "dbo_STUD_VALIDATION_DETAIL";
        public string StudNoCol { get; set; } = "_007";
        public string StudQualCol { get; set; } = "_001";
        public string CregStudNoCol { get; set; } = "_007";
        public string CregQualCol { get; set; } = "_001";
        public string CregCourseCol { get; set; } = "_030";
        public string QualQualCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string CredQualCol { get; set; } = "_001";
        public string CredCourseCol { get; set; } = "_030";
        public string CredCreditsCol { get; set; } = "_036";
        public string CrseCourseCol { get; set; } = "_030";
        public string CrseNameCol { get; set; } = "_058";
        public decimal MaxTotalCredits { get; set; } = 1.0m;
        public string DetailErrorTypeCol { get; set; } = "";
        public string DetailErrorCol { get; set; } = "Error";
        public string DetailErrorTypeValue { get; set; } = "Fatal";
        public string DetailExclusionCodes { get; set; } = "02202,02301,02302,00708,07201,01501";
        public string DetailElementInfoCol { get; set; } = "Element_Information";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule68ValidationSummary? Summary { get; set; }
    }

    public class Rule68RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule68ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule68WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule68WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule68RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule68WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule68SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
