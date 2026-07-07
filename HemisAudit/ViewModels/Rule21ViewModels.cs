using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule21TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? AutoNalTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule21ColumnDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoQualRefColumn { get; set; }
        public string? AutoFirstTimeColumn { get; set; }
        public string? AutoStud007Column { get; set; }
        public string? AutoStud008Column { get; set; }
        public string? AutoStud012Column { get; set; }
        public string? AutoStud026Column { get; set; }
        public string? AutoQualCodeColumn { get; set; }
        public string? AutoQualNameColumn { get; set; }
        public string? AutoNalRefColumn { get; set; }
        public string? AutoNalNameColumn { get; set; }
        public string? AutoNalAlignedColumn { get; set; }
        public string? AutoNalCategoryColumn { get; set; }
        public string? Error { get; set; }
    }

    public class Rule21DistinctValuesResult
    {
        public bool Success { get; set; }
        public List<string> Values { get; set; } = new();
        public string? AutoValue { get; set; }
        public string? Error { get; set; }
    }

    public class Rule21VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string NalTable { get; set; } = "";
        public string StudQualRefColumn { get; set; } = "_001";
        public string Stud007Column { get; set; } = "";
        public string Stud008Column { get; set; } = "";
        public string StudFirstTimeColumn { get; set; } = "_010";
        public string Stud012Column { get; set; } = "";
        public string Stud026Column { get; set; } = "";
        public string StudFirstTimeValue { get; set; } = "F";
        public string QualCodeColumn { get; set; } = "_001";
        public string QualNameColumn { get; set; } = "_003";
        public string NalCategoryColumn { get; set; } = "Category";
        public string NalCategoryValue { get; set; } = "C";
    }

    public class Rule21VerifyResult
    {
        public bool Success { get; set; }
        public int StudTotalCount { get; set; }
        public int StudFilteredCount { get; set; }
        public int QualTotalCount { get; set; }
        public int NalTotalCount { get; set; }
        public int NalFilteredCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule21ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string NalTable { get; set; } = "Non_Aligned_Qualifications";
        public string StudQualRefColumn { get; set; } = "_001";
        public string Stud007Column { get; set; } = "";
        public string Stud008Column { get; set; } = "";
        public string StudFirstTimeColumn { get; set; } = "_010";
        public string Stud012Column { get; set; } = "";
        public string Stud026Column { get; set; } = "";
        public string StudFirstTimeValue { get; set; } = "F";
        public string QualCodeColumn { get; set; } = "_001";
        public string QualNameColumn { get; set; } = "_003";
        public string NalRefColumn { get; set; } = "Qualification_reference_number";
        public string NalNameColumn { get; set; } = "Existing_qualification_name";
        public string NalAlignedColumn { get; set; } = "Aligned_qualification_name";
        public string NalCategoryColumn { get; set; } = "Category";
        public string NalCategoryValue { get; set; } = "C";
        public string NalHeqsfRefColumn { get; set; } = "";
        public string NalSaqaIdColumn { get; set; } = "";
        public string NalNqfColumn { get; set; } = "";
        public string NalCreditsColumn { get; set; } = "";
        public string NalOutcomeColumn { get; set; } = "";
    }

    public class Rule21ValidationRowViewModel
    {
        public int RowNumber { get; set; }
        public string StudQualRef { get; set; } = "";
        public string Stud007Value { get; set; } = "";
        public string Stud008Value { get; set; } = "";
        public string Stud010Value { get; set; } = "";
        public string Stud012Value { get; set; } = "";
        public string Stud026Value { get; set; } = "";
        public string QualCodeValue { get; set; } = "";
        public string QualNameValue { get; set; } = "";
        public string? NalQualName { get; set; }
        public string? NalAlignedName { get; set; }
        public string? NalCategory { get; set; }
        public string? NalHeqsfRef { get; set; }
        public string? NalSaqaId { get; set; }
        public string? NalNqf { get; set; }
        public string? NalCredits { get; set; }
        public string? NalOutcome { get; set; }
        public string Result { get; set; } = "CLEAR";
        public string? ExceptionReason { get; set; }
    }

    public class Rule21ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int FlaggedCount { get; set; }
        public int ClearCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string StudTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string NalTable { get; set; } = "";
        public string StudQualRefColumn { get; set; } = "";
        public string Stud007Column { get; set; } = "";
        public string Stud008Column { get; set; } = "";
        public string StudFirstTimeColumn { get; set; } = "";
        public string Stud012Column { get; set; } = "";
        public string Stud026Column { get; set; } = "";
        public string StudFirstTimeValue { get; set; } = "";
        public string QualCodeColumn { get; set; } = "";
        public string QualNameColumn { get; set; } = "";
        public string NalRefColumn { get; set; } = "";
        public string NalNameColumn { get; set; } = "";
        public string NalAlignedColumn { get; set; } = "";
        public string NalCategoryColumn { get; set; } = "";
        public string NalCategoryValue { get; set; } = "";
        public string NalHeqsfRefColumn { get; set; } = "";
        public string NalSaqaIdColumn { get; set; } = "";
        public string NalNqfColumn { get; set; } = "";
        public string NalCreditsColumn { get; set; } = "";
        public string NalOutcomeColumn { get; set; } = "";
        public int StudTotalCount { get; set; }
        public int QualTotalCount { get; set; }
        public int NalCategoryCount { get; set; }
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public bool IsPreviewOnly { get; set; }
        public int PreviewLimit { get; set; }
        public List<Rule21ValidationRowViewModel> FlaggedRows { get; set; } = new();
        public List<Rule21ValidationRowViewModel> ClearSampleRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule21WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string NalTable { get; set; } = "";
        public string StudQualRefColumn { get; set; } = "_001";
        public string Stud007Column { get; set; } = "";
        public string Stud008Column { get; set; } = "";
        public string StudFirstTimeColumn { get; set; } = "_010";
        public string Stud012Column { get; set; } = "";
        public string Stud026Column { get; set; } = "";
        public string StudFirstTimeValue { get; set; } = "F";
        public string QualCodeColumn { get; set; } = "_001";
        public string QualNameColumn { get; set; } = "_003";
        public string NalRefColumn { get; set; } = "Qualification_reference_number";
        public string NalNameColumn { get; set; } = "Existing_qualification_name";
        public string NalAlignedColumn { get; set; } = "Aligned_qualification_name";
        public string NalCategoryColumn { get; set; } = "Category";
        public string NalCategoryValue { get; set; } = "C";
        public string NalHeqsfRefColumn { get; set; } = "";
        public string NalSaqaIdColumn { get; set; } = "";
        public string NalNqfColumn { get; set; } = "";
        public string NalCreditsColumn { get; set; } = "";
        public string NalOutcomeColumn { get; set; } = "";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule21ValidationSummary? Summary { get; set; }
    }

    public class Rule21WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule21WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule21RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule21ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule21RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule21WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule21SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }

    public class Rule21GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
        public string TableRole { get; set; } = "";  // "stud" | "qual" | "nal"
    }

    public class Rule21GetDistinctValuesRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public string? PreferredValue { get; set; }
    }
}
