using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule51ColumnMapping
    {
        public string ValpacColumn { get; set; } = "";
        public string ProdColumn { get; set; } = "";
        public string Label { get; set; } = "";
    }

    public class Rule51TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoValpacTable { get; set; }
        public string? AutoProdTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule51VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ValpacTable    { get; set; } = "dbo_STUD_VALPAC";
        public string ProdTable      { get; set; } = "dbo_STUD_PRODUCTION";
        public string ValpacCol007   { get; set; } = "_007";
        public string ValpacCol008   { get; set; } = "_008";
        public string ValpacCol001   { get; set; } = "_001";
        public string ValpacColYear  { get; set; } = "ColYear";
        public string ProdColStNo    { get; set; } = "IAGSTNO";
        public string ProdColIdNo    { get; set; } = "IADIDNO";
        public string ProdColQual    { get; set; } = "IAGQUAL";
        public string ProdColYear    { get; set; } = "IAGCYR";
        public string ValpacCol049   { get; set; } = "_049";
        public string SaNationalValues { get; set; } = "SA,PR";
        public string ValpacCol008ZPlaceholders { get; set; } = "ZZZZZZZZZZZZZ";
        public string CregTable      { get; set; } = "";
        public string CregIdCol      { get; set; } = "_007";
        public string CregCompletionStatusCol    { get; set; } = "_032";
        public string CregCompletionStatusValues { get; set; } = "W";
        public List<Rule51ColumnMapping> ColumnMappings { get; set; } = new();
    }

    public class Rule51VerifyResult
    {
        public bool Success { get; set; }
        public int ValpacRecordCount { get; set; }
        public int ProdRecordCount { get; set; }
        public int TotalTested { get; set; }
        public int MatchedCount { get; set; }
        public int MissingCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule51ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ValpacTable    { get; set; } = "dbo_STUD_VALPAC";
        public string ProdTable      { get; set; } = "dbo_STUD_PRODUCTION";
        public string ValpacCol007   { get; set; } = "_007";
        public string ValpacCol008   { get; set; } = "_008";
        public string ValpacCol001   { get; set; } = "_001";
        public string ValpacColYear  { get; set; } = "ColYear";
        public string ProdColStNo    { get; set; } = "IAGSTNO";
        public string ProdColIdNo    { get; set; } = "IADIDNO";
        public string ProdColQual    { get; set; } = "IAGQUAL";
        public string ProdColYear    { get; set; } = "IAGCYR";
        public string ValpacCol049   { get; set; } = "_049";
        public string SaNationalValues { get; set; } = "SA,PR";
        public string ValpacCol008ZPlaceholders { get; set; } = "ZZZZZZZZZZZZZ";
        public string CregTable      { get; set; } = "";
        public string CregIdCol      { get; set; } = "_007";
        public string CregCompletionStatusCol    { get; set; } = "_032";
        public string CregCompletionStatusValues { get; set; } = "W";
        public List<Rule51ColumnMapping> ColumnMappings { get; set; } = new();
    }

    public class Rule51ControlSummaryItemViewModel
    {
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string CriteriaText { get; set; } = "";
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string Status { get; set; } = "";
    }

    public class Rule51ExceptionCategoryViewModel
    {
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public int Count { get; set; }
    }

    public class Rule51ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule51ValidationSummary
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
        public int ForeignNationalExemptCount { get; set; }
        public int PassWithReviewCount { get; set; }
        public int NotInCregCount { get; set; }
        public int CregWithdrawnCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string ValpacTable   { get; set; } = "dbo_STUD_VALPAC";
        public string ProdTable     { get; set; } = "dbo_STUD_PRODUCTION";
        public string ValpacCol007  { get; set; } = "_007";
        public string ValpacCol008  { get; set; } = "_008";
        public string ValpacCol001  { get; set; } = "_001";
        public string ValpacColYear { get; set; } = "ColYear";
        public string ProdColStNo   { get; set; } = "IAGSTNO";
        public string ProdColIdNo   { get; set; } = "IADIDNO";
        public string ProdColQual   { get; set; } = "IAGQUAL";
        public string ProdColYear   { get; set; } = "IAGCYR";
        public string ValpacCol049  { get; set; } = "_049";
        public string SaNationalValues { get; set; } = "SA,PR";
        public string ValpacCol008ZPlaceholders { get; set; } = "ZZZZZZZZZZZZZ";
        public string CregTable      { get; set; } = "";
        public string CregIdCol      { get; set; } = "_007";
        public string CregCompletionStatusCol    { get; set; } = "_032";
        public string CregCompletionStatusValues { get; set; } = "W";
        public List<Rule51ColumnMapping> ColumnMappings { get; set; } = new();
        public string TableLinkageText { get; set; } = "";
        public string RuleModeText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule51ControlSummaryItemViewModel> ControlSummaries { get; set; } = new();
        public List<Rule51ExceptionCategoryViewModel> ExceptionCategories { get; set; } = new();
        public List<Rule51ValidationRowRecord> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule51WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ValpacTable   { get; set; } = "dbo_STUD_VALPAC";
        public string ProdTable     { get; set; } = "dbo_STUD_PRODUCTION";
        public string ValpacCol007  { get; set; } = "_007";
        public string ValpacCol008  { get; set; } = "_008";
        public string ValpacCol001  { get; set; } = "_001";
        public string ValpacColYear { get; set; } = "ColYear";
        public string ProdColStNo   { get; set; } = "IAGSTNO";
        public string ProdColIdNo   { get; set; } = "IADIDNO";
        public string ProdColQual   { get; set; } = "IAGQUAL";
        public string ProdColYear   { get; set; } = "IAGCYR";
        public string ValpacCol049  { get; set; } = "_049";
        public string SaNationalValues { get; set; } = "SA,PR";
        public string ValpacCol008ZPlaceholders { get; set; } = "ZZZZZZZZZZZZZ";
        public string CregTable      { get; set; } = "";
        public string CregIdCol      { get; set; } = "_007";
        public string CregCompletionStatusCol    { get; set; } = "_032";
        public string CregCompletionStatusValues { get; set; } = "W";
        public List<Rule51ColumnMapping> ColumnMappings { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule51ValidationSummary? Summary { get; set; }
    }

    public class Rule51RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule51ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule51WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule51WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule51RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule51WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule51SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
