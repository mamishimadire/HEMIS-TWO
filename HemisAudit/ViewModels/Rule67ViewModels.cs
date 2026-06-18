namespace HemisAudit.ViewModels
{
    public class Rule67TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoCregTable { get; set; }
        public string? AutoStudTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule67VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CregTable { get; set; } = "";
        public string StudTable { get; set; } = "dbo_STUD_VALPAC";
        public string CregStudentNoCol { get; set; } = "_007";
        public string CregQualCol { get; set; } = "_001";
        public string CregE051Col { get; set; } = "_051";
        public string StudStudentNoCol { get; set; } = "_007";
        public string StudQualCol { get; set; } = "_001";
        public string E051FilterValues { get; set; } = "E";
    }

    public class Rule67VerifyResult
    {
        public bool Success { get; set; }
        public int CregRecordCount { get; set; }
        public int StudRecordCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule67ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CregTable { get; set; } = "";
        public string StudTable { get; set; } = "dbo_STUD_VALPAC";
        public string CregStudentNoCol { get; set; } = "_007";
        public string CregQualCol { get; set; } = "_001";
        public string CregE051Col { get; set; } = "_051";
        public string StudStudentNoCol { get; set; } = "_007";
        public string StudQualCol { get; set; } = "_001";
        public string E051FilterValues { get; set; } = "E";
    }

    public class Rule67ControlSummaryItemViewModel
    {
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string CriteriaText { get; set; } = "";
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string Status { get; set; } = "";
    }

    public class Rule67ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
        public string ExceptionCode { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule67ValidationSummary
    {
        public bool Success { get; set; }
        public int CregRecordCount { get; set; }
        public int StudRecordCount { get; set; }
        public int TotalValidated { get; set; }
        public int DisplayedCount { get; set; }
        public bool IsPreviewOnly { get; set; }
        public int PreviewLimit { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int NotInStudCount { get; set; }
        public int InvalidE051Count { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string CregTable { get; set; } = "";
        public string StudTable { get; set; } = "dbo_STUD_VALPAC";
        public string CregStudentNoCol { get; set; } = "_007";
        public string CregQualCol { get; set; } = "_001";
        public string CregE051Col { get; set; } = "_051";
        public string StudStudentNoCol { get; set; } = "_007";
        public string StudQualCol { get; set; } = "_001";
        public string E051FilterValues { get; set; } = "E";
        public string TableLinkageText { get; set; } = "";
        public string RuleModeText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule67ControlSummaryItemViewModel> ControlSummaries { get; set; } = new();
        public List<Rule67ValidationRowRecord> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule67WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CregTable { get; set; } = "";
        public string StudTable { get; set; } = "dbo_STUD_VALPAC";
        public string CregStudentNoCol { get; set; } = "_007";
        public string CregQualCol { get; set; } = "_001";
        public string CregE051Col { get; set; } = "_051";
        public string StudStudentNoCol { get; set; } = "_007";
        public string StudQualCol { get; set; } = "_001";
        public string E051FilterValues { get; set; } = "E";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule67ValidationSummary? Summary { get; set; }
    }

    public class Rule67RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule67ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule67WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule67WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule67RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule67WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule67SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
