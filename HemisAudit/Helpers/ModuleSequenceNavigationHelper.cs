using HemisAudit.ViewModels;

namespace HemisAudit.Helpers
{
    public static class ModuleSequenceNavigationHelper
    {
        private static readonly ModuleSequenceItem[] Modules =
        {
            new(1, "Rule 1", "Qualifications without qualification type"),
            new(2, "Rule 2", "Qualifications without approval status"),
            new(3, "Rule 3", "Duplicate qualification codes"),
            new(4, "Rule 4", "Duplicate course codes"),
            new(5, "Rule 5", "Invalid student numbers"),
            new(6, "Rule 6", "Students without foundation indicator"),
            new(7, "Rule 7", "Students with invalid qualifications"),
            new(8, "Rule 8", "Course registrations for invalid courses"),
            new(9, "Rule 9", "Course registrations for ghost students"),
            new(10, "Rule 10", "Joining Rules"),
            new(11, "Rule 11", "QUAL vs CESM vs PQM Validation"),
            new(12, "Rule 12", "Course Selection from dbo_CREG"),
            new(13, "Rule 13", "CESM Qualification Population Validation"),
            new(14, "Rule 14", "Course Registration Validation"),
            new(15, "Rule 15", "Student Population Validation"),
            new(16, "Rule 16", "Student Population Validation"),
            new(17, "Rule 17", "Graduate Students Fulfilled Qualification Validation"),
            new(18, "Rule 18", "NSFAS Student Validation"),
            new(19, "Rule 19", "Masters and PhD Population Validation"),
            new(20, "Rule 20", "Foundation Validation"),
            new(21, "Rule 21", "First Time Entering Students Validation"),
            new(22, "Rule 22", "Staff Sampling (dbo_PROF)"),
            new(23, "Rule 23", "Reconcile Datasets"),
            new(24, "Rule 24", "Reconcile Qualification Datasets"),
            new(25, "Rule 25", "Reconcile Course Datasets"),
            new(26, "Rule 26", "dbo_PROF to Payroll_Sample 5-Control Validation"),
            new(27, "Rule 27", "Error Validation"),
            new(28, "Rule 28", "Fatal Errors with Exclusions (CESM)"),
            new(29, "Rule 29", "Single Column Filter"),
            new(30, "Rule 30", "Fatal Errors with Exclusions (PROF)"),
            new(31, "Rule 31", "Fatal Errors with Exclusions (QUAL)"),
            new(32, "Rule 32", "Fatal Errors with Exclusions"),
            new(34, "Rule 34", "Census Date Validation"),
            new(35, "Rule 35", "Duplicate Check on dbo_CRSE"),
            new(36, "Rule 36", "Deceased Students Validation"),
            new(37, "Rule 37", "CESM vs PQM Validation"),
            new(38, "Rule 38", "Enhanced QUAL vs PQM Validation"),
            new(39, "Rule 39", "First-Time Entering vs Non-Aligned Qualifications"),
            new(40, "Rule 40", "PROF ASCII Staff Agreement"),
            new(41, "Rule 41", "Student ASCII Agreement"),
            new(44, "Rule 44", "Masters & PhD Research Time Validation"),
            new(45, "Rule 45", "STU vs H16STU Agreement"),
            new(46, "Rule 46", "Foundation Student Chain Validation"),
            new(47, "Rule 47", "QUAL vs H16QUAL Agreement"),
            new(48, "Rule 48", "CRED vs H16CRED Agreement"),
            new(51, "Rule 51", "VALPAC Data in Production"),
            new(52, "Rule 52", "QUAL VALPAC Data in Production"),
            new(53, "Rule 53", "CRSE VALPAC Data in Production"),
            new(54, "Rule 54", "CRED vs QUAL vs PQM Validation"),
            new(55, "Rule 55", "Graduate W-Code Validation"),
            new(57, "Rule 57", "Registration Documentation Agreement"),
            new(58, "Rule 58", "STAFF VALPAC Data in STAFF PRODUCTION"),
            new(59, "Rule 59", "SFTE VALPAC Data in STAFF PRODUCTION"),
            new(60, "Rule 60", "CRSE vs H16CRSE Agreement"),
            new(61, "Rule 61", "Masters / Doctoral Research Time Validation"),
            new(62, "Rule 62", "SQLVALPAC Approved Exception Review"),
            new(63, "Rule 63", "Qualification Code Reference Validation"),
            new(64, "Rule 64", "STUD to CREG Student Number Validation"),
            new(65, "Rule 65", "Cancellation Census Date Validation"),
            new(66, "Rule 66", "NSFAS Students in CREG"),
            new(67, "Rule 67", "CREG-STUD Pair Validation")
        };

        public static ModuleSequenceNavigationViewModel? BuildForWorkspace(int currentRuleNumber, int clientId)
        {
            if (clientId <= 0)
                return null;

            var currentIndex = FindModuleIndex(currentRuleNumber);
            if (currentIndex < 0)
                return null;

            return new ModuleSequenceNavigationViewModel
            {
                CurrentRuleNumber = Modules[currentIndex].RuleNumber,
                CurrentRuleLabel = Modules[currentIndex].RuleLabel,
                CurrentRuleTitle = Modules[currentIndex].RuleTitle,
                BackToValidationRunsUrl = $"/Admin/ClientDetail/{clientId}#validation-runs-panel",
                BackToEngagementUrl = $"/Admin/ClientDetail/{clientId}",
                Previous = currentIndex > 0 ? BuildWorkspaceLink(Modules[currentIndex - 1], clientId) : null,
                Next = currentIndex < Modules.Length - 1 ? BuildWorkspaceLink(Modules[currentIndex + 1], clientId) : null
            };
        }

        public static ModuleSequenceNavigationViewModel? BuildForSavedRun(
            int currentRuleNumber,
            int clientId,
            IEnumerable<ValidationRunRow>? validationRuns,
            string systemRole,
            string currentEngagementRole)
        {
            if (clientId <= 0)
                return null;

            var currentIndex = FindModuleIndex(currentRuleNumber);
            if (currentIndex < 0)
                return null;

            var runs = validationRuns?.ToList() ?? new List<ValidationRunRow>();

            return new ModuleSequenceNavigationViewModel
            {
                CurrentRuleNumber = Modules[currentIndex].RuleNumber,
                CurrentRuleLabel = Modules[currentIndex].RuleLabel,
                CurrentRuleTitle = Modules[currentIndex].RuleTitle,
                Previous = currentIndex > 0
                    ? BuildSavedRunLink(Modules[currentIndex - 1], clientId, runs, systemRole, currentEngagementRole)
                    : null,
                Next = currentIndex < Modules.Length - 1
                    ? BuildSavedRunLink(Modules[currentIndex + 1], clientId, runs, systemRole, currentEngagementRole)
                    : null
            };
        }

        private static int FindModuleIndex(int currentRuleNumber) =>
            Array.FindIndex(Modules, item => item.RuleNumber == currentRuleNumber);

        private static ModuleSequenceLinkViewModel BuildWorkspaceLink(ModuleSequenceItem module, int clientId) =>
            new()
            {
                RuleNumber = module.RuleNumber,
                RuleLabel = module.RuleLabel,
                RuleTitle = module.RuleTitle,
                Url = $"/Rule{module.RuleNumber}?clientId={clientId}",
                OpensSavedRun = false
            };

        private static ModuleSequenceLinkViewModel BuildSavedRunLink(
            ModuleSequenceItem module,
            int clientId,
            List<ValidationRunRow> validationRuns,
            string systemRole,
            string currentEngagementRole)
        {
            var candidateRun = validationRuns
                .Where(run => run.RuleNumber == module.RuleNumber)
                .Where(run => ValidationRunAccessPolicy.CanViewSignedResults(
                    systemRole,
                    currentEngagementRole,
                    run.HasDataAnalystSignoff))
                .OrderByDescending(run => run.IsCurrent)
                .ThenByDescending(run => run.HasDataAnalystSignoff)
                .ThenByDescending(run => run.RunAt)
                .ThenByDescending(run => run.Id)
                .FirstOrDefault();

            return new ModuleSequenceLinkViewModel
            {
                RuleNumber = module.RuleNumber,
                RuleLabel = module.RuleLabel,
                RuleTitle = module.RuleTitle,
                Url = candidateRun != null
                    ? $"/Rule{module.RuleNumber}/Run/{candidateRun.Id}"
                    : $"/Rule{module.RuleNumber}?clientId={clientId}",
                OpensSavedRun = candidateRun != null
            };
        }

        private sealed record ModuleSequenceItem(int RuleNumber, string RuleLabel, string RuleTitle);
    }
}
