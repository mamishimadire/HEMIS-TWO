using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule17GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
    }

    public class Rule17ColumnSelectionResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoFilterColumn { get; set; }
        public string? AutoBreakdownColumn { get; set; }
        public string? Error { get; set; }
    }

    public class Rule17FilterValueRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string FilterColumn { get; set; } = "";
    }

    public class Rule17FilterValueOption
    {
        public string Value { get; set; } = "";
        public int Count { get; set; }
        public string Label { get; set; } = "";
    }

    public class Rule17FilterValueResult
    {
        public bool Success { get; set; }
        public List<Rule17FilterValueOption> Options { get; set; } = new();
        public string? DefaultValue { get; set; }
        public string? Error { get; set; }
    }

    public class Rule17VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string FilterColumn { get; set; } = "";
        public string FilterValue { get; set; } = "";
    }

    public class Rule17SampleRowViewModel
    {
        public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule17VerifyResult
    {
        public bool Success { get; set; }
        public int TotalRecords { get; set; }
        public int MatchingCount { get; set; }
        public List<Rule17SampleRowViewModel> SampleRows { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule17ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string QualJoinCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string FilterColumn { get; set; } = "_025";
        public string FilterValue { get; set; } = "F";
        public string BreakdownColumn { get; set; } = "";
        public int SampleSize { get; set; } = 1;
        public bool ShowAllRecords { get; set; } = true;
    }

    public class Rule17BreakdownItemViewModel
    {
        public string Value { get; set; } = "";
        public int Count { get; set; }
    }

    public class Rule17ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string FilterValue { get; set; } = "";
        public string BreakdownValue { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule17ValidationSummary
    {
        public bool Success { get; set; }
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
        public string TableName { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string QualJoinCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string FilterColumn { get; set; } = "";
        public string FilterValue { get; set; } = "";
        public string BreakdownColumn { get; set; } = "";
        public int SampleSize { get; set; }
        public bool ShowAllRecords { get; set; }
        public bool Sampled { get; set; }
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule17BreakdownItemViewModel> Breakdown { get; set; } = new();
        public List<Rule17ValidationRowRecord> MatchingRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule17RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule17ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule17WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string QualJoinCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string FilterColumn { get; set; } = "_025";
        public string FilterValue { get; set; } = "F";
        public string BreakdownColumn { get; set; } = "";
        public int SampleSize { get; set; } = 1;
        public bool ShowAllRecords { get; set; } = true;
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule17ValidationSummary? Summary { get; set; }
    }

    public class Rule17WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule17WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule17RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule17WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule17SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}


