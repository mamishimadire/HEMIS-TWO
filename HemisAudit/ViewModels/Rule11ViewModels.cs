using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    // ═══════════════════════════════════════════════════════════════════════════
    // RULE 11 – QUAL vs CESM vs PQM VALIDATION
    // Tables: dbo_QUAL (_001,_003,_004,_005,_053,_054,_084,_090) + dbo_CESM (_001,_006) + PQM
    // Checks: QUAL._003 == PQM.Authorised_Qualification_Name  (case-insensitive, whitespace-normalised)
    //         QUAL._005 == PQM.HEQF_Qual_Type                 (both on same PQM row → PASS)
    // EL §5.1.2: Inspect E005 Qualification Type and agree correct type has been allocated per PQM.
    // ═══════════════════════════════════════════════════════════════════════════

    public class Rule11ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        // QUAL table
        public string QualTable { get; set; } = "";
        public string QualIdCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        public string QualApprovalCol { get; set; } = "";
        public string QualHeqfTypeCol { get; set; } = "";
        public string QualApprovalFilterValue { get; set; } = "A";
        public string QualTypeCodesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75"; // Postgraduate type codes
        // CESM table
        public string CesmTable { get; set; } = "";
        public string CesmIdCol { get; set; } = "";
        public string CesmCodeCol { get; set; } = "";
        // PQM table
        public string PqmTable { get; set; } = "";
        public string PqmNameCol { get; set; } = "";
        public string PqmHeqfTypeCol { get; set; } = "";
        public string PqmCodeCol { get; set; } = "";
        public string PqmCesmCode1Col { get; set; } = "CESM_CODE1";
        public string PqmMinTimeTotalCol { get; set; } = "Total2";
        public string PqmWilCol { get; set; } = "WIL_EL2";
        public string PqmAccreditationCol { get; set; } = "CHE_HEQC_Accreditation_Approval_Ref_Nr";
        public string PqmTotalSubsidyCol { get; set; } = "Total2";
        public string QualMinTimeTotalCol { get; set; } = "_053";
        public string QualMinTimeWilCol { get; set; } = "_054";
        public string QualHeqfCol { get; set; } = "_084";
        public string QualTotalSubsidyCol { get; set; } = "_090";
        public string HeqfIndicatorCodesCsv { get; set; } = "H/,HEQF,HEQSF";
        public bool UseMPrefixPopulationSplit { get; set; } = false;
        public bool ExcludeMPrefixPattern { get; set; } = false;
    }

    public class Rule11ValidationRow
    {
        public int ValidationNumber { get; set; }
        public string QualId { get; set; } = "";          // QUAL._001
        public string QualName { get; set; } = "";        // QUAL._003
        public string QualApproval { get; set; } = "";    // QUAL._004
        public string QualHeqfType { get; set; } = "";    // QUAL._005
        public string Qual002 { get; set; } = "";         // QUAL._002 Previous_Years_Qualification_Code
        public string Qual053 { get; set; } = "";         // QUAL._053 Minimum_Time_Total
        public string Qual054 { get; set; } = "";         // QUAL._054 Minimum_Time_Experiential
        public string Qual081 { get; set; } = "";         // QUAL._081 Institution_Programme_Name
        public string Qual082 { get; set; } = "";         // QUAL._082 Qualifier
        public string Qual083 { get; set; } = "";         // QUAL._083 Abbreviation
        public string Qual084 { get; set; } = "";         // QUAL._084 Legacy_Indicator
        public string Qual085 { get; set; } = "";         // QUAL._085 NQF_Exit_Level
        public string Qual086 { get; set; } = "";         // QUAL._086 Minimum_Total_Credits
        public string Qual087 { get; set; } = "";         // QUAL._087 Minimum_Credits_At_Level
        public string Qual088 { get; set; } = "";         // QUAL._088 Maximum_Credits_At_Level
        public string Qual089 { get; set; } = "";         // QUAL._089 Mode_Of_Delivery
        public string Qual090 { get; set; } = "";         // QUAL._090 Total_Subsidy_Units
        public string PopulationType { get; set; } = "";  // Undergraduate / Postgraduate
        public string? CesmCode { get; set; }             // CESM._006
        public string? PqmName { get; set; }              // PQM.Authorised_Qualification_Name
        public string? PqmHeqfType { get; set; }          // PQM.HEQF_Qual_Type
        public string? PqmCode { get; set; }              // PQM.CESM_Code
        public bool NameMatch { get; set; }
        public bool HeqfTypeMatch { get; set; }
        public bool CesmCodeMatch { get; set; }
        public bool NeedsReview { get; set; }
        public string ValidationResult { get; set; } = "";
        public string? ExceptionReason { get; set; }
        public string? PqmCesmCode1 { get; set; }
        public string? PqmMinTimeTotal { get; set; }
        public string? PqmWIL { get; set; }
        public string? PqmAccreditation { get; set; }
        public string? PqmTotalSubsidy { get; set; }
        public bool C2_TypeMatch { get; set; }
        public bool C3_MinTimeMatch { get; set; }
        public bool C4_WILMatch { get; set; }
        public bool C5_HeqfMatch { get; set; }
        public string? C5_ExpectedHeqf { get; set; }
        public bool C6_SubsidyMatch { get; set; }
        public List<string> FailedControls { get; set; } = new();
        public string? PopulationClassificationNote { get; set; }
    }

    public class Rule11ExceptionRecord
    {
        public int ValidationNumber { get; set; }
        public string QualId { get; set; } = "";
        public string QualName { get; set; } = "";
        public string QualApproval { get; set; } = "";
        public string QualHeqfType { get; set; } = "";
        public string Qual002 { get; set; } = "";
        public string Qual053 { get; set; } = "";
        public string Qual054 { get; set; } = "";
        public string Qual081 { get; set; } = "";
        public string Qual082 { get; set; } = "";
        public string Qual083 { get; set; } = "";
        public string Qual084 { get; set; } = "";
        public string Qual085 { get; set; } = "";
        public string Qual086 { get; set; } = "";
        public string Qual087 { get; set; } = "";
        public string Qual088 { get; set; } = "";
        public string Qual089 { get; set; } = "";
        public string Qual090 { get; set; } = "";
        public string PopulationType { get; set; } = "";
        public string? CesmCode { get; set; }
        public string? PqmName { get; set; }
        public string? PqmHeqfType { get; set; }
        public string? PqmCode { get; set; }
        public bool NameMatch { get; set; }
        public bool HeqfTypeMatch { get; set; }
        public bool CesmCodeMatch { get; set; }
        public bool NeedsReview { get; set; }
        public string ValidationResult { get; set; } = "";
        public string ExceptionReason { get; set; } = "";
        public string? PqmCesmCode1 { get; set; }
        public string? PqmMinTimeTotal { get; set; }
        public string? PqmWIL { get; set; }
        public string? PqmAccreditation { get; set; }
        public string? PqmTotalSubsidy { get; set; }
        public bool C2_TypeMatch { get; set; }
        public bool C3_MinTimeMatch { get; set; }
        public bool C4_WILMatch { get; set; }
        public bool C5_HeqfMatch { get; set; }
        public string? C5_ExpectedHeqf { get; set; }
        public bool C6_SubsidyMatch { get; set; }
        public List<string> FailedControls { get; set; } = new();
        public string? PopulationClassificationNote { get; set; }
    }

    public class Rule11ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int ReviewCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        // QUAL
        public string QualTable { get; set; } = "";
        public string QualIdCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        public string QualApprovalCol { get; set; } = "";
        public string QualHeqfTypeCol { get; set; } = "";
        public string QualApprovalFilterValue { get; set; } = "A";
        public string QualTypeCodesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75"; // Postgraduate type codes
        // CESM
        public string CesmTable { get; set; } = "";
        public string CesmIdCol { get; set; } = "";
        public string CesmCodeCol { get; set; } = "";
        // PQM
        public string PqmTable { get; set; } = "";
        public string PqmNameCol { get; set; } = "";
        public string PqmHeqfTypeCol { get; set; } = "";
        public string PqmCodeCol { get; set; } = "";
        public string PqmCesmCode1Col { get; set; } = "CESM_CODE1";
        public string PqmMinTimeTotalCol { get; set; } = "Total2";
        public string PqmWilCol { get; set; } = "WIL_EL2";
        public string PqmAccreditationCol { get; set; } = "CHE_HEQC_Accreditation_Approval_Ref_Nr";
        public string PqmTotalSubsidyCol { get; set; } = "Total2";
        public string QualMinTimeTotalCol { get; set; } = "_053";
        public string QualMinTimeWilCol { get; set; } = "_054";
        public string QualHeqfCol { get; set; } = "_084";
        public string QualTotalSubsidyCol { get; set; } = "_090";
        public string HeqfIndicatorCodesCsv { get; set; } = "H/,HEQF,HEQSF";
        public bool UseMPrefixPopulationSplit { get; set; } = false;
        public bool ExcludeMPrefixPattern { get; set; } = false;
        public int UndergraduateCount { get; set; }
        public int PostgraduateCount { get; set; }
        public List<Rule11ControlSummary> ControlSummaries { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule11ValidationRow> ValidationRows { get; set; } = new();
        public List<Rule11ExceptionRecord> Exceptions { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule11VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string CesmTable { get; set; } = "";
        public string PqmTable { get; set; } = "";
        public string QualIdCol { get; set; } = "";
        public string CesmIdCol { get; set; } = "";
        public string QualApprovalCol { get; set; } = "";
        public string QualHeqfTypeCol { get; set; } = "";
        public string QualApprovalFilterValue { get; set; } = "A";
        public string QualTypeCodesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75"; // Postgraduate type codes
    }

    public class Rule11VerifyResult
    {
        public bool Success { get; set; }
        public int QualTotal { get; set; }
        public int CesmTotal { get; set; }
        public int PqmTotal { get; set; }
        public int MergedTotal { get; set; }
        public string? Error { get; set; }
    }

    public class Rule11FilterValueRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string ApprovalColumn { get; set; } = "";
    }

    public class Rule11FilterValueOption
    {
        public string Value { get; set; } = "";
        public int Count { get; set; }
        public string Label { get; set; } = "";
    }

    public class Rule11FilterValueResult
    {
        public bool Success { get; set; }
        public List<Rule11FilterValueOption> Options { get; set; } = new();
        public string? DefaultValue { get; set; }
        public string? Error { get; set; }
    }

    public class Rule11TableListResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoQualTable { get; set; }
        public string? AutoCesmTable { get; set; }
        public string? AutoPqmTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule11GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string TableRole { get; set; } = "";
    }

    public class Rule11ControlSummary
    {
        public string ControlId { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string CriteriaText { get; set; } = "";
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string Status { get; set; } = "";
    }

    public class Rule11WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        // QUAL
        public string QualTable { get; set; } = "";
        public string QualIdCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        public string QualApprovalCol { get; set; } = "";
        public string QualHeqfTypeCol { get; set; } = "";
        public string QualApprovalFilterValue { get; set; } = "A";
        public string QualTypeCodesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        // CESM
        public string CesmTable { get; set; } = "";
        public string CesmIdCol { get; set; } = "";
        public string CesmCodeCol { get; set; } = "";
        // PQM
        public string PqmTable { get; set; } = "";
        public string PqmNameCol { get; set; } = "";
        public string PqmHeqfTypeCol { get; set; } = "";
        public string PqmCodeCol { get; set; } = "";
        public string PqmCesmCode1Col { get; set; } = "CESM_CODE1";
        public string PqmMinTimeTotalCol { get; set; } = "Total2";
        public string PqmWilCol { get; set; } = "WIL_EL2";
        public string PqmAccreditationCol { get; set; } = "CHE_HEQC_Accreditation_Approval_Ref_Nr";
        public string PqmTotalSubsidyCol { get; set; } = "Total2";
        public string QualMinTimeTotalCol { get; set; } = "_053";
        public string QualMinTimeWilCol { get; set; } = "_054";
        public string QualHeqfCol { get; set; } = "_084";
        public string QualTotalSubsidyCol { get; set; } = "_090";
        public string HeqfIndicatorCodesCsv { get; set; } = "H/,HEQF,HEQSF";
        public bool UseMPrefixPopulationSplit { get; set; } = false;
        public bool ExcludeMPrefixPattern { get; set; } = false;
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule11ValidationSummary? Summary { get; set; }
    }

    public class Rule11WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule11WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule11RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule11ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff =>
            IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload =>
            ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule11RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule11WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }
}
