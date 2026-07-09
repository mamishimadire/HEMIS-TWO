namespace HemisAudit.ViewModels
{
    public class NursingTableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoNursingTable { get; set; }
        public string? AutoProductionTable { get; set; }
        public string? Error { get; set; }
    }

    public class NursingColumnRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
    }

    public class NursingVerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string NursingTable { get; set; } = "Nursing";
        public string ProductionTable { get; set; } = "Clinical_Production";
        public string QualificationColumn { get; set; } = "QUALIFICATION";
        public string SurnameColumn { get; set; } = "Surname";
        public string? TableName { get; set; }
    }

    public class NursingVerifyResult
    {
        public bool Success { get; set; }
        public int NursingRecordCount { get; set; }
        public int ProductionRecordCount { get; set; }
        public int TotalTested { get; set; }
        public int MatchedCount { get; set; }
        public int MissingCount { get; set; }
        public string? Error { get; set; }
    }

    public class NursingValidationRequest
    {
        public int ClientId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string NursingTable { get; set; } = "Nursing";
        public string ProductionTable { get; set; } = "Clinical_Production";
        public string QualificationColumn { get; set; } = "QUALIFICATION";
        public string SurnameColumn { get; set; } = "Surname";
    }

    public class NursingValidationSummary
    {
        public bool Success { get; set; }
        public string Status { get; set; } = "";
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public List<NursingReviewRow> ReviewRows { get; set; } = new();
        public bool IsPreviewOnly { get; set; }
        public int? SavedRunId { get; set; }
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class NursingReviewRow
    {
        public string NursingQualification { get; set; } = "";
        public string NursingSurname { get; set; } = "";
        public string Status { get; set; } = "";
        public string ProductionQualification { get; set; } = "";
        public string ProductionSurname { get; set; } = "";
    }

    public class NursingWorkspaceState
    {
        public int ClientId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string NursingTable { get; set; } = "Nursing";
        public string ProductionTable { get; set; } = "Clinical_Production";
        public string QualificationColumn { get; set; } = "QUALIFICATION";
        public string SurnameColumn { get; set; } = "Surname";
        public DateTime? LastRunAt { get; set; }
        public string? LastRunStatus { get; set; }
        public string? CurrentStatus { get; set; }
        public int? LastRunId { get; set; }
        public NursingValidationSummary? Summary { get; set; }
        public bool ResultsVisible { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserEngagementRole { get; set; } = "";
        public string CurrentUserSignoffComment { get; set; } = "";
    }
}
