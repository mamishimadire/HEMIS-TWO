using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule52TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoValpacTable { get; set; }
        public string? AutoProdTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule52VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ValpacTable  { get; set; } = "dbo_QUAL";
        public string ProdTable    { get; set; } = "MT-audit-prod-QUAL";
        public string ValpacSubjCol { get; set; } = "_001";
        public string ProdSubjCol   { get; set; } = "IAIQUAL";
        public string ApprovalStatusCol { get; set; } = "_004";
        public string ApprovalStatusValues { get; set; } = "A";
    }

    public class Rule52VerifyResult
    {
        public bool Success { get; set; }
        public int ValpacRecordCount { get; set; }
        public int ProdRecordCount { get; set; }
        public int TotalTested { get; set; }
        public int MatchedCount { get; set; }
        public int MissingCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule52ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ValpacTable   { get; set; } = "dbo_QUAL";
        public string ProdTable     { get; set; } = "MT-audit-prod-QUAL";
        public string ValpacSubjCol { get; set; } = "_001";
        public string ProdSubjCol   { get; set; } = "IAIQUAL";
        public string ApprovalStatusCol { get; set; } = "_004";
        public string ApprovalStatusValues { get; set; } = "A";
    }

    public class Rule52ControlSummaryItemViewModel
    {
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string CriteriaText { get; set; } = "";
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string Status { get; set; } = "";
    }

    public class Rule52ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule52ValidationSummary
    {
        public bool Success { get; set; }
        public int ValpacRecordCount { get; set; }
        public int ProdRecordCount { get; set; }
        public int TotalRequested { get; set; }
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
        public string ValpacTable   { get; set; } = "dbo_QUAL";
        public string ProdTable     { get; set; } = "MT-audit-prod-QUAL";
        public string ValpacSubjCol { get; set; } = "_001";
        public string ProdSubjCol   { get; set; } = "IAIQUAL";
        public string ApprovalStatusCol { get; set; } = "_004";
        public string ApprovalStatusValues { get; set; } = "A";
        public string TableLinkageText { get; set; } = "";
        public string RuleModeText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule52ControlSummaryItemViewModel> ControlSummaries { get; set; } = new();
        public List<Rule52ValidationRowRecord> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule52WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ValpacTable   { get; set; } = "dbo_QUAL";
        public string ProdTable     { get; set; } = "MT-audit-prod-QUAL";
        public string ValpacSubjCol { get; set; } = "_001";
        public string ProdSubjCol   { get; set; } = "IAIQUAL";
        public string ApprovalStatusCol { get; set; } = "_004";
        public string ApprovalStatusValues { get; set; } = "A";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule52ValidationSummary? Summary { get; set; }
    }

    public class Rule52RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule52ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule52WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule52WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule52RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule52WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule52SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
