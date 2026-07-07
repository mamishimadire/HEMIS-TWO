using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule16TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoBridgeTable { get; set; }
        public string? AutoCregTable
        {
            get => AutoBridgeTable;
            set => AutoBridgeTable = value;
        }
        public string? AutoCrseTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule16VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CregTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CRSE";
    }

    public class Rule16VerifyResult
    {
        public bool Success { get; set; }
        public int StudRecordCount { get; set; }
        public int BridgeRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
        public int UnfulfilledPopulationCount { get; set; }
        public int Control1PopulationCount { get; set; }
        public int Control2PopulationCount { get; set; }
        public int Control3PopulationCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule16ColumnValuesRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
    }

    public class Rule16ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CregTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CRSE";
        public int SampleControl1Size { get; set; } = 5;
        public int SampleControl2Size { get; set; } = 3;
        public int SampleControl3Size { get; set; } = 32;
        public string UnfulfilledCol { get; set; } = "_025";
        public string UnfulfilledVal { get; set; } = "N";
        public string FoundationCol { get; set; } = "_091";
        public string FoundationVal { get; set; } = "Y";
        public string DistanceCol { get; set; } = "_024";
        public string DistanceVal { get; set; } = "D";
    }

    public class Rule16ControlSummaryItemViewModel
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

    public class Rule16ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule16ValidationSummary
    {
        public bool Success { get; set; }
        public int StudRecordCount { get; set; }
        public int BridgeRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
        public int UnfulfilledPopulationCount { get; set; }
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
        public string StudTable { get; set; } = "dbo_STUD";
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CregTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string UnfulfilledCol { get; set; } = "_025";
        public string UnfulfilledVal { get; set; } = "N";
        public string FoundationCol { get; set; } = "_091";
        public string FoundationVal { get; set; } = "Y";
        public string DistanceCol { get; set; } = "_024";
        public string DistanceVal { get; set; } = "D";
        public string TableLinkageText { get; set; } = "";
        public string RuleModeText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule16ControlSummaryItemViewModel> ControlSummaries { get; set; } = new();
        public List<Rule16ValidationRowRecord> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule16RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule16ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule16WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CregTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string UnfulfilledCol { get; set; } = "_025";
        public string UnfulfilledVal { get; set; } = "N";
        public string FoundationCol { get; set; } = "_091";
        public string FoundationVal { get; set; } = "Y";
        public string DistanceCol { get; set; } = "_024";
        public string DistanceVal { get; set; } = "D";
        public int SampleControl1Size { get; set; } = 5;
        public int SampleControl2Size { get; set; } = 3;
        public int SampleControl3Size { get; set; } = 32;
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule16ValidationSummary? Summary { get; set; }
    }

    public class Rule16WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule16WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule16RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule16WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule16SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}



