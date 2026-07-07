namespace HemisAudit.ViewModels
{
    public class Rule65ColumnMapping
    {
        public string StudentNoCol { get; set; } = "STD_NO";
        public string QualificationCol { get; set; } = "QUAL";
        public string SubjectCol { get; set; } = "SUBJ";
        public string CancelDateCol { get; set; } = "CANCEL";
        public string CensusDateCol { get; set; } = "CENSUS";
        public string CurrentCensusCol { get; set; } = "CURRENT_CENSUS";
    }

    public class Rule65ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CancellationTable { get; set; } = "canceliation list";
        public string ClientTable { get; set; } = "CENSUS_LIST_CLIENT";
        public bool UseClientCensusTable { get; set; } = true;
        public Rule65ColumnMapping ColumnMapping { get; set; } = new();
    }

    public class Rule65ReviewRow
    {
        public int RowNumber { get; set; }
        public string SourceTable { get; set; } = "";
        public string StudentNo { get; set; } = "";
        public string Qualification { get; set; } = "";
        public string Subject { get; set; } = "";
        public string CancelDate { get; set; } = "";
        public string CensusDate { get; set; } = "";
        public string CurrentCensus { get; set; } = "";
        public string ExceptionCategory { get; set; } = "";
        public string ErrorCode { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
    }

    public class Rule65ExceptionCategoryViewModel
    {
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public int Count { get; set; }
    }

    public class Rule65ValidationSummary
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Warning { get; set; }
        public int? SavedRunId { get; set; }
        public int ClientId { get; set; }
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string CancellationTable { get; set; } = "canceliation list";
        public string ClientTable { get; set; } = "CENSUS_LIST_CLIENT";
        public bool UseClientCensusTable { get; set; } = true;
        public Rule65ColumnMapping ColumnMapping { get; set; } = new();
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int ExceptionDetailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public bool IsPreviewOnly { get; set; }
        public int PreviewLimit { get; set; }
        public List<Rule65ReviewRow> PassRows { get; set; } = new();
        public List<Rule65ReviewRow> FailRows { get; set; } = new();
        public List<Rule65ExceptionCategoryViewModel> ExceptionCategories { get; set; } = new();
    }

    public class Rule65VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CancellationTable { get; set; } = "canceliation list";
        public string ClientTable { get; set; } = "CENSUS_LIST_CLIENT";
        public bool UseClientCensusTable { get; set; } = true;
        public Rule65ColumnMapping ColumnMapping { get; set; } = new();
    }

    public class Rule65VerifyResult
    {
        public bool Success { get; set; }
        public int CancellationCount { get; set; }
        public int ClientCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule65TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoCancellationTable { get; set; }
        public string? AutoClientTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule65ColumnDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoStudentNoCol { get; set; }
        public string? AutoQualificationCol { get; set; }
        public string? AutoSubjectCol { get; set; }
        public string? AutoCancelDateCol { get; set; }
        public string? AutoCensusDateCol { get; set; }
        public string? AutoCurrentCensusCol { get; set; }
        public string? Error { get; set; }
    }

    public class Rule65WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule65WorkspaceStateViewModel? Workspace { get; set; }
    }

    public class Rule65WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CancellationTable { get; set; } = "canceliation list";
        public string ClientTable { get; set; } = "CENSUS_LIST_CLIENT";
        public bool UseClientCensusTable { get; set; } = true;
        public Rule65ColumnMapping ColumnMapping { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule65ValidationSummary? Summary { get; set; }
    }

    public class Rule65RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string? GeneratedSql { get; set; }
        public Rule65ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule65SqlResult
    {
        public bool Success { get; set; }
        public string? Sql { get; set; }
        public string? Error { get; set; }
    }

    public class Rule65WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule65GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
    }
}
