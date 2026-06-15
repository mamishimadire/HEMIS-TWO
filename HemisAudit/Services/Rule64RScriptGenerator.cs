using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule64RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule64ValidationRequest req)
    {
        var m = req.ColumnMapping ?? new Rule64ColumnMapping();

        var studTable = RString(req.StudTable);
        var cregTable = RString(req.CregTable);
        var prodTable = RString(req.ProdTable);

        var studNoCol = RString(m.StudStudentNoCol);
        var cregNoCol = RString(m.CregStudentNoCol);
        var studCompareCol = RString(m.StudCompareValueCol);
        var cregCompareCol = RString(m.CregCompareValueCol);
        var prodNoCol = RString(m.ProdStudentNoCol);

        return RScriptScaffold.BuildDataLoadingPrelude() + $@"
options(stringsAsFactors = FALSE)

norm <- function(x) toupper(trimws(as.character(x)))

safe_names <- function(dt) {{
  setnames(dt, old = names(dt), new = gsub('^_', 'X', names(dt)))
  invisible(dt)
}}

force_char_trim <- function(dt, cols) {{
  for (col in cols) {{
    if (col %in% names(dt)) {{
      set(dt, j = col, value = trimws(as.character(dt[[col]])))
      set(dt, i = which(is.na(dt[[col]]) | dt[[col]] == 'NA'), j = col, value = '')
    }}
  }}
  invisible(dt)
}}

col_val <- function(dt, col, default = '') {{
  if (!is.null(col) && nzchar(col) && col %in% names(dt)) {{
    trimws(as.character(dt[[col]]))
  }} else {{
    rep(default, nrow(dt))
  }}
}}

print_summary <- function(dt, rule_label) {{
  cat(sprintf('\n=== %s ===\n', rule_label))
  cat(sprintf('Total rows: %d\n', nrow(dt)))
  if ('ValidationResult' %in% names(dt)) {{
    tbl <- dt[, .N, by = ValidationResult][order(ValidationResult)]
    for (i in seq_len(nrow(tbl))) {{
      cat(sprintf('  %-6s : %d\n', tbl$ValidationResult[i], tbl$N[i]))
    }}
  }}
}}

stud_table  <- '{studTable}'
creg_table  <- '{cregTable}'
prod_table  <- '{prodTable}'
stud_no_col <- '{studNoCol}'
creg_no_col <- '{cregNoCol}'
stud_value_col <- '{studCompareCol}'
creg_value_col <- '{cregCompareCol}'
prod_no_col <- '{prodNoCol}'

output_file <- file.path(default_data_dir, 'Rule64_STUD_CREG_Student_Number_Validation.xlsx')

# ============================================================
# RULE 64: STUD to CREG Student Number Validation
# Logic:
#   1. Select non-blank student numbers from STUD.
#   2. Build a distinct student-number and compare-value reference from CREG.
#   3. Confirm whether a missing STUD student number also appears in STUD PRODUCTION.
#   4. PASS when STUD._007 exists in CREG._007 and STUD._001 matches CREG._001.
#   5. FAIL when STUD._007 is missing from CREG._007.
#   6. FAIL when STUD._007 exists in CREG._007 but STUD._001 differs from CREG._001.
#   7. FAIL note: the student should not appear in production when the student number is missing from CREG.
# ============================================================

STUD <- copy(ds[[stud_table]]); safe_names(STUD)
CREG <- copy(ds[[creg_table]]); safe_names(CREG)
PROD <- copy(ds[[prod_table]]); safe_names(PROD)

stud_no_key <- gsub('^_', 'X', stud_no_col)
creg_no_key <- gsub('^_', 'X', creg_no_col)
stud_value_key <- gsub('^_', 'X', stud_value_col)
creg_value_key <- gsub('^_', 'X', creg_value_col)
prod_no_key <- gsub('^_', 'X', prod_no_col)

force_char_trim(STUD, c(stud_no_key, stud_value_key))
force_char_trim(CREG, c(creg_no_key, creg_value_key))
force_char_trim(PROD, c(prod_no_key))

# ============================================================
# STEP 1: Build reference student numbers from CREG
# ============================================================
CregReference <- unique(
  data.table(
    StudentNo = norm(col_val(CREG, creg_no_key)),
    CregCompareValue = norm(col_val(CREG, creg_value_key))
  )[StudentNo != '']
)
CregReference[, CregStudentNo := StudentNo]
setkey(CregReference, StudentNo, CregCompareValue)

CregSummary <- CregReference[, .(
  CregStudentNo = CregStudentNo[1L],
  CregCompareValue = paste(unique(fifelse(CregCompareValue == '', '[BLANK]', CregCompareValue)), collapse = ', ')
), by = StudentNo]
setkey(CregSummary, StudentNo)

MatchReference <- unique(CregReference[, .(StudentNo, StudCompareValue = CregCompareValue)])
MatchReference[, HasExactValueMatch := TRUE]
setkey(MatchReference, StudentNo, StudCompareValue)

# ============================================================
# STEP 2: Build production reference student numbers
# ============================================================
ProdReference <- unique(
  data.table(
    StudentNo = norm(col_val(PROD, prod_no_key))
  )[StudentNo != '']
)
ProdReference[, ProdStudentNo := StudentNo]
setkey(ProdReference, StudentNo)

# ============================================================
# STEP 3: Build validation population from STUD
# ============================================================
StudPopulation <- unique(data.table(
  SourceTable = 'STUD',
  StudentNo = norm(col_val(STUD, stud_no_key)),
  StudCompareValue = norm(col_val(STUD, stud_value_key))
)[StudentNo != ''])

# ============================================================
# STEP 4: Validate STUD student numbers against CREG
# ============================================================
results <- merge(
  StudPopulation,
  CregSummary,
  by = 'StudentNo',
  all.x = TRUE,
  sort = FALSE
)
results <- merge(
  results,
  MatchReference,
  by = c('StudentNo', 'StudCompareValue'),
  all.x = TRUE,
  sort = FALSE
)
results <- merge(
  results,
  ProdReference,
  by = 'StudentNo',
  all.x = TRUE,
  sort = FALSE
)

results[is.na(CregStudentNo), CregStudentNo := '']
results[is.na(CregCompareValue), CregCompareValue := '']
results[is.na(HasExactValueMatch), HasExactValueMatch := FALSE]
results[is.na(ProdStudentNo), ProdStudentNo := '']
results[, CregCompareValue := fifelse(HasExactValueMatch, StudCompareValue, CregCompareValue)]
results[, ErrorCode := fifelse(
  CregStudentNo == '',
  'NOTE',
  fifelse(!HasExactValueMatch, 'MISMATCH', '')
)]
results[, ValidationResult := fifelse(CregStudentNo == '' | !HasExactValueMatch, 'FAIL', 'PASS')]
results[, ExceptionCategory := fifelse(
  ValidationResult == 'PASS',
  'PASS_FOUND_IN_CREG__VALUE_MATCH',
  fifelse(
    CregStudentNo != '',
    'VALUE_MISMATCH__FOUND_IN_CREG',
    fifelse(
      ProdStudentNo == '',
      'NOT_FOUND_IN_CREG__NOT_IN_PRODUCTION',
      'NOT_FOUND_IN_CREG__FOUND_IN_PRODUCTION'
    )
  )
)]
results[, ValidationExplanation := fifelse(
  CregStudentNo == '' & ProdStudentNo == '',
  paste0(
    'FAIL: STUD.', stud_no_col, ' student number ''', StudentNo,
    ''' was not found in CREG.', creg_no_col,
    '. Note: this student should not appear in production. Confirmation: it was not found in STUD PRODUCTION.', prod_no_col, '.'
  ),
  fifelse(
    CregStudentNo == '',
    paste0(
      'FAIL: STUD.', stud_no_col, ' student number ''', StudentNo,
      ''' was not found in CREG.', creg_no_col,
      '. Note: this student should not appear in production. Confirmation: it exists in STUD PRODUCTION.', prod_no_col, ' as ''', ProdStudentNo, '''.'
    ),
    paste0(
      fifelse(!HasExactValueMatch, 'FAIL', 'PASS'),
      ': STUD.', stud_no_col, ' student number ''', StudentNo,
      ''' exists in CREG.', creg_no_col,
      fifelse(
        !HasExactValueMatch,
        paste0(
          ', but STUD.', stud_value_col, ' value ''',
          fifelse(StudCompareValue == '', '[BLANK]', StudCompareValue),
          ''' does not match CREG.', creg_value_col, ' value(s) ''',
          fifelse(CregCompareValue == '', '[BLANK]', CregCompareValue),
          '''.'
        ),
        paste0(
          ' and STUD.', stud_value_col, ' value ''',
          fifelse(StudCompareValue == '', '[BLANK]', StudCompareValue),
          ''' matches CREG.', creg_value_col, ' value ''',
          fifelse(StudCompareValue == '', '[BLANK]', StudCompareValue),
          '''.'
        )
      )
    )
  )
)]
results[, Status := ValidationResult]
results[, SortBucket := fifelse(ValidationResult == 'FAIL', 0L, 1L)]
data.table::setorder(results, SortBucket, StudentNo, StudCompareValue)
results[, SortBucket := NULL]
results[, HasExactValueMatch := NULL]
results[, RowNumber := .I]

pass_rows <- results[ValidationResult == 'PASS']
exceptions <- results[ValidationResult == 'FAIL']
fail_rows <- copy(exceptions)

exception_breakdown <- results[, .(Count = .N), by = .(Category = ExceptionCategory)]
exception_breakdown[, Description := fifelse(
  Category == 'PASS_FOUND_IN_CREG__VALUE_MATCH',
  'Student No found in CREG and the compare values match',
  fifelse(
    Category == 'VALUE_MISMATCH__FOUND_IN_CREG',
    'Student No found in CREG but the STUD and CREG compare values differ',
    fifelse(
      Category == 'NOT_FOUND_IN_CREG__NOT_IN_PRODUCTION',
      'Student No not found in CREG and not found in STUD PRODUCTION',
      'Student No not found in CREG but found in STUD PRODUCTION'
    )
  )
)]
setcolorder(exception_breakdown, c('Category', 'Description', 'Count'))
setorder(exception_breakdown, -Count, Category)

summary_table <- data.table(
  Metric = c('Total Rows Tested', 'Pass Rows', 'Fail Rows', 'Exception Rate %', 'Overall Status'),
  Value = c(
    as.character(nrow(results)),
    as.character(nrow(pass_rows)),
    as.character(nrow(exceptions)),
    sprintf('%.2f', if (nrow(results) == 0) 0 else (nrow(exceptions) / nrow(results)) * 100),
    if (nrow(exceptions) == 0) 'PASS' else 'FAIL'
  )
)

# ============================================================
# STEP 5: Output
# ============================================================
cat('=== RULE 64 SUMMARY ===\n')
cat('Total rows tested: ', nrow(results), '\n', sep = '')
cat('Pass rows:         ', nrow(pass_rows), '\n', sep = '')
cat('Fail rows:         ', nrow(exceptions), '\n', sep = '')
cat('Overall status:    ', ifelse(nrow(exceptions) == 0, 'PASS', 'FAIL'), '\n\n', sep = '')

print_summary(results, 'Rule 64 Validation Results')

cat('\n=== RULE 64 EXCEPTION CATEGORY BREAKDOWN ===\n')
if (nrow(exception_breakdown) == 0) {{
  cat('No categories available.\n')
}} else {{
  print(exception_breakdown)
}}

cat('\n=== RULE 64 EXCEPTIONS ===\n')
if (nrow(exceptions) == 0) {{
  cat('No exceptions found.\n')
}} else {{
  print(exceptions[, .(
    RowNumber,
    SourceTable,
    StudentNo,
    CregStudentNo,
    StudCompareValue,
    CregCompareValue,
    ProdStudentNo,
    ExceptionCategory,
    ErrorCode,
    ValidationResult,
    ValidationExplanation
  )])
}}
";
    }
}
