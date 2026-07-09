namespace HemisAudit.ViewModels
{
    // ═══════════════════════════════════════════════════════════════════════════
    // DATABASE DISCOVERY RESULTS
    // ═══════════════════════════════════════════════════════════════════════════

    public class ClinicalTechTableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoClinicaltechTable { get; set; }
        public string? AutoProductionTable { get; set; }
        public string? Error { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // VERIFICATION REQUEST
    // ═══════════════════════════════════════════════════════════════════════════

    public class ClinicalTechVerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ClinicaltechTable { get; set; } = "Clinicaltech";
        public string ProductionTable { get; set; } = "Clinical_Production";
        public string QualificationColumn { get; set; } = "QUALIFICATION";
        public string SurnameColumn { get; set; } = "Surname";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // VERIFICATION RESULT
    // ═══════════════════════════════════════════════════════════════════════════

    public class ClinicalTechVerifyResult
    {
        public bool Success { get; set; }
        public int ClinicaltechRecordCount { get; set; }
        public int ProductionRecordCount { get; set; }
        public int TotalTested { get; set; }
        public int MatchedCount { get; set; }
        public int MissingCount { get; set; }
        public string? Error { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // VALIDATION REQUEST
    // ═══════════════════════════════════════════════════════════════════════════

    public class ClinicalTechValidationRequest
    {
        public int ClientId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ClinicaltechTable { get; set; } = "Clinicaltech";
        public string ProductionTable { get; set; } = "Clinical_Production";
        public string QualificationColumn { get; set; } = "QUALIFICATION";
        public string SurnameColumn { get; set; } = "Surname";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // VALIDATION SUMMARY (results of analysis)
    // ═══════════════════════════════════════════════════════════════════════════

    public class ClinicalTechValidationSummary
    {
        public bool Success { get; set; }
        public string Status { get; set; } = "";
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public List<ClinicalTechReviewRow> ReviewRows { get; set; } = new();
        public bool IsPreviewOnly { get; set; }
        public int? SavedRunId { get; set; }
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // REVIEW ROW (individual validation result)
    // ═══════════════════════════════════════════════════════════════════════════

    public class ClinicalTechReviewRow
    {
        public string ClinicaltechQualification { get; set; } = "";
        public string ClinicaltechSurname { get; set; } = "";
        public string Status { get; set; } = "";  // PASS or FAIL
        public string ProductionQualification { get; set; } = "";
        public string ProductionSurname { get; set; } = "";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WORKSPACE STATE (stored configuration for a client)
    // ═══════════════════════════════════════════════════════════════════════════

    public class ClinicalTechWorkspaceState
    {
        public int ClientId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ClinicaltechTable { get; set; } = "Clinicaltech";
        public string ProductionTable { get; set; } = "Clinical_Production";
        public string QualificationColumn { get; set; } = "QUALIFICATION";
        public string SurnameColumn { get; set; } = "Surname";
        public DateTime? LastRunAt { get; set; }
        public string? LastRunStatus { get; set; }
        public int? LastRunId { get; set; }
        public ClinicalTechValidationSummary? Summary { get; set; }
        public bool ResultsVisible { get; set; }
    }
}
