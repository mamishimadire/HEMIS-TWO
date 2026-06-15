using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule65RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule65ValidationRequest req)
    {
        var m = req.ColumnMapping ?? new Rule65ColumnMapping();

        var cancellationTable = RString(req.CancellationTable);
        var clientTable = RString(req.ClientTable);

        var studentNoCol = RString(m.StudentNoCol);
        var qualCol = RString(m.QualificationCol);
        var subjectCol = RString(m.SubjectCol);
        var cancelCol = RString(m.CancelDateCol);
        var censusCol = RString(m.CensusDateCol);
        var currentCensusCol = RString(m.CurrentCensusCol);

        return RScriptScaffold.BuildDataLoadingPrelude() + $@"
options(stringsAsFactors = FALSE)

safe_names <- function(dt) {{
  setnames(dt, old = names(dt), new = make.names(names(dt), unique = TRUE))
  invisible(dt)
}}

char_val <- function(dt, col, default = '') {{
  if (!nzchar(col)) return(rep(default, nrow(dt)))
  key <- make.names(col)
  if (!(key %in% names(dt))) return(rep(default, nrow(dt)))
  trimws(as.character(dt[[key]]))
}}

date_val <- function(x) {{
  suppressWarnings(as.Date(trimws(as.character(x))))
}}

category_desc <- function(category) {{
  switch(category,
    'PASS_NOT_ON_CENSUS' = 'Cancel date does not equal the row census date and does not match CURRENT_CENSUS',
    'CANCEL_EQUALS_CENSUS' = 'Cancel date equals the row census date',
    'CURRENT_CENSUS_MATCH' = 'Cancel date matches CURRENT_CENSUS in CENSUS_LIST_CLIENT',
    'CANCEL_EQUALS_CENSUS_AND_CURRENT_CENSUS' = 'Cancel date equals the row census date and matches CURRENT_CENSUS',
    'INVALID_CANCEL_DATE' = 'Cancel value could not be converted to a valid date',
    category
  )
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

cancellation_table <- '{cancellationTable}'
client_table <- '{clientTable}'
student_no_col <- '{studentNoCol}'
qual_col <- '{qualCol}'
subject_col <- '{subjectCol}'
cancel_col <- '{cancelCol}'
census_col <- '{censusCol}'
current_census_col <- '{currentCensusCol}'

output_file <- file.path(default_data_dir, 'Rule65_Cancellation_Census_Date_Validation.xlsx')

Cancellation <- copy(ds[[cancellation_table]]); safe_names(Cancellation)
ClientCensus <- copy(ds[[client_table]]); safe_names(ClientCensus)

current_census_dates <- unique(na.omit(date_val(char_val(ClientCensus, current_census_col))))

results <- unique(data.table(
  SourceTable = 'CANCELLATION LIST',
  StudentNo = char_val(Cancellation, student_no_col),
  Qualification = char_val(Cancellation, qual_col),
  Subject = char_val(Cancellation, subject_col),
  CancelDate = char_val(Cancellation, cancel_col),
  CensusDate = char_val(Cancellation, census_col)
))

results <- results[CancelDate != '']
results[, CancelDateParsed := date_val(CancelDate)]
results[, CensusDateParsed := date_val(CensusDate)]
results[, CurrentCensusParsed := fifelse(is.na(CancelDateParsed), as.Date(NA), CancelDateParsed)]
results[, CancelMatchesCurrentCensus := !is.na(CancelDateParsed) & CancelDateParsed %in% current_census_dates]
results[, CurrentCensus := fifelse(CancelMatchesCurrentCensus, format(CancelDateParsed, '%Y-%m-%d'), '')]

results[, ExceptionCategory := fifelse(
  is.na(CancelDateParsed),
  'INVALID_CANCEL_DATE',
  fifelse(
    !is.na(CensusDateParsed) & CancelDateParsed == CensusDateParsed & CancelMatchesCurrentCensus,
    'CANCEL_EQUALS_CENSUS_AND_CURRENT_CENSUS',
    fifelse(
      !is.na(CensusDateParsed) & CancelDateParsed == CensusDateParsed,
      'CANCEL_EQUALS_CENSUS',
      fifelse(
        CancelMatchesCurrentCensus,
        'CURRENT_CENSUS_MATCH',
        'PASS_NOT_ON_CENSUS'
      )
    )
  )
)]

results[, ErrorCode := fifelse(
  ExceptionCategory == 'INVALID_CANCEL_DATE',
  'INVALID_CANCEL_DATE',
  fifelse(
    ExceptionCategory == 'CANCEL_EQUALS_CENSUS_AND_CURRENT_CENSUS',
    'BOTH',
    fifelse(
      ExceptionCategory == 'CANCEL_EQUALS_CENSUS',
      'ROW_CENSUS',
      fifelse(ExceptionCategory == 'CURRENT_CENSUS_MATCH', 'CURRENT_CENSUS', '')
    )
  )
)]

results[, ValidationResult := fifelse(ExceptionCategory == 'PASS_NOT_ON_CENSUS', 'PASS', 'FAIL')]
results[, ValidationExplanation := fifelse(
  ExceptionCategory == 'INVALID_CANCEL_DATE',
  paste0('FAIL: CANCEL value ''', CancelDate, ''' could not be converted to a valid date.'),
  fifelse(
    ExceptionCategory == 'CANCEL_EQUALS_CENSUS_AND_CURRENT_CENSUS',
    paste0('FAIL: CANCEL date ''', format(CancelDateParsed, '%Y-%m-%d'), ''' equals the row CENSUS date and also matches CURRENT_CENSUS.'),
    fifelse(
      ExceptionCategory == 'CANCEL_EQUALS_CENSUS',
      paste0('FAIL: CANCEL date ''', format(CancelDateParsed, '%Y-%m-%d'), ''' equals the row CENSUS date ''', format(CensusDateParsed, '%Y-%m-%d'), '''.'),
      fifelse(
        ExceptionCategory == 'CURRENT_CENSUS_MATCH',
        paste0('FAIL: CANCEL date ''', format(CancelDateParsed, '%Y-%m-%d'), ''' matches CURRENT_CENSUS from CENSUS_LIST_CLIENT.'),
        paste0('PASS: CANCEL date ''', format(CancelDateParsed, '%Y-%m-%d'), ''' does not equal the row CENSUS date and does not appear in CURRENT_CENSUS.')
      )
    )
  )
)]

results[, CategoryDescription := vapply(ExceptionCategory, category_desc, character(1))]

flagged <- results[ValidationResult == 'FAIL']
clear <- results[ValidationResult == 'PASS']
breakdown <- results[, .(Count = .N, Description = category_desc(ExceptionCategory[1L])), by = ExceptionCategory][order(-Count, ExceptionCategory)]

print_summary(results, 'Rule 65: Cancellation Census Date Validation')

if (requireNamespace('openxlsx', quietly = TRUE)) {{
  wb <- openxlsx::createWorkbook()
  openxlsx::addWorksheet(wb, 'Summary')
  openxlsx::writeData(wb, 'Summary', data.frame(
    Field = c(
      'Cancellation Table', 'Client Table', 'Student No Column', 'Qualification Column', 'Subject Column',
      'Cancel Date Column', 'Census Date Column', 'Current Census Column', 'Total Rows', 'Clear Rows', 'Flagged Rows'
    ),
    Value = c(
      cancellation_table, client_table, student_no_col, qual_col, subject_col,
      cancel_col, census_col, current_census_col, nrow(results), nrow(clear), nrow(flagged)
    )
  ))
  openxlsx::addWorksheet(wb, 'Exception Breakdown')
  openxlsx::writeData(wb, 'Exception Breakdown', breakdown)
  openxlsx::addWorksheet(wb, 'Flagged Rows')
  openxlsx::writeData(wb, 'Flagged Rows', flagged)
  openxlsx::addWorksheet(wb, 'Clear Rows')
  openxlsx::writeData(wb, 'Clear Rows', clear)
  openxlsx::saveWorkbook(wb, output_file, overwrite = TRUE)
  cat(sprintf('Saved workbook: %s\n', output_file))
}} else {{
  cat('Package openxlsx is not installed; workbook export skipped.\n')
}}
";
    }
}
