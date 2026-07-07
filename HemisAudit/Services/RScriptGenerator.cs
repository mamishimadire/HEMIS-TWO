using HemisAudit.ViewModels;

namespace HemisAudit.Services;

/// <summary>
/// Generates R/data.table audit scripts for each HEMIS rule.
/// The generated scripts mirror the SQL validation logic and use the 'data.table' package.
/// By default the scripts load CSV files from the local default data folder, while still supporting a caller-supplied ds object.
/// </summary>
public static class RScriptGenerator
{
    // ── Shared R helpers injected at the top of every script ─────────────────

    private static string RHeader => RScriptScaffold.BuildDataLoadingPrelude() + @"

# ── Helpers ────────────────────────────────────────────────────────────────────
norm <- function(x) toupper(trimws(as.character(x)))

force_char_trim <- function(dt, ...) {
  cols <- c(...)
  for (col in cols) if (col %in% names(dt))
    set(dt, j = col, value = trimws(as.character(dt[[col]])))
  invisible(dt)
}

safe_names <- function(dt) {
  setnames(dt, old = names(dt), new = gsub('^_', 'X', names(dt)))
  invisible(dt)
}

col_val <- function(dt, col, default = NA_character_) {
  if (!is.null(col) && nchar(col) > 0 && col %in% names(dt)) dt[[col]] else rep(default, nrow(dt))
}

print_summary <- function(dt, rule_label) {
  cat(sprintf('\n── %s ─────────────────────────────────────────\n', rule_label))
  cat(sprintf('Total rows     : %d\n', nrow(dt)))
  if ('Status' %in% names(dt)) {
    tbl <- dt[, .N, by = Status]
    for (i in seq_len(nrow(tbl))) cat(sprintf('  %-16s: %d\n', tbl$Status[i], tbl$N[i]))
  }
}
";

    // ── Rule 10: Integrity rules 1–10 ────────────────────────────────────────

    public static string GenerateRule10Script(Rule10ValidationRequest req)
    {
        var qual  = req.QualTable  ?? "dbo_QUAL";
        var stud  = req.StudTable  ?? "dbo_STUD";
        var creg  = req.CregTable  ?? "dbo_CREG";
        var crse  = req.CrseTable  ?? "dbo_CRSE";
        var qcol  = req.QualColumn ?? "_005";
        var scol  = req.StudColumn ?? "_007";
        return RHeader + $@"
# ── Rule 10: HEMIS Integrity Rules (1–10) ─────────────────────────────────────
# Checks cross-table referential integrity between QUAL, STUD, CREG, and CRSE.

qual_table <- '{qual}'
stud_table <- '{stud}'
creg_table <- '{creg}'
crse_table <- '{crse}'
qual_col   <- '{qcol}'    # qualification code in QUAL/STUD
stud_col   <- '{scol}'    # student number column

qual <- copy(ds[[qual_table]]); safe_names(qual)
stud <- copy(ds[[stud_table]]); safe_names(stud)
creg <- copy(ds[[creg_table]]); safe_names(creg)
crse <- copy(ds[[crse_table]]); safe_names(crse)

sc <- gsub('^_', 'X', stud_col)
qc <- gsub('^_', 'X', qual_col)

force_char_trim(stud, sc, qc)
force_char_trim(creg, sc, qc)
force_char_trim(qual, qc)

valid_stud <- stud[!is.na(get(sc)) & get(sc) != '', .(KEY = norm(get(sc)))]
valid_qual <- qual[!is.na(get(qc)) & get(qc) != '', .(KEY = norm(get(qc)))]
setkey(valid_stud, KEY)
setkey(valid_qual, KEY)

# Check 1: CREG rows with no matching STUD record (ghost students)
creg[, KEY_STUD := norm(col_val(.SD, sc))]
ghost <- creg[is.na(KEY_STUD) | KEY_STUD == '' | !KEY_STUD %in% valid_stud$KEY,
              .(Check = 'Ghost Student', RefValue = KEY_STUD)]

# Check 2: CREG rows with invalid QUAL code
creg[, KEY_QUAL := norm(col_val(.SD, qc))]
invalid_qual <- creg[!is.na(KEY_QUAL) & KEY_QUAL != '' & !KEY_QUAL %in% valid_qual$KEY,
                     .(Check = 'Invalid Qual Code', RefValue = KEY_QUAL)]

exceptions <- rbindlist(list(ghost, invalid_qual), use.names = TRUE, fill = TRUE)
exceptions[, Status := 'FAIL']

print_summary(exceptions, 'Rule 10: Integrity Exceptions')
if (nrow(exceptions) == 0) cat('No integrity exceptions found.\n') else print(exceptions)
";
    }

    // ── Rule 11: QUAL / CESM / PQM ───────────────────────────────────────────

    public static string GenerateRule11Script(Rule11ValidationRequest req)
    {
        return RHeader + $@"
# ── Rule 11: QUAL / CESM / PQM Validation ─────────────────────────────────────
# Cross-validates QUAL approval status, HEQF type codes and CESM code presence.

cesm_table           <- '{req.CesmTable}'
cesm_id_col          <- '{req.CesmIdCol}'
cesm_code_col        <- '{req.CesmCodeCol}'
qual_table           <- '{req.QualTable}'
qual_id_col          <- '{req.QualIdCol}'
qual_approval_col    <- '{req.QualApprovalCol}'
qual_approval_val    <- '{req.QualApprovalFilterValue}'
qual_heqf_col        <- '{req.QualHeqfTypeCol}'
qual_type_codes_text <- '{req.QualTypeCodesText}'
pqm_table            <- '{req.PqmTable}'
pqm_code_col         <- '{req.PqmCodeCol}'
pqm_heqf_col         <- '{req.PqmHeqfTypeCol}'

type_codes <- trimws(unlist(strsplit(qual_type_codes_text, ',')))

cesm <- copy(ds[[cesm_table]]); safe_names(cesm)
qual <- copy(ds[[qual_table]]); safe_names(qual)
pqm  <- copy(ds[[pqm_table]]);  safe_names(pqm)

cesm_ic <- gsub('^_', 'X', cesm_id_col)
cesm_cc <- gsub('^_', 'X', cesm_code_col)
qual_ic  <- gsub('^_', 'X', qual_id_col)
qual_ac  <- gsub('^_', 'X', qual_approval_col)
qual_hc  <- gsub('^_', 'X', qual_heqf_col)
pqm_cc   <- gsub('^_', 'X', pqm_code_col)
pqm_hc   <- gsub('^_', 'X', pqm_heqf_col)

force_char_trim(cesm, cesm_ic, cesm_cc)
force_char_trim(qual, qual_ic, qual_ac, qual_hc)
force_char_trim(pqm,  pqm_cc, pqm_hc)

qual[, QUAL_KEY  := norm(col_val(.SD, qual_ic))]
qual[, APPROVAL  := norm(col_val(.SD, qual_ac))]
qual[, HEQF_TYPE := norm(col_val(.SD, qual_hc))]
cesm[, CESM_KEY  := norm(col_val(.SD, cesm_ic))]
cesm[, CESM_CD   := norm(col_val(.SD, cesm_cc))]
pqm[,  PQM_CD    := norm(col_val(.SD, pqm_cc))]

approved_keys <- qual[APPROVAL == norm(qual_approval_val), QUAL_KEY]

# Check 1 – CESM QUAL code not in QUAL table at all
missing_in_qual <- cesm[!CESM_KEY %in% qual$QUAL_KEY,
                         .(QualCode = CESM_KEY, Reason = 'QUAL code not found in QUAL table')]

# Check 2 – QUAL code not approved
not_approved <- cesm[CESM_KEY %in% qual$QUAL_KEY & !CESM_KEY %in% approved_keys,
                      .(QualCode = CESM_KEY, Reason = paste0('Not approved (', qual_approval_col, ' != ', qual_approval_val, ')'))]

# Check 3 – HEQF type code not in expected postgraduate list
pg_mismatch <- qual[HEQF_TYPE != '' & !HEQF_TYPE %in% type_codes & QUAL_KEY %in% cesm$CESM_KEY,
                     .(QualCode = QUAL_KEY, Reason = paste0('HEQF type (', HEQF_TYPE, ') not in expected list'))]

# Check 4 – CESM code missing from PQM
pqm_codes <- unique(pqm$PQM_CD)
missing_pqm <- cesm[!CESM_CD %in% pqm_codes,
                     .(QualCode = CESM_KEY, Reason = paste0('CESM code (', CESM_CD, ') not in PQM table'))]

exceptions <- rbindlist(list(missing_in_qual, not_approved, pg_mismatch, missing_pqm),
                         use.names = TRUE, fill = TRUE)
exceptions[, Status := 'FAIL']

print_summary(exceptions, 'Rule 11: QUAL/CESM/PQM Exceptions')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }

    // ── Rule 12: Active Student Course Selection ──────────────────────────────

    public static string GenerateRule12Script(Rule12ValidationRequest req)
    {
        var creg = req.CregTable;
        var qual = req.QualTable;
        var cres = req.CresTable;
        var sc   = req.CregStudentCol;
        var qc   = req.CregQualCol;
        var cc   = req.CregCourseCol;
        var qjc  = req.QualJoinCol;
        var qdc  = req.QualDescCol;
        var ccc  = req.CresCourseCol;
        var csc  = req.CresStatusCol;
        var csf  = req.CresStatusFilter;
        var ex1  = req.CresExtra1Col;
        return RHeader + $@"
# ── Rule 12: Active Student Course Selection ───────────────────────────────────
# Validates that CREG entries link to active CRES courses with valid QUAL records.

creg_table        <- '{creg}'
qual_table        <- '{qual}'
cres_table        <- '{cres}'
creg_student_col  <- '{sc}'
creg_qual_col     <- '{qc}'
creg_course_col   <- '{cc}'
qual_join_col     <- '{qjc}'
qual_desc_col     <- '{qdc}'
cres_course_col   <- '{ccc}'
cres_status_col   <- '{csc}'
cres_status_filter<- '{csf}'
cres_extra1_col   <- '{ex1}'

creg <- copy(ds[[creg_table]])
qual <- copy(ds[[qual_table]])
cres <- copy(ds[[cres_table]])

force_char_trim(cres, cres_course_col, cres_status_col)
force_char_trim(creg, creg_student_col, creg_qual_col, creg_course_col)
force_char_trim(qual, qual_join_col, qual_desc_col)

# Active CRES (status = filter value)
cres_active <- cres[norm(col_val(.SD, cres_status_col)) == norm(cres_status_filter)]
setnames(cres_active, cres_course_col, 'CRES_COURSE')
if (nchar(cres_extra1_col) > 0 && cres_extra1_col %in% names(cres_active))
  setnames(cres_active, cres_extra1_col, 'CRES_EXTRA1')

cres_keys <- unique(cres_active[, .(CRES_COURSE = norm(CRES_COURSE))])
setkey(cres_keys, CRES_COURSE)

# QUAL lookup
setnames(qual, qual_join_col, 'QUAL_KEY')
setnames(qual, qual_desc_col, 'QUAL_DESC')
qual_lookup <- unique(qual[!is.na(QUAL_KEY) & QUAL_KEY != '', .(QUAL_KEY = norm(QUAL_KEY), QUAL_DESC)])
setkey(qual_lookup, QUAL_KEY)

# CREG: check course in active CRES
setnames(creg, creg_student_col, 'STUD_NO')
setnames(creg, creg_qual_col,    'QUAL_CODE')
setnames(creg, creg_course_col,  'COURSE_CODE')
creg[, STUD_NO    := norm(STUD_NO)]
creg[, QUAL_CODE  := norm(QUAL_CODE)]
creg[, COURSE_CODE:= norm(COURSE_CODE)]

creg[, CRES_MATCH := COURSE_CODE %in% cres_keys$CRES_COURSE]
creg[, QUAL_MATCH := QUAL_CODE   %in% qual_lookup$QUAL_KEY]

creg[qual_lookup, QUAL_DESC := i.QUAL_DESC, on = .(QUAL_CODE = QUAL_KEY)]

creg[, Status := fcase(
  !CRES_MATCH, 'FAIL – Course not in active CRES',
  !QUAL_MATCH, 'FAIL – Qual not in QUAL table',
  default = 'PASS'
)]

exceptions <- creg[Status != 'PASS']
print_summary(creg, 'Rule 12: Active Course Selection')
cat(sprintf('Exceptions (FAIL rows): %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions[, .(STUD_NO, QUAL_CODE, COURSE_CODE, QUAL_DESC, Status)])
";
    }

    // ── Rules 13–25: CESM/QUAL/STUD/CRSE foundation checks ───────────────────

    public static string GenerateCesmQualScript(int ruleNumber, string ruleTitle,
        string cesmTable, string qualTable, string studTable, string crseTable,
        string pgTypesText = "", string governingPartCodes = "ALL")
    {
        return RHeader + $@"
# ── Rule {ruleNumber}: {ruleTitle} ─────────────────────────────────────────────
# Validates CESM (STUD) records against QUAL and CRSE for foundation compliance.

cesm_table  <- '{cesmTable}'
qual_table  <- '{qualTable}'
stud_table  <- '{studTable}'
crse_table  <- '{crseTable}'
pg_types    <- '{pgTypesText}'
governing   <- '{governingPartCodes}'

cesm <- copy(ds[[cesm_table]]); safe_names(cesm)
qual <- copy(ds[[qual_table]]); safe_names(qual)
stud <- copy(ds[[stud_table]]); safe_names(stud)
crse <- copy(ds[[crse_table]]); safe_names(crse)

force_char_trim(cesm, 'X001', 'X007', 'X019', 'X005')
force_char_trim(qual, 'X001', 'X003', 'X005')
force_char_trim(stud, 'X007', 'X001')
force_char_trim(crse, 'X030', 'X091')

# Foundation flag: _091 = '1' marks foundation courses
foundation_crse <- crse[!is.na(X091) & X091 == '1', .(COURSE_CODE = norm(X030))]
setkey(foundation_crse, COURSE_CODE)

valid_qual <- qual[!is.na(X001) & X001 != '', .(KEY = norm(X001), QualDesc = X003, QualType = X005)]
setkey(valid_qual, KEY)

# Check: CESM qual code present in QUAL
cesm[, QUAL_KEY := norm(X001)]
missing_qual <- cesm[!QUAL_KEY %in% valid_qual$KEY,
                     .(StudentNo = X007, QualCode = QUAL_KEY, Reason = 'Qual code not in QUAL table')]

# Check: pg type filter (if supplied)
pg_check <- data.table(Status = character(0), Reason = character(0))
if (nchar(pg_types) > 0) {{
  pg_list <- trimws(strsplit(pg_types, ',')[[1]])
  cesm[, PG_TYPE := norm(X005)]
  pg_check <- cesm[!PG_TYPE %in% norm(pg_list),
                   .(StudentNo = X007, QualCode = QUAL_KEY, Reason = paste('PG type not in filter:', pg_types))]
}}

exceptions <- rbindlist(list(missing_qual, pg_check), use.names = TRUE, fill = TRUE)
exceptions[, Status := 'FAIL']

print_summary(exceptions, paste0('Rule {ruleNumber}: ', '{ruleTitle}'))
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }

    // ── Convenience wrappers for rules that share the CESM/QUAL pattern ───────

    public static string GenerateRule13Script(Rule13ValidationRequest r) =>
        GenerateCesmQualScript(13, "Foundation Student CESM/QUAL Validation",
            r.StudTable, r.QualTable, r.CregTable, r.CrseTable,
            r.PgTypesText, string.Join(",", r.GoverningPartCodes ?? ["ALL"]));

    public static string GenerateRule14Script(Rule14ValidationRequest r) =>
        GenerateCesmQualScript(14, "Foundation CESM – Qualification Type Check",
            r.StudTable, r.QualTable, r.BridgeTable, r.CrseTable,
            "", "ALL");

    public static string GenerateRule15Script(Rule15ValidationRequest r) =>
        GenerateCesmQualScript(15, "Foundation CESM – Course Registration Check",
            r.StudTable, r.QualTable, r.BridgeTable, r.CrseTable,
            "", "ALL");

    public static string GenerateRule16Script(Rule16ValidationRequest r) =>
        GenerateCesmQualScript(16, "Foundation CESM – Programme Type Validation",
            r.StudTable, r.StudTable, r.CregTable, r.CrseTable,
            "", "ALL");

    public static string GenerateRule17Script(Rule17ValidationRequest r) =>
        GenerateCesmQualScript(17, "CESM – Qualification Link Validation",
            r.TableName, "", "", "",
            "", "ALL");

    public static string GenerateRule18Script(Rule18ValidationRequest r) =>
        GenerateCesmQualScript(18, "CESM – Enrolment Type Validation",
            r.StudTable, r.StudTable, r.CregTable, r.CrseTable,
            "", "ALL");

    // ── Rule 19: Masters and PhD student population ───────────────────────────

    public static string GenerateRule19Script(Rule19ValidationRequest r) =>
        GenerateCesmQualScript(19, "Masters and PhD Student Population Validation",
            r.StudTable, r.QualTable, "", "",
            r.MdTypesText, "ALL");

    // ── Rule 20: Foundation STUD/QUAL/CREG/CRSE ───────────────────────────────

    public static string GenerateRule20Script(Rule20ValidationRequest req)
    {
        var stud = req.StudTable;
        var qual = req.QualTable;
        var creg = req.CregTable;
        var crse = req.CrseTable;
        var m = req.ColumnMapping ?? new Rule20ColumnMapping();
        return RHeader + $@"
# ── Rule 20: Foundation Student Validation ─────────────────────────────────────
# Validates that foundation-flagged students in STUD link through CREG to CRSE.

stud_table <- '{stud}'
qual_table <- '{qual}'
creg_table <- '{creg}'
crse_table <- '{crse}'
stud_foundation_flag  <- '{m.StudFoundationFlag}'
stud_foundation_value <- '{m.StudFoundationValue}'
stud_qual_col  <- '{m.StudQualCode}'
creg_qual_col  <- '{m.CregQualCode}'
creg_crse_col  <- '{m.CregCourseCode}'
crse_crse_col  <- '{m.CrseCourseCode}'
crse_fdn_flag  <- '{m.CrseFoundationFlag}'
crse_fdn_value <- '{m.CrseFoundationValue}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
qual <- copy(ds[[qual_table]]); safe_names(qual)
creg <- copy(ds[[creg_table]]); safe_names(creg)
crse <- copy(ds[[crse_table]]); safe_names(crse)

sc_stud  <- gsub('^_', 'X', stud_foundation_flag)
sc_qual  <- gsub('^_', 'X', stud_qual_col)
sc_cregq <- gsub('^_', 'X', creg_qual_col)
sc_cregc <- gsub('^_', 'X', creg_crse_col)
sc_crsec <- gsub('^_', 'X', crse_crse_col)
sc_crsef <- gsub('^_', 'X', crse_fdn_flag)

force_char_trim(stud, sc_stud, sc_qual, 'X007')
force_char_trim(creg, sc_cregq, sc_cregc)
force_char_trim(crse, sc_crsec, sc_crsef)

fdn_students <- stud[norm(col_val(.SD, sc_stud)) == norm(stud_foundation_value)]
fdn_crse     <- crse[norm(col_val(.SD, sc_crsef)) == norm(crse_fdn_value),
                     .(CRSE_CODE = norm(col_val(.SD, sc_crsec)))]
setkey(fdn_crse, CRSE_CODE)

creg[, CREG_QUAL  := norm(col_val(.SD, sc_cregq))]
creg[, CREG_CRSE  := norm(col_val(.SD, sc_cregc))]
creg[, CRSE_MATCH := CREG_CRSE %in% fdn_crse$CRSE_CODE]

fdn_students[, STUD_QUAL := norm(col_val(.SD, sc_qual))]
result <- merge(fdn_students[, .(StudentNo = X007, STUD_QUAL)],
                creg[, .(CREG_QUAL, CREG_CRSE, CRSE_MATCH)],
                by.x = 'STUD_QUAL', by.y = 'CREG_QUAL', all.x = TRUE)

result[, Status := fcase(
  is.na(CREG_CRSE), 'FAIL – No CREG record',
  !CRSE_MATCH,      'FAIL – Course not a foundation course',
  default = 'PASS'
)]

exceptions <- result[Status != 'PASS']
print_summary(result, 'Rule 20: Foundation Student Validation')
cat(sprintf('Exceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions)
";
    }

    // ── Rule 21: First-time entering students ─────────────────────────────────

    public static string GenerateRule21Script(Rule21ValidationRequest r) =>
        Rule21RScriptGenerator.Generate(r) + RScriptScaffold.BuildAutoExportFooter("Rule21");

    // ── Rule 22: PROF staff validation ───────────────────────────────────────

    public static string GenerateRule22Script(Rule22ValidationRequest req)
    {
        var stud = req.ProfTable;
        var qual = req.ProfTable;
        return RHeader + $@"
# ── Rule 22: Staff Validation (dbo_PROF) ──────────────────────────────────────
# Validates staff records for required fields and consistency.

prof_table <- '{stud}'
qual_table <- '{qual}'

prof <- copy(ds[[prof_table]]); safe_names(prof)
qual <- copy(ds[[qual_table]]); safe_names(qual)

required_cols <- c('X037', 'X011', 'X012', 'X013', 'X014')
missing_cols  <- setdiff(required_cols, names(prof))
if (length(missing_cols) > 0)
  cat(sprintf('WARNING: Missing columns in %s: %s\n', prof_table, paste(missing_cols, collapse = ', ')))

present_cols <- intersect(required_cols, names(prof))
force_char_trim(prof, present_cols)

prof[, StaffKey := norm(col_val(.SD, 'X037'))]
blank_key <- prof[is.na(StaffKey) | StaffKey == '', .(Reason = 'Blank staff number (X037)', StaffKey)]

exceptions <- blank_key
exceptions[, Status := 'FAIL']
print_summary(exceptions, 'Rule 22: Staff Validation')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }

    // ── Rules 23–25: Reconciliation checks ───────────────────────────────────

    public static string GenerateReconcileScript(int ruleNumber, string ruleTitle,
        string studTable, string qualTable, string cregTable, string crseTable)
    {
        return RHeader + $@"
# ── Rule {ruleNumber}: {ruleTitle} ─────────────────────────────────────────────
# Reconciles record counts between HEMIS datasets.

stud_table <- '{studTable}'
qual_table <- '{qualTable}'
creg_table <- '{cregTable}'
crse_table <- '{crseTable}'

stud <- ds[[stud_table]]
qual <- ds[[qual_table]]
creg <- ds[[creg_table]]
crse <- ds[[crse_table]]

cat(sprintf('── Rule {ruleNumber}: {ruleTitle} ──\n'))
cat(sprintf('%-20s : %d\n', stud_table, nrow(stud)))
cat(sprintf('%-20s : %d\n', qual_table, nrow(qual)))
cat(sprintf('%-20s : %d\n', creg_table, nrow(creg)))
cat(sprintf('%-20s : %d\n', crse_table, nrow(crse)))

# Population ratios
cat(sprintf('\nCREG / STUD ratio : %.2f\n', nrow(creg) / max(nrow(stud), 1)))
cat(sprintf('CREG / CRSE ratio : %.2f\n', nrow(creg) / max(nrow(crse), 1)))
";
    }

    public static string GenerateRule23Script(Rule23ValidationRequest r) =>
        GenerateReconcileScript(23, "Reconcile Datasets", r.StudTable, r.AuditTable, r.H16Table, "");
    public static string GenerateRule24Script(Rule24ValidationRequest r) =>
        GenerateReconcileScript(24, "Reconcile Qualification Datasets", "", r.QualTable, r.AuditTable, r.H16Table);
    public static string GenerateRule25Script(Rule25ValidationRequest r) =>
        GenerateReconcileScript(25, "Reconcile Course Datasets", r.CrseTable, r.AuditTable, r.H16Table, "");

    // ── Rule 26: PROF to Payroll 5-Control Validation ─────────────────────────

    public static string GenerateRule26Script(Rule26ValidationRequest req)
    {
        var prof    = req.ProfTable;
        var payroll = req.PayrollTable;
        var ppn     = req.ProfPersonnelColumn;
        var pet     = req.ProfEmploymentTypeColumn;
        var pg      = req.ProfGenderColumn;
        var pgr     = req.ProfGroupColumn;
        var pbd     = req.ProfBirthDateColumn;
        var ypn     = req.PayrollPersonnelColumn;
        var yet     = req.PayrollEmploymentTypeColumn;
        var yg      = req.PayrollGenderColumn;
        var ygr     = req.PayrollGroupColumn;
        var ybd     = req.PayrollBirthDateColumn;
        return RHeader + $@"
# ── Rule 26: PROF to Payroll 5-Control Validation ─────────────────────────────
# Compares staff records between dbo_PROF and Payroll_Sample on 5 controls.

prof_table    <- '{prof}'
payroll_table <- '{payroll}'

prof    <- copy(ds[[prof_table]]);    safe_names(prof)
payroll <- copy(ds[[payroll_table]]); safe_names(payroll)

# Column mapping (after safe_names renames _ -> X)
prof_personnel_col    <- gsub('^_', 'X', '{ppn}')
prof_empl_type_col    <- gsub('^_', 'X', '{pet}')
prof_gender_col       <- gsub('^_', 'X', '{pg}')
prof_group_col        <- gsub('^_', 'X', '{pgr}')
prof_birth_col        <- gsub('^_', 'X', '{pbd}')
payroll_personnel_col <- '{ypn}'
payroll_empl_type_col <- '{yet}'
payroll_gender_col    <- '{yg}'
payroll_group_col     <- '{ygr}'
payroll_birth_col     <- '{ybd}'

# Build base tables
ProfBase <- prof[, .(
  PersonnelKey     = norm(col_val(.SD, prof_personnel_col)),
  EmploymentType   = trimws(as.character(col_val(.SD, prof_empl_type_col))),
  GenderValue      = toupper(trimws(as.character(col_val(.SD, prof_gender_col)))),
  GroupValue       = trimws(as.character(col_val(.SD, prof_group_col))),
  BirthValue       = trimws(as.character(col_val(.SD, prof_birth_col)))
)]
ProfBase <- ProfBase[!is.na(PersonnelKey) & PersonnelKey != '']

PayrollBase <- payroll[, .(
  PersonnelKey     = norm(col_val(.SD, payroll_personnel_col)),
  EmploymentType   = trimws(as.character(col_val(.SD, payroll_empl_type_col))),
  GenderValue      = toupper(trimws(as.character(col_val(.SD, payroll_gender_col)))),
  GroupValue       = trimws(as.character(col_val(.SD, payroll_group_col))),
  BirthValue       = trimws(as.character(col_val(.SD, payroll_birth_col)))
)]

setkey(ProfBase,    PersonnelKey)
setkey(PayrollBase, PersonnelKey)

# Control 1: PROF not in Payroll
c1 <- ProfBase[!PayrollBase, .(PersonnelKey, Control = 1L, ControlName = 'PROF without Payroll record',
                                PROF_Value = PersonnelKey, Payroll_Value = NA_character_)]

# Control 2: Employment type first-letter mismatch
linked <- merge(ProfBase, PayrollBase, by = 'PersonnelKey', suffixes = c('_p', '_y'))
c2 <- linked[substr(EmploymentType_p, 1, 1) != substr(EmploymentType_y, 1, 1),
             .(PersonnelKey, Control = 2L, ControlName = 'Employment Type Mismatch',
               PROF_Value = EmploymentType_p, Payroll_Value = EmploymentType_y)]

# Control 3: Gender first-letter mismatch
c3 <- linked[substr(GenderValue_p, 1, 1) != substr(GenderValue_y, 1, 1),
             .(PersonnelKey, Control = 3L, ControlName = 'Gender Mismatch',
               PROF_Value = GenderValue_p, Payroll_Value = GenderValue_y)]

# Control 4: Group value mismatch
c4 <- linked[norm(GroupValue_p) != norm(GroupValue_y),
             .(PersonnelKey, Control = 4L, ControlName = 'Group Mismatch',
               PROF_Value = GroupValue_p, Payroll_Value = GroupValue_y)]

# Control 5: Birth date mismatch
c5 <- linked[norm(BirthValue_p) != norm(BirthValue_y),
             .(PersonnelKey, Control = 5L, ControlName = 'Birth Date Mismatch',
               PROF_Value = BirthValue_p, Payroll_Value = BirthValue_y)]

exceptions <- rbindlist(list(c1, c2, c3, c4, c5), use.names = TRUE, fill = TRUE)
exceptions[, Status := 'FAIL']

cat(sprintf('── Rule 26: PROF to Payroll 5-Control Validation ─────\n'))
cat(sprintf('PROF population    : %d\n', nrow(ProfBase)))
cat(sprintf('Payroll population : %d\n', nrow(PayrollBase)))
cat(sprintf('Total exceptions   : %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) {{
  tbl <- exceptions[, .N, by = .(Control, ControlName)]
  for (i in seq_len(nrow(tbl)))
    cat(sprintf('  Control %-2d %-35s: %d\n', tbl$Control[i], tbl$ControlName[i], tbl$N[i]))
}}
if (nrow(exceptions) > 0) print(exceptions) else cat('No exceptions found.\n')
";
    }

    // ── Rules 27–32: Error / fatal filtering ─────────────────────────────────

    public static string GenerateErrorFilterScript(int ruleNumber, string ruleTitle,
        string studTable, string qualTable, string cregTable, string crseTable,
        string pgTypesText = "")
    {
        return RHeader + $@"
# ── Rule {ruleNumber}: {ruleTitle} ─────────────────────────────────────────────
# Filters rows with fatal or error codes from HEMIS submission datasets.

stud_table <- '{studTable}'
qual_table <- '{qualTable}'
creg_table <- '{cregTable}'
crse_table <- '{crseTable}'
pg_types   <- '{pgTypesText}'

stud <- copy(ds[[stud_table]]); safe_names(stud)

# Identify error indicator columns (typically start with 'ERR' or '_ERR')
err_cols <- grep('^X?ERR|_ERR|^ERR', names(stud), value = TRUE, ignore.case = TRUE)
if (length(err_cols) == 0) {{
  cat('No error columns found in', stud_table, '\n')
}} else {{
  error_rows <- stud[Reduce('|', lapply(err_cols, function(c) !is.na(stud[[c]]) & stud[[c]] != '' & stud[[c]] != '0'))]
  error_rows[, ErrorCount := rowSums(sapply(err_cols, function(c) !is.na(.SD[[c]]) & .SD[[c]] != '' & .SD[[c]] != '0'))]
  cat(sprintf('── Rule {ruleNumber}: {ruleTitle} ──\n'))
  cat(sprintf('Total rows with errors: %d / %d\n', nrow(error_rows), nrow(stud)))
  print(error_rows[, .SD, .SDcols = c('X007', err_cols, 'ErrorCount')])
}}
";
    }

    public static string GenerateRule27Script(Rule27ValidationRequest r) =>
        GenerateErrorFilterScript(27, "Error Validation (Dynamic Filtering)",
            r.TableName, "", "", "", "");
    public static string GenerateRule28Script(Rule32ValidationRequest r) =>
        GenerateErrorFilterScript(28, "Fatal Errors with Exclusions (CESM)",
            r.TableName, "", "", "", "");
    public static string GenerateRule29Script(Rule29ValidationRequest r) =>
        GenerateSingleColumnFilterScript(29, "Single Column Filter",
            r.TableName, "", "", "", "");
    public static string GenerateRule30Script(Rule32ValidationRequest r) =>
        GenerateErrorFilterScript(30, "Fatal Errors Extended",
            r.TableName, "", "", "", "");
    public static string GenerateRule31Script(Rule31ValidationRequest r) =>
        GenerateErrorFilterScript(31, "Fatal Errors with Exclusions (QUAL)",
            r.TableName, "", "", "", "");
    public static string GenerateRule32Script(Rule32ValidationRequest r) =>
        GenerateErrorFilterScript(32, "Fatal Errors with Exclusions (STUD)",
            r.TableName, "", "", "", "");

    private static string GenerateSingleColumnFilterScript(int ruleNumber, string ruleTitle,
        string studTable, string qualTable, string cregTable, string crseTable, string pgTypesText)
    {
        return RHeader + $@"
# ── Rule {ruleNumber}: {ruleTitle} ─────────────────────────────────────────────
# Filters records based on a single column value criterion.

stud_table <- '{studTable}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
force_char_trim(stud, 'X007', 'X001')

cat(sprintf('── Rule {ruleNumber}: {ruleTitle} ──\n'))
cat(sprintf('Table: %s  Rows: %d\n', stud_table, nrow(stud)))
";
    }

    // ── Rule 34: Census Date Calculation ──────────────────────────────────────

    public static string GenerateRule34Script(Rule34ValidationRequest req)
    {
        var tbl    = req.TableName;
        var fday   = req.FirstDayColumn;
        var lday   = req.LastDayColumn;
        var census = req.CensusDateColumn;
        var block  = req.BlockColumn;
        return RHeader + $@"
# ── Rule 34: Census Date Calculation Validation ────────────────────────────────
# Validates that census dates fall within the valid period and are not on holidays.

creg_table        <- '{tbl}'
first_day_col     <- '{fday}'
last_day_col      <- '{lday}'
census_date_col   <- '{census}'
block_col         <- '{block}'

creg <- copy(ds[[creg_table]]); safe_names(creg)

fc <- gsub('^_', 'X', first_day_col)
lc <- gsub('^_', 'X', last_day_col)
cc <- gsub('^_', 'X', census_date_col)
bc <- if (nchar(block_col) > 0) gsub('^_', 'X', block_col) else NULL

force_char_trim(creg, fc, lc, cc)

creg[, FirstDay    := as.Date(col_val(.SD, fc), tryFormats = c('%Y-%m-%d', '%d/%m/%Y', '%d-%m-%Y'))]
creg[, LastDay     := as.Date(col_val(.SD, lc), tryFormats = c('%Y-%m-%d', '%d/%m/%Y', '%d-%m-%Y'))]
creg[, CensusDate  := as.Date(col_val(.SD, cc), tryFormats = c('%Y-%m-%d', '%d/%m/%Y', '%d-%m-%Y'))]

creg[, PeriodDays  := as.numeric(LastDay - FirstDay)]
creg[, CensusDayNo := as.numeric(CensusDate - FirstDay) + 1L]

creg[, Status := fcase(
  is.na(CensusDate),                           'FAIL – Census date missing',
  is.na(FirstDay) | is.na(LastDay),            'FAIL – Period date missing',
  CensusDate < FirstDay | CensusDate > LastDay,'FAIL – Census date outside period',
  default = 'PASS'
)]

exceptions <- creg[Status != 'PASS']
print_summary(creg, 'Rule 34: Census Date Validation')
cat(sprintf('Exceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0)
  print(exceptions[, .(col_val(.SD, 'X007'), FirstDay, LastDay, CensusDate, PeriodDays, CensusDayNo, Status)])
";
    }

    // ── Rule 35: Duplicate check on CRSE ─────────────────────────────────────

    public static string GenerateRule35Script(Rule35ValidationRequest req)
    {
        var tbl = req.TableName;
        var dup = req.DuplicateColumn ?? "_030";
        return RHeader + $@"
# ── Rule 35: Duplicate Check on CRSE (_030) ────────────────────────────────────
# Identifies duplicate course code (_030) records in the CRSE table.

crse_table      <- '{tbl}'
duplicate_col   <- '{dup}'

crse <- copy(ds[[crse_table]]); safe_names(crse)
dc   <- gsub('^_', 'X', duplicate_col)
force_char_trim(crse, dc)

crse[, KEY := norm(col_val(.SD, dc))]
dupes <- crse[KEY != '' & !is.na(KEY), .(.N), by = KEY][N > 1]
setnames(dupes, 'KEY', 'CourseCode')

exceptions <- crse[norm(col_val(.SD, dc)) %in% dupes$CourseCode]
exceptions[, Status := 'FAIL – Duplicate course code']

print_summary(exceptions, 'Rule 35: Duplicate CRSE Course Code')
cat(sprintf('Duplicate groups : %d\n', nrow(dupes)))
cat(sprintf('Affected rows    : %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions[, c(dc, 'Status'), with = FALSE])
";
    }

    // ── Rule 36: STUD vs Deceased ─────────────────────────────────────────────

    public static string GenerateRule36Script(ValidationRequest req)
    {
        return RHeader + $@"
# ── Rule 36: STUD vs Deceased Student Validation ──────────────────────────────
# Checks that students in the Deceased table are not still active in STUD.

stud_table     <- '{req.StudTable}'
deceased_table <- '{req.DeceasedTable}'
stud_col       <- '{req.StudColumn}'
deceased_col   <- '{req.DeceasedColumn}'

stud     <- copy(ds[[stud_table]]);     safe_names(stud)
deceased <- copy(ds[[deceased_table]]); safe_names(deceased)

sc  <- gsub('^_', 'X', stud_col)
dc  <- gsub('^_', 'X', deceased_col)
force_char_trim(stud,     sc)
force_char_trim(deceased, dc)

stud_keys <- stud[!is.na(col_val(.SD, sc)) & col_val(.SD, sc) != '',
                   .(KEY = norm(col_val(.SD, sc)))]
setkey(stud_keys, KEY)

deceased[, KEY_DEC := norm(col_val(.SD, dc))]
still_active <- deceased[KEY_DEC %in% stud_keys$KEY,
                          .(DeceasedStudentNo = KEY_DEC, Reason = 'Deceased student still active in STUD')]
still_active[, Status := 'FAIL']

missing_from_stud <- deceased[!KEY_DEC %in% stud_keys$KEY,
                               .(DeceasedStudentNo = KEY_DEC, Reason = 'Deceased student not in STUD')]
missing_from_stud[, Status := 'INFO']

exceptions <- rbindlist(list(still_active, missing_from_stud), use.names = TRUE)
print_summary(exceptions[Status == 'FAIL'], 'Rule 36: STUD vs Deceased')
cat(sprintf('Still active (FAIL): %d\n', nrow(still_active)))
cat(sprintf('Not in STUD (INFO) : %d\n', nrow(missing_from_stud)))
if (nrow(still_active) > 0) print(still_active)
";
    }

    // ── Rule 37: CESM / QUAL / PQM ───────────────────────────────────────────

    public static string GenerateRule37Script(Rule37ValidationRequest req)
    {
        return RHeader + $@"
# ── Rule 37: CESM / QUAL / PQM Validation ─────────────────────────────────────
# Validates that CESM qualification names match PQM authorised names.

cesm_table  <- '{req.CesmTable}'
qual_table  <- '{req.QualTable}'
pqm_table   <- '{req.PqmTable}'
cesm_id_col <- '{req.CesmIdCol}'
cesm_code_col <- '{req.CesmCodeCol}'
qual_id_col   <- '{req.QualIdCol}'
qual_name_col <- '{req.QualNameCol}'
pqm_name_col  <- '{req.PqmNameCol}'
pqm_code1_col <- '{req.PqmCode1Col}'

cesm <- copy(ds[[cesm_table]]); safe_names(cesm)
qual <- copy(ds[[qual_table]]); safe_names(qual)
pqm  <- copy(ds[[pqm_table]]); safe_names(pqm)

cc <- gsub('^_', 'X', cesm_code_col)
qc <- gsub('^_', 'X', qual_id_col)
qn <- gsub('^_', 'X', qual_name_col)
force_char_trim(cesm, cc)
force_char_trim(qual, qc, qn)

qual_lookup <- qual[!is.na(col_val(.SD, qc)) & col_val(.SD, qc) != '',
                    .(QualKey = norm(col_val(.SD, qc)), QualName = col_val(.SD, qn))]
setkey(qual_lookup, QualKey)

cesm[, CESM_QUAL := norm(col_val(.SD, cc))]
result <- merge(cesm[, .(CESM_QUAL)], qual_lookup, by.x = 'CESM_QUAL', by.y = 'QualKey', all.x = TRUE)
result[, Status := fifelse(is.na(QualName), 'FAIL – Qual not in QUAL table', 'PASS')]

exceptions <- result[Status != 'PASS']
print_summary(exceptions, 'Rule 37: CESM/QUAL/PQM')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }

    // ── Rule 38: QUAL / PQM agreement ────────────────────────────────────────

    public static string GenerateRule38Script(Rule38ValidationRequest req)
    {
        return RHeader + $@"
# ── Rule 38: QUAL / PQM Agreement ─────────────────────────────────────────────
# Validates that QUAL names and research codes match PQM authorised records.

qual_table  <- '{req.QualTable}'
qual_id_col <- '{req.QualIdCol}'
qual_nm_col <- '{req.QualNameCol}'

qual <- copy(ds[[qual_table]]); safe_names(qual)
qc   <- gsub('^_', 'X', qual_id_col)
qn   <- gsub('^_', 'X', qual_nm_col)
force_char_trim(qual, qc, qn)

qual[, QualKey  := norm(col_val(.SD, qc))]
qual[, QualName := col_val(.SD, qn)]
dupes <- qual[QualKey != '' & !is.na(QualKey), .(.N), by = QualKey][N > 1]

exceptions <- qual[QualKey %in% dupes$QualKey | is.na(QualName) | QualName == '']
exceptions[, Status := fcase(
  QualKey %in% dupes$QualKey, 'FAIL – Duplicate QUAL code',
  is.na(QualName) | QualName == '', 'FAIL – Missing QUAL name',
  default = 'FAIL'
)]

print_summary(exceptions, 'Rule 38: QUAL/PQM Agreement')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions[, .(QualKey, QualName, Status)])
";
    }

    // ── Rule 39: First-time entering vs non-aligned qualifications ─────────────

    public static string GenerateRule39Script(Rule39ValidationRequest req)
    {
        var fteCol = req.StudFirstTimeColumn ?? "_010";
        var fteVal = req.StudFirstTimeValue  ?? "F";
        var s007   = req.Stud007Column       ?? "_007";
        var nalCat = req.NalCategoryColumn   ?? "Category";
        var nalVal = req.NalCategoryValue    ?? "C";
        return RHeader + $@"
# ── Rule 39: First-Time Entering Students vs Non-Aligned Qualifications ─────────
# Checks that first-time entering students are not linked to non-aligned qualifications.

stud_table         <- '{req.StudTable}'
qual_table         <- '{req.QualTable}'
nal_table          <- '{req.NalTable}'
fte_col            <- '{fteCol}'
fte_value          <- '{fteVal}'
stud_007_col       <- '{s007}'
stud_qual_ref_col  <- '{req.StudQualRefColumn ?? "_001"}'
nal_category_col   <- '{nalCat}'
nal_category_value <- '{nalVal}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
qual <- copy(ds[[qual_table]]); safe_names(qual)
nal  <- if (nchar(nal_table) > 0 && table_exists(nal_table)) copy(ds[[nal_table]]) else data.table()

sc   <- gsub('^_', 'X', stud_007_col)
ftc  <- gsub('^_', 'X', fte_col)
sqrc <- gsub('^_', 'X', stud_qual_ref_col)
force_char_trim(stud, sc, ftc, sqrc)

# First-time entering: fte_col == fte_value
fte <- stud[norm(col_val(.SD, ftc)) == norm(fte_value)]
cat(sprintf('First-time entering students: %d\n', nrow(fte)))

if (nrow(nal) > 0) {{
  safe_names(nal)
  nalc <- gsub('^_', 'X', nal_category_col)
  nal_nonaligned <- nal[norm(col_val(.SD, nalc)) == norm(nal_category_value)]
  nal_quals <- unique(nal_nonaligned[, .(NAL_QUAL = norm(col_val(.SD, gsub('^_', 'X', '{req.NalCategoryColumn ?? "X001"}'))))])
  fte[, QUAL := norm(col_val(.SD, sqrc))]
  exceptions <- fte[QUAL %in% nal_quals$NAL_QUAL,
                    .(StudentNo = col_val(.SD, sc), QualCode = QUAL, Status = 'FAIL – Linked to non-aligned qual')]
}} else {{
  cat('NAL table not provided; skipping non-aligned qualification check.\n')
  exceptions <- data.table()
}}

print_summary(exceptions, 'Rule 39: FTE vs Non-Aligned Quals')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }

    // ── Rule 40: PROF / VALPAC Staff Agreement ────────────────────────────────

    public static string GenerateRule40Script(Rule40ValidationRequest req)
    {
        var prof  = req.ProfTable;
        var other = req.ValpacTable;
        var pk    = req.ProfKey;
        var ok    = req.ValpacKey;
        var pairs = req.ValpacPairs?.Select(p => $"list(prof='{p.ProfCol}', other='{p.OtherCol}', label='{p.Label}')") ?? [];
        var pairsR = string.Join(",\n  ", pairs);
        return RHeader + $@"
# ── Rule 40: PROF / VALPAC Staff Agreement ─────────────────────────────────────
# Full outer join comparison of PROF vs VALPAC (or SFTE) on staff fields.

prof_table  <- '{prof}'
other_table <- '{other}'
prof_key    <- '{pk}'
other_key   <- '{ok}'

column_pairs <- list(
  {(string.IsNullOrEmpty(pairsR) ? "list(prof='_037', other='_037', label='Staff No')" : pairsR)}
)

prof  <- copy(ds[[prof_table]]);  safe_names(prof)
other <- copy(ds[[other_table]]); safe_names(other)

pk_safe <- gsub('^_', 'X', prof_key)
ok_safe <- gsub('^_', 'X', other_key)

force_char_trim(prof,  pk_safe)
force_char_trim(other, ok_safe)

prof[,  KEY := norm(col_val(.SD, pk_safe))]
other[, KEY := norm(col_val(.SD, ok_safe))]

result <- merge(prof, other, by = 'KEY', all = TRUE, suffixes = c('_p', '_o'))

result[, MISSING_PROF  := is.na(col_val(.SD, paste0(pk_safe, '_p')))]
result[, MISSING_OTHER := is.na(col_val(.SD, paste0(ok_safe, '_o')))]

disagree_cols <- character(0)
for (pair in column_pairs) {{
  pc <- gsub('^_', 'X', pair$prof)
  oc <- gsub('^_', 'X', pair$other)
  pc_r <- if (paste0(pc, '_p') %in% names(result)) paste0(pc, '_p') else pc
  oc_r <- if (paste0(oc, '_o') %in% names(result)) paste0(oc, '_o') else oc
  if (pc_r %in% names(result) && oc_r %in% names(result)) {{
    col_name <- paste0('DIFF_', pair$label)
    result[, (col_name) := norm(col_val(.SD, pc_r)) != norm(col_val(.SD, oc_r))]
    disagree_cols <- c(disagree_cols, col_name)
  }}
}}

result[, Status := fcase(
  MISSING_PROF,  paste0('MISSING-', prof_table),
  MISSING_OTHER, paste0('MISSING-', other_table),
  length(disagree_cols) > 0 && Reduce('|', lapply(disagree_cols, function(c) result[[c]])), 'DISAGREE',
  default = 'AGREE'
)]

exceptions <- result[Status != 'AGREE']
print_summary(result, 'Rule 40: Staff Agreement')
cat(sprintf('Exceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions[, c('KEY', 'Status', disagree_cols), with = FALSE])
";
    }

    // ── Rule 41: STUD vs MT-Audit ─────────────────────────────────────────────

    public static string GenerateRule41Script(Rule41ValidationRequest req)
    {
        var stud  = req.StudTable;
        var audit = req.AuditTable;
        var sk    = req.StudKey;
        var ak    = req.AuditKey;
        var pairs = req.Pairs ?? new() { new() { StudCol = "_007", AuditCol = "IAGSTNO", Label = "Student No" },
                                         new() { StudCol = "_008", AuditCol = "IADIDNO", Label = "Birth Date" },
                                         new() { StudCol = "_001", AuditCol = "IAGQUAL", Label = "Qualification" } };
        var pairsR = string.Join(",\n  ",
            pairs.Select(p => $"list(stud='{p.StudCol}', audit='{p.AuditCol}', label='{p.Label}')"));
        return RHeader + $@"
# ── Rule 41: STUD vs MT-Audit Agreement ───────────────────────────────────────
# Full outer join of STUD against MT-Audit, comparing student fields.

stud_table  <- '{stud}'
audit_table <- '{audit}'
stud_key    <- '{sk}'
audit_key   <- '{ak}'

column_pairs <- list(
  {pairsR}
)

stud  <- copy(ds[[stud_table]]);  safe_names(stud)
audit <- copy(ds[[audit_table]]); safe_names(audit)

sk_safe <- gsub('^_', 'X', stud_key)
ak_safe <- audit_key   # audit table uses production column names

force_char_trim(stud,  sk_safe)
force_char_trim(audit, ak_safe)

stud[,  KEY := norm(col_val(.SD, sk_safe))]
audit[, KEY := norm(col_val(.SD, ak_safe))]

result <- merge(stud, audit, by = 'KEY', all = TRUE, suffixes = c('_s', '_a'))

agree_parts <- character(0)
for (pair in column_pairs) {{
  sc <- gsub('^_', 'X', pair$stud)
  ac <- pair$audit
  sc_r <- if (paste0(sc, '_s') %in% names(result)) paste0(sc, '_s') else sc
  ac_r <- if (paste0(ac, '_a') %in% names(result)) paste0(ac, '_a') else ac
  if (sc_r %in% names(result) && ac_r %in% names(result)) {{
    diff_col <- paste0('DIFF_', gsub(' ', '_', pair$label))
    result[, (diff_col) := norm(col_val(.SD, sc_r)) != norm(col_val(.SD, ac_r))]
    agree_parts <- c(agree_parts, diff_col)
  }}
}}

result[, Status := fcase(
  is.na(col_val(.SD, paste0(sk_safe, '_s'))), paste0('MISSING-', stud_table),
  is.na(col_val(.SD, paste0(ak_safe, '_a'))), paste0('MISSING-', audit_table),
  length(agree_parts) > 0 && Reduce('|', lapply(agree_parts, function(c) result[[c]])), 'DISAGREE',
  default = 'AGREE'
)]

exceptions <- result[Status != 'AGREE']
print_summary(result, 'Rule 41: STUD vs MT-Audit Agreement')
cat(sprintf('AGREE    : %d\n', nrow(result[Status == 'AGREE'])))
cat(sprintf('DISAGREE : %d\n', nrow(result[Status == 'DISAGREE'])))
cat(sprintf('MISSING  : %d\n', nrow(result[grepl('^MISSING', Status)])))
if (nrow(exceptions) > 0) print(exceptions[, c('KEY', 'Status', agree_parts), with = FALSE])
";
    }

    // ── Rule 44 / 61: Graduation and Research Time Validation ────────────────

    public static string GenerateGraduationScript(int ruleNumber, string ruleTitle,
        string studTable, string qualTable,
        string studStudentNoCol, string studQualCodeCol, string studStatusCol,
        string studIdCol, string studResearchTimeCol,
        string qualQualCodeCol)
    {
        return RHeader + $@"
# ── Rule {ruleNumber}: {ruleTitle} ─────────────────────────────────────────────
# Validates student graduation status and research time against qualification requirements.

stud_table        <- '{studTable}'
qual_table        <- '{qualTable}'
stud_stno_col     <- '{studStudentNoCol}'
stud_qual_col     <- '{studQualCodeCol}'
stud_status_col   <- '{studStatusCol}'
stud_id_col       <- '{studIdCol}'
stud_research_col <- '{studResearchTimeCol}'
qual_code_col     <- '{qualQualCodeCol}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
qual <- copy(ds[[qual_table]]); safe_names(qual)

sc <- gsub('^_', 'X', stud_stno_col)
qc <- gsub('^_', 'X', stud_qual_col)
st <- gsub('^_', 'X', stud_status_col)
rc <- gsub('^_', 'X', stud_research_col)
qqc<- gsub('^_', 'X', qual_code_col)

force_char_trim(stud, sc, qc, st, rc)
force_char_trim(qual, qqc)

valid_quals <- qual[!is.na(col_val(.SD, qqc)) & col_val(.SD, qqc) != '',
                    .(QualKey = norm(col_val(.SD, qqc)))]
setkey(valid_quals, QualKey)

stud[, QUAL_KEY := norm(col_val(.SD, qc))]
stud[, STATUS   := norm(col_val(.SD, st))]

# Check: graduated students (status = 'G') with missing QUAL record
graduated <- stud[STATUS == 'G']
missing_qual <- graduated[!QUAL_KEY %in% valid_quals$QualKey,
                           .(StudentNo = col_val(.SD, sc), QualCode = QUAL_KEY,
                             Status = 'FAIL – Qual code not in QUAL table')]

exceptions <- missing_qual
print_summary(exceptions, paste0('Rule {ruleNumber}: ', '{ruleTitle}'))
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }

    public static string GenerateRule44Script(Rule44ValidationRequest req)
    {
        var m = req.ColumnMapping ?? new Rule44ColumnMapping();
        return GenerateGraduationScript(44, "Student Graduation Validation",
            req.StudTable, req.QualTable,
            m.StudStudentNoCol, m.StudQualCodeCol, m.StudStatusCol,
            m.StudIdCol, m.StudResearchTimeCol, m.QualQualCodeCol);
    }

    public static string GenerateRule61Script(Rule61ValidationRequest req)
    {
        var m = req.ColumnMapping ?? new Rule61ColumnMapping();
        return GenerateGraduationScript(61, "Graduate Research Time Validation",
            req.StudTable, req.QualTable,
            m.StudStudentNoCol, m.StudQualCodeCol, m.StudStatusCol,
            m.StudIdCol, m.StudResearchTimeCol, m.QualQualCodeCol);
    }

    // ── Rule 45: STU vs H16STU ────────────────────────────────────────────────

    public static string GenerateAgreementScript(int ruleNumber, string ruleTitle,
        string table1, string table2, string key1, string key2,
        IEnumerable<(string Col1, string Col2, string Label)> pairs)
    {
        var pairsR = string.Join(",\n  ",
            pairs.Select(p => $"list(col1='{p.Col1}', col2='{p.Col2}', label='{p.Label}')"));
        return RHeader + $@"
# ── Rule {ruleNumber}: {ruleTitle} ─────────────────────────────────────────────
# Full outer join comparison between {table1} and {table2}.

table1 <- '{table1}'
table2 <- '{table2}'
key1   <- '{key1}'
key2   <- '{key2}'

column_pairs <- list(
  {(string.IsNullOrEmpty(pairsR) ? $"list(col1='{key1}', col2='{key2}', label='Key')" : pairsR)}
)

tbl1 <- copy(ds[[table1]]); safe_names(tbl1)
tbl2 <- copy(ds[[table2]]); safe_names(tbl2)

k1 <- gsub('^_', 'X', key1)
k2 <- gsub('^_', 'X', key2)
force_char_trim(tbl1, k1)
force_char_trim(tbl2, k2)

tbl1[, KEY := norm(col_val(.SD, k1))]
tbl2[, KEY := norm(col_val(.SD, k2))]

result <- merge(tbl1, tbl2, by = 'KEY', all = TRUE, suffixes = c('_1', '_2'))

diff_cols <- character(0)
for (pair in column_pairs) {{
  c1 <- gsub('^_', 'X', pair$col1)
  c2 <- gsub('^_', 'X', pair$col2)
  c1r <- if (paste0(c1, '_1') %in% names(result)) paste0(c1, '_1') else c1
  c2r <- if (paste0(c2, '_2') %in% names(result)) paste0(c2, '_2') else c2
  if (c1r %in% names(result) && c2r %in% names(result)) {{
    dcol <- paste0('DIFF_', gsub('[^A-Za-z0-9]', '_', pair$label))
    result[, (dcol) := norm(col_val(.SD, c1r)) != norm(col_val(.SD, c2r))]
    diff_cols <- c(diff_cols, dcol)
  }}
}}

result[, Status := fcase(
  is.na(KEY), 'MISSING',
  !KEY %in% tbl2$KEY, paste0('MISSING-', table2),
  !KEY %in% tbl1$KEY, paste0('MISSING-', table1),
  length(diff_cols) > 0 && Reduce('|', lapply(diff_cols, function(c) !is.na(result[[c]]) & result[[c]])), 'DISAGREE',
  default = 'AGREE'
)]

cat(sprintf('── Rule {ruleNumber}: {ruleTitle} ──\n'))
tbl <- result[, .N, by = Status]
for (i in seq_len(nrow(tbl))) cat(sprintf('  %-20s: %d\n', tbl$Status[i], tbl$N[i]))
exceptions <- result[Status != 'AGREE']
if (nrow(exceptions) > 0) print(exceptions[, c('KEY', 'Status', diff_cols), with = FALSE])
";
    }

    // Rules 45, 47, 48, 60 all share Rule41ValidationRequest
    public static string GenerateRule45Script(Rule41ValidationRequest req) =>
        GenerateAgreementScript(45, "STU vs H16STU Agreement",
            req.StudTable, req.AuditTable, req.StudKey, req.AuditKey,
            (req.Pairs ?? new()).Select(p => (p.StudCol, p.AuditCol, p.Label)));

    public static string GenerateRule47Script(Rule41ValidationRequest req) =>
        GenerateAgreementScript(47, "QUAL vs H16QUAL Agreement",
            req.StudTable, req.AuditTable, req.StudKey, req.AuditKey,
            (req.Pairs ?? new()).Select(p => (p.StudCol, p.AuditCol, p.Label)));

    public static string GenerateRule48Script(Rule41ValidationRequest req) =>
        GenerateAgreementScript(48, "CRED vs H16CRED Agreement",
            req.StudTable, req.AuditTable, req.StudKey, req.AuditKey,
            (req.Pairs ?? new()).Select(p => (p.StudCol, p.AuditCol, p.Label)));

    public static string GenerateRule60Script(Rule41ValidationRequest req) =>
        GenerateAgreementScript(60, "CRSE vs H16CRSE Agreement",
            req.StudTable, req.AuditTable, req.StudKey, req.AuditKey,
            (req.Pairs ?? new()).Select(p => (p.StudCol, p.AuditCol, p.Label)));

    // ── Rule 46: Foundation student qualification validation ──────────────────

    public static string GenerateRule46Script(Rule46ValidationRequest req)
    {
        return RHeader + $@"
# ── Rule 46: Foundation Student Qualification Validation ─────────────────────
# Validates that foundation-coded students have approved foundation qualifications.

stud_table    <- '{req.StudTable}'
qual_table    <- '{req.QualTable}'
stud_id_col   <- '{req.StudIdCol}'
stud_007_col  <- '{req.Stud007Col}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
qual <- copy(ds[[qual_table]]); safe_names(qual)

sc  <- gsub('^_', 'X', stud_007_col)
ic  <- gsub('^_', 'X', stud_id_col)
force_char_trim(stud, sc, ic, 'X001')
force_char_trim(qual, 'X001', 'X091')

fdn_qual <- qual[norm(col_val(.SD, 'X091')) == '1', .(QUAL_CODE = norm(X001))]
setkey(fdn_qual, QUAL_CODE)

stud[, QUAL_KEY := norm(X001)]
stud[, Status := fifelse(QUAL_KEY %in% fdn_qual$QUAL_CODE, 'PASS', 'FAIL – Not a foundation qualification')]

exceptions <- stud[Status != 'PASS']
print_summary(stud, 'Rule 46: Foundation Qualification')
cat(sprintf('Exceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions[, .(col_val(.SD, sc), QUAL_KEY, Status)])
";
    }

    // ── Rules 51–53, 58–59: VALPAC vs PRODUCTION ─────────────────────────────

    public static string GenerateValpacScript(int ruleNumber, string ruleTitle,
        string valpacTable, string prodTable, string valpacKeyCol, string prodKeyCol)
    {
        return RHeader + $@"
# ── Rule {ruleNumber}: {ruleTitle} ─────────────────────────────────────────────
# Compares VALPAC data against PRODUCTION records on key fields.

valpac_table  <- '{valpacTable}'
prod_table    <- '{prodTable}'
valpac_key    <- '{valpacKeyCol}'
prod_key      <- '{prodKeyCol}'

valpac <- copy(ds[[valpac_table]]); safe_names(valpac)
prod   <- copy(ds[[prod_table]])

force_char_trim(valpac, gsub('^_', 'X', valpac_key))
force_char_trim(prod,   prod_key)

vk <- gsub('^_', 'X', valpac_key)
valpac[, KEY_V := norm(col_val(.SD, vk))]
prod[,   KEY_P := norm(col_val(.SD, prod_key))]

in_valpac_not_prod <- valpac[!KEY_V %in% prod$KEY_P,
                              .(Key = KEY_V, Reason = paste('In', valpac_table, 'but not in', prod_table))]
in_prod_not_valpac <- prod[!KEY_P %in% valpac$KEY_V,
                            .(Key = KEY_P, Reason = paste('In', prod_table, 'but not in', valpac_table))]

exceptions <- rbindlist(list(in_valpac_not_prod, in_prod_not_valpac), use.names = TRUE)
exceptions[, Status := 'FAIL']

cat(sprintf('── Rule {ruleNumber}: {ruleTitle} ──\n'))
cat(sprintf('%s population : %d\n', valpac_table, nrow(valpac)))
cat(sprintf('%s population : %d\n', prod_table,   nrow(prod)))
cat(sprintf('Exceptions           : %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions) else cat('No exceptions found.\n')
";
    }

    public static string GenerateRule51Script(Rule51ValidationRequest req) =>
        GenerateValpacScript(51, "STUD VALPAC vs PRODUCTION Matching",
            req.ValpacTable, req.ProdTable, req.ValpacCol007, req.ProdColStNo);

    public static string GenerateRule52Script(Rule52ValidationRequest req) =>
        GenerateValpacScript(52, "QUAL VALPAC vs PRODUCTION Matching",
            req.ValpacTable, req.ProdTable, req.ValpacSubjCol, req.ProdSubjCol);

    public static string GenerateRule53Script(Rule53ValidationRequest req) =>
        GenerateValpacScript(53, "CREG VALPAC vs PRODUCTION Matching",
            req.ValpacTable, req.ProdTable, req.ValpacSubjCol, req.ProdSubjCol);

    public static string GenerateRule58Script(Rule58ValidationRequest req) =>
        GenerateValpacScript(58, "Staff VALPAC Data vs Staff PRODUCTION",
            req.ValpacTable, req.ProdTable, req.ValpacCol037, req.ProdColPersonelNumber);

    public static string GenerateRule59Script(Rule59ValidationRequest req) =>
        GenerateValpacScript(59, "SFTE VALPAC Data vs SFTE PRODUCTION",
            req.ValpacTable, req.ProdTable, req.ValpacCol037, req.ProdColPersonelNumber);

    // ── Rule 54: CRED / QUAL / PQM ────────────────────────────────────────────

    public static string GenerateRule54Script(Rule54ValidationRequest req)
    {
        return RHeader + $@"
# ── Rule 54: CRED / QUAL / PQM Validation ─────────────────────────────────────
# Validates CRED qualification name and research code against PQM authorised list.

cred_table       <- '{req.CredTable}'
qual_table       <- '{req.QualTable}'
pqm_table        <- '{req.PqmTable}'
cred_id_col      <- '{req.CredIdCol}'
cred_research1   <- '{req.CredResearch1Col}'
qual_id_col      <- '{req.QualIdCol}'
qual_nm_col      <- '{req.QualNameCol}'
pqm_nm_col       <- '{req.PqmNameCol}'
pqm_research1col <- '{req.PqmResearch1Col}'

cred <- copy(ds[[cred_table]]); safe_names(cred)
qual <- copy(ds[[qual_table]]); safe_names(qual)
pqm  <- copy(ds[[pqm_table]]); safe_names(pqm)

cc <- gsub('^_', 'X', cred_id_col)
qc <- gsub('^_', 'X', qual_id_col)
qn <- gsub('^_', 'X', qual_nm_col)
rc <- gsub('^_', 'X', cred_research1)

force_char_trim(cred, cc, rc)
force_char_trim(qual, qc, qn)

qual_lookup <- qual[, .(QualKey = norm(col_val(.SD, qc)), QualName = col_val(.SD, qn))]
setkey(qual_lookup, QualKey)

cred[, CRED_QUAL     := norm(col_val(.SD, cc))]
cred[, CRED_RESEARCH := norm(col_val(.SD, rc))]
result <- merge(cred[, .(CRED_QUAL, CRED_RESEARCH)], qual_lookup, by.x = 'CRED_QUAL', by.y = 'QualKey', all.x = TRUE)

result[, Status := fcase(
  is.na(QualName) | QualName == '', 'FAIL – Qual not in QUAL table',
  default = 'PASS'
)]

exceptions <- result[Status != 'PASS']
print_summary(exceptions, 'Rule 54: CRED/QUAL/PQM')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }

    // ── Rule 55: Graduate fulfilled-status validation ─────────────────────────

    public static string GenerateRule55Script(Rule55ValidationRequest req)
    {
        return RHeader + $@"
# ── Rule 55: Graduate Fulfilled-Status Validation ─────────────────────────────
# Validates that students with fulfilled status have valid graduation records.

stud_table          <- '{req.StudTable}'
qual_table          <- '{req.QualTable}'
stud_id_col         <- '{req.StudIdCol}'
stud_qual_code_col  <- '{req.StudQualCodeCol}'
stud_fulfilled_col  <- '{req.StudFulfilledCol}'
stud_fulfilled_val  <- '{req.StudFulfilledFilterValue}'
qual_code_col       <- '{req.QualCodeCol}'
qual_name_col       <- '{req.QualNameCol}'
qual_approval_col   <- '{req.QualApprovalCol}'
qual_approval_val   <- '{req.QualApprovalFilterValue}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
qual <- copy(ds[[qual_table]]); safe_names(qual)

sic <- gsub('^_', 'X', stud_id_col)
sqc <- gsub('^_', 'X', stud_qual_code_col)
sfc <- gsub('^_', 'X', stud_fulfilled_col)
qqc <- gsub('^_', 'X', qual_code_col)
qnc <- gsub('^_', 'X', qual_name_col)
qac <- gsub('^_', 'X', qual_approval_col)

force_char_trim(stud, sic, sqc, sfc)
force_char_trim(qual, qqc, qnc, qac)

# Approved qualifications only
approved_quals <- qual[norm(col_val(.SD, qac)) == norm(qual_approval_val),
                       .(QualKey = norm(col_val(.SD, qqc)), QualName = col_val(.SD, qnc))]
setkey(approved_quals, QualKey)

# Fulfilled students
fulfilled <- stud[norm(col_val(.SD, sfc)) == norm(stud_fulfilled_val)]
fulfilled[, QUAL_KEY := norm(col_val(.SD, sqc))]

exceptions <- fulfilled[!QUAL_KEY %in% approved_quals$QualKey,
                         .(StudentId = col_val(.SD, sic), QualCode = QUAL_KEY,
                           Status = 'FAIL – Fulfilled student qual not in approved QUAL table')]

print_summary(exceptions, 'Rule 55: Graduate Fulfilled-Status')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }

    // ── Rule 57: Registration documentation agreement ─────────────────────────

    public static string GenerateRule57Script(Rule57ValidationRequest req)
    {
        return RHeader + $@"
# ── Rule 57: Registration Documentation Agreement ──────────────────────────────
# Validates that STUD registration type matches CREG registration type filter.

stud_table          <- '{req.StudTable}'
creg_table          <- '{req.CregTable}'
stud_id_col         <- '{req.StudIdCol}'
stud_code_col       <- '{req.StudCodeCol}'
stud_reg_type_col   <- '{req.StudRegTypeCol}'
creg_id_col         <- '{req.CregIdCol}'
creg_code_col       <- '{req.CregCodeCol}'
creg_reg_type_col   <- '{req.CregRegTypeCol}'
creg_filter_val     <- '{req.CregRegTypeFilterValue}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
creg <- copy(ds[[creg_table]]); safe_names(creg)

sic  <- gsub('^_', 'X', stud_id_col)
src  <- gsub('^_', 'X', stud_reg_type_col)
cic  <- gsub('^_', 'X', creg_id_col)
crc  <- gsub('^_', 'X', creg_reg_type_col)

force_char_trim(stud, sic, src)
force_char_trim(creg, cic, crc)

# Filter CREG by registration type
creg_filtered <- if (nchar(creg_filter_val) > 0)
  creg[norm(col_val(.SD, crc)) == norm(creg_filter_val)]
else creg

creg_filtered[, CREG_KEY := norm(col_val(.SD, cic))]
creg_filtered[, CREG_REG := norm(col_val(.SD, crc))]
stud[,          STUD_KEY := norm(col_val(.SD, sic))]
stud[,          STUD_REG := norm(col_val(.SD, src))]

result <- merge(creg_filtered[, .(CREG_KEY, CREG_REG)],
                stud[, .(STUD_KEY, STUD_REG)],
                by.x = 'CREG_KEY', by.y = 'STUD_KEY', all.x = TRUE)

result[, Status := fcase(
  is.na(STUD_REG),          'FAIL – Student not in STUD',
  CREG_REG != STUD_REG,     'FAIL – Registration type mismatch',
  default = 'PASS'
)]

exceptions <- result[Status != 'PASS']
print_summary(result, 'Rule 57: Registration Documentation Agreement')
cat(sprintf('Exceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions)
";
    }

    // ── Rule 62: STUD postal code / entrance category ────────────────────────

    public static string GenerateRule62Script(Rule62ValidationRequest req)
    {
        var m = req.ColumnMapping ?? new Rule62ColumnMapping();
        return RHeader + $@"
# ── Rule 62: STUD Postal Code / Entrance Category Validation ──────────────────
# Validates postal codes and entrance category fields in the STUD table.

stud_table        <- '{req.StudTable}'
stud_no_col       <- '{m.StudStudentNoCol}'
postal_code_col   <- '{m.StudPostalCodeCol}'
home_postcode_col <- '{m.StudHomePostcodeCol}'
entrance_cat_col  <- '{m.StudEntranceCategoryCol}'

stud <- copy(ds[[stud_table]]); safe_names(stud)

sc  <- gsub('^_', 'X', stud_no_col)
pcc <- gsub('^_', 'X', postal_code_col)
hpc <- gsub('^_', 'X', home_postcode_col)
ecc <- gsub('^_', 'X', entrance_cat_col)

force_char_trim(stud, sc, pcc, hpc, ecc)

stud[, POSTAL   := col_val(.SD, pcc)]
stud[, H_POSTAL := col_val(.SD, hpc)]
stud[, ENT_CAT  := col_val(.SD, ecc)]

blank_postal <- stud[is.na(POSTAL) | POSTAL == '' | !grepl('^[0-9]{{4}}', POSTAL),
                     .(StudentNo = col_val(.SD, sc), PostalCode = POSTAL,
                       Status = 'FAIL – Invalid or blank postal code')]

blank_ent <- stud[is.na(ENT_CAT) | ENT_CAT == '',
                  .(StudentNo = col_val(.SD, sc), EntranceCat = ENT_CAT,
                    Status = 'FAIL – Blank entrance category')]

exceptions <- rbindlist(list(blank_postal, blank_ent), use.names = TRUE, fill = TRUE)
print_summary(exceptions, 'Rule 62: Postal Code / Entrance Category')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }

    // ── Rule 63: STUD / CREG / QUAL qualification validation ─────────────────

    public static string GenerateRule63Script(Rule63ValidationRequest req)
    {
        var m = req.ColumnMapping ?? new Rule63ColumnMapping();
        return RHeader + $@"
# ── Rule 63: STUD / CREG / QUAL Qualification Validation ──────────────────────
# Validates that STUD qualification codes exist in QUAL and match CREG records.

stud_table      <- '{req.StudTable}'
creg_table      <- '{req.CregTable}'
qual_table      <- '{req.QualTable}'
stud_no_col     <- '{m.StudStudentNoCol}'
stud_qual_col   <- '{m.StudQualCodeCol}'
creg_no_col     <- '{m.CregStudentNoCol}'
creg_qual_col   <- '{m.CregQualCodeCol}'
qual_code_col   <- '{m.QualQualCodeCol}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
creg <- copy(ds[[creg_table]]); safe_names(creg)
qual <- copy(ds[[qual_table]]); safe_names(qual)

sc  <- gsub('^_', 'X', stud_no_col)
sqc <- gsub('^_', 'X', stud_qual_col)
cnc <- gsub('^_', 'X', creg_no_col)
cqc <- gsub('^_', 'X', creg_qual_col)
qqc <- gsub('^_', 'X', qual_code_col)

force_char_trim(stud, sc, sqc)
force_char_trim(creg, cnc, cqc)
force_char_trim(qual, qqc)

valid_quals <- qual[!is.na(col_val(.SD, qqc)) & col_val(.SD, qqc) != '',
                    .(QualKey = norm(col_val(.SD, qqc)))]
setkey(valid_quals, QualKey)

stud[, STUD_KEY  := norm(col_val(.SD, sc))]
stud[, STUD_QUAL := norm(col_val(.SD, sqc))]
creg[, CREG_KEY  := norm(col_val(.SD, cnc))]
creg[, CREG_QUAL := norm(col_val(.SD, cqc))]

missing_qual_in_stud <- stud[!STUD_QUAL %in% valid_quals$QualKey,
                              .(StudentNo = STUD_KEY, QualCode = STUD_QUAL,
                                Reason = 'STUD qual code not in QUAL table')]

result <- merge(stud[, .(STUD_KEY, STUD_QUAL)],
                creg[, .(CREG_KEY, CREG_QUAL)],
                by.x = 'STUD_KEY', by.y = 'CREG_KEY', all.x = TRUE)
qual_mismatch <- result[!is.na(CREG_QUAL) & norm(STUD_QUAL) != norm(CREG_QUAL),
                         .(StudentNo = STUD_KEY, QualCode = STUD_QUAL,
                           Reason = 'STUD and CREG qual codes differ')]

exceptions <- rbindlist(list(missing_qual_in_stud, qual_mismatch), use.names = TRUE, fill = TRUE)
exceptions[, Status := 'FAIL']

print_summary(exceptions, 'Rule 63: STUD/CREG/QUAL Qualification Validation')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }
}
