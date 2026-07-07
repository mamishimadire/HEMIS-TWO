using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule14TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoQualTable
        {
            get => AutoStudTable;
            set => AutoStudTable = value;
        }
        public string? AutoBridgeTable { get; set; }
        public string? AutoCredTable
        {
            get => AutoBridgeTable;
            set => AutoBridgeTable = value;
        }
        public string? AutoCrseTable { get; set; }
        public string? AutoRegistrationTable
        {
            get => AutoCrseTable;
            set => AutoCrseTable = value;
        }
        public string? Error { get; set; }
    }

    public class Rule14VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_CRSE";
        public string QualTable
        {
            get => StudTable;
            set => StudTable = value;
        }
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CredTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CREG";
        public string RegistrationTable
        {
            get => CrseTable;
            set => CrseTable = value;
        }
        public string CrseApprovedCol { get; set; } = "_031";
        public string CrseLinkCol { get; set; } = "_030";
        public string CregLinkCol { get; set; } = "_030";
    }

    public class Rule14VerifyResult
    {
        public bool Success { get; set; }
        public int StudRecordCount { get; set; }
        public int QualRecordCount
        {
            get => StudRecordCount;
            set => StudRecordCount = value;
        }
        public int BridgeRecordCount { get; set; }
        public int CredRecordCount
        {
            get => BridgeRecordCount;
            set => BridgeRecordCount = value;
        }
        public int CrseRecordCount { get; set; }
        public int CregRecordCount
        {
            get => CrseRecordCount;
            set => CrseRecordCount = value;
        }
        public int UnfulfilledPopulationCount { get; set; }
        public int ApprovedQualificationCount
        {
            get => UnfulfilledPopulationCount;
            set => UnfulfilledPopulationCount = value;
        }
        public int ApprovedCredentialCount { get; set; }
        public int RegisteredCredentialCount { get; set; }
        public int Control1PopulationCount { get; set; }
        public int Control2PopulationCount { get; set; }
        public int Control3PopulationCount { get; set; }
        public int ApprovedCourseCount
        {
            get => ApprovedQualificationCount;
            set => ApprovedQualificationCount = value;
        }
        public int RegisteredCourseCount
        {
            get => RegisteredCredentialCount;
            set => RegisteredCredentialCount = value;
        }
        public int MissingRegistrationCount => Math.Max(ApprovedCourseCount - RegisteredCourseCount, 0);
        public string? Error { get; set; }
    }

    public class Rule14ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_CRSE";
        public string QualTable
        {
            get => StudTable;
            set => StudTable = value;
        }
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CredTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CREG";
        public string RegistrationTable
        {
            get => CrseTable;
            set => CrseTable = value;
        }
        public string CrseApprovedCol { get; set; } = "_031";
        public string CrseApprovedVal { get; set; } = "A";
        public string CrseLinkCol { get; set; } = "_030";
        public string CregLinkCol { get; set; } = "_030";
    }

    public class Rule14ControlSummaryItemViewModel
    {
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string CriteriaText { get; set; } = "";
        public int RequestedCount { get; set; }
        public int AvailableCount { get; set; }
        public int AchievedCount { get; set; }
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string Status { get; set; } = "";
    }

    public class Rule14ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule14ValidationSummary
    {
        public bool Success { get; set; }
        public int StudRecordCount { get; set; }
        public int QualRecordCount
        {
            get => StudRecordCount;
            set => StudRecordCount = value;
        }
        public int BridgeRecordCount { get; set; }
        public int CredRecordCount
        {
            get => BridgeRecordCount;
            set => BridgeRecordCount = value;
        }
        public int CrseRecordCount { get; set; }
        public int CregRecordCount
        {
            get => CrseRecordCount;
            set => CrseRecordCount = value;
        }
        public int UnfulfilledPopulationCount { get; set; }
        public int ApprovedQualificationCount
        {
            get => UnfulfilledPopulationCount;
            set => UnfulfilledPopulationCount = value;
        }
        public int ApprovedCredentialCount { get; set; }
        public int RegisteredCredentialCount { get; set; }
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
        public string StudTable { get; set; } = "dbo_CRSE";
        public string QualTable
        {
            get => StudTable;
            set => StudTable = value;
        }
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CredTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CREG";
        public string RegistrationTable
        {
            get => CrseTable;
            set => CrseTable = value;
        }
        public string CrseApprovedCol { get; set; } = "_031";
        public string CrseApprovedVal { get; set; } = "A";
        public string CrseLinkCol { get; set; } = "_030";
        public string CregLinkCol { get; set; } = "_030";
        public string TableLinkageText { get; set; } = "";
        public string RuleModeText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule14ControlSummaryItemViewModel> ControlSummaries { get; set; } = new();
        public List<Rule14ValidationRowRecord> ReviewRows { get; set; } = new();
        public int ApprovedCourseCount
        {
            get => ApprovedQualificationCount;
            set => ApprovedQualificationCount = value;
        }
        public int RegisteredCourseCount
        {
            get => RegisteredCredentialCount;
            set => RegisteredCredentialCount = value;
        }
        public int MissingRegistrationCount => Math.Max(ApprovedCourseCount - RegisteredCourseCount, 0);
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule14RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule14ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule14WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_CRSE";
        public string QualTable
        {
            get => StudTable;
            set => StudTable = value;
        }
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CredTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CREG";
        public string RegistrationTable
        {
            get => CrseTable;
            set => CrseTable = value;
        }
        public string CrseApprovedCol { get; set; } = "_031";
        public string CrseApprovedVal { get; set; } = "A";
        public string CrseLinkCol { get; set; } = "_030";
        public string CregLinkCol { get; set; } = "_030";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule14ValidationSummary? Summary { get; set; }
    }

    public class Rule14WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule14WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule14RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule14WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule14SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}






