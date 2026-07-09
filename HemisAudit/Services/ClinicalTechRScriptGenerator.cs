namespace HemisAudit.Services
{
    public static class ClinicalTechRScriptGenerator
    {
        public static string Generate(int ruleNumber, string ruleTitle, string clinicalTechTable, string prodTable, string qualColumn, string surnameColumn)
        {
            return $@"#!/usr/bin/env Rscript
# ══════════════════════════════════════════════════════════════════════════════
# HEMIS Rule {ruleNumber}: {ruleTitle}
# ══════════════════════════════════════════════════════════════════════════════
# Generated automatically; do not edit manually.
# 
# Purpose:
#   Validate that QUALIFICATION values from the Clinicaltech table
#   exist in the Clinical Production table, and confirm the Surname.
#
# Logic:
#   1. Load distinct QUALIFICATION codes from Production table
#   2. Load all records from Clinicaltech table
#   3. Left-join Clinicaltech to Production on QUALIFICATION
#   4. Mark PASS when qualification exists, FAIL when missing
#
# ══════════════════════════════════════════════════════════════════════════════

library(data.table)
library(dplyr, warn.conflicts = FALSE)

# ════════════════════════════════════════════════════════════════════════════
# CONFIGURATION
# ════════════════════════════════════════════════════════════════════════════

clinical_table    <- '{clinicalTechTable}'
prod_table        <- '{prodTable}'
qual_col          <- '{qualColumn}'
surname_col       <- '{surnameColumn}'

# ════════════════════════════════════════════════════════════════════════════
# UTILITY FUNCTIONS
# ════════════════════════════════════════════════════════════════════════════

# Normalize text: uppercase, trim, handle NA
norm <- function(x) {{
  if (is.na(x)) return('')
  return(trimws(toupper(as.character(x))))
}}

# Safe column value extraction
col_val <- function(dt, col_name) {{
  if (col_name %in% names(dt)) return(dt[[col_name]])
  return(rep('', nrow(dt)))
}}

# Table exists check
table_exists <- function(name) {{
  tryCatch(exists(name) && is.data.frame(get(name)), error = function(e) FALSE)
}}

# ════════════════════════════════════════════════════════════════════════════
# MAIN ANALYSIS
# ════════════════════════════════════════════════════════════════════════════

cat(sprintf('\n%-60s\n', '= HEMIS Rule {ruleNumber}: {ruleTitle} ='))
cat(sprintf('%-60s\n', ''))

# Load tables from data source
if (!table_exists(clinical_table)) {{
  stop(sprintf('Table %s not found in data source.', clinical_table))
}}
if (!table_exists(prod_table)) {{
  stop(sprintf('Table %s not found in data source.', prod_table))
}}

clinical_dt <- as.data.table(get(clinical_table))
prod_dt     <- as.data.table(get(prod_table))

cat(sprintf('Clinicaltech table rows: %d\n', nrow(clinical_dt)))
cat(sprintf('Production table rows:   %d\n', nrow(prod_dt)))

# Build reference from Production table (distinct qualifications)
ref_quals <- unique(
  data.table(
    QualCode = norm(col_val(prod_dt, qual_col)),
    Surname  = norm(col_val(prod_dt, surname_col))
  )
)[QualCode != '']

setkey(ref_quals, QualCode)
cat(sprintf('Distinct qualifications in Production: %d\n', nrow(ref_quals)))

# Validate Clinicaltech records
validation_dt <- clinical_dt[, .(
  ClinicalQual = norm(col_val(.SD, qual_col)),
  ClinicalSurname = norm(col_val(.SD, surname_col))
)]

validation_dt <- validation_dt[ClinicalQual != '']
validation_dt[, QualKey := ClinicalQual]
setkey(validation_dt, QualKey)

validation_dt <- ref_quals[validation_dt, on = 'QualCode==QualKey', allow.cartesian = FALSE]
validation_dt[, Status := fifelse(is.na(QualCode), 'FAIL - Qual not in Production', 'PASS')]

cat(sprintf('Total validated records: %d\n', nrow(validation_dt)))

# Summary statistics
pass_count <- sum(validation_dt\$Status == 'PASS', na.rm = TRUE)
fail_count <- sum(validation_dt\$Status == 'FAIL - Qual not in Production', na.rm = TRUE)

cat(sprintf('PASS records: %d\n', pass_count))
cat(sprintf('FAIL records: %d\n', fail_count))

if (nrow(validation_dt) > 0) {{
  exception_rate <- round(fail_count * 100 / nrow(validation_dt), 2)
  cat(sprintf('Exception rate: %.2f%%\n', exception_rate))
}}

# Display sample of failures
failure_sample <- validation_dt[Status != 'PASS'][1:10]
if (nrow(failure_sample) > 0) {{
  cat(sprintf('\n%-20s %-50s\n', 'Clinical Qual', 'Status'))
  cat(sprintf('%s\n', paste(rep('-', 70), collapse = '')))
  for (i in seq_len(min(10, nrow(failure_sample)))) {{
    row <- failure_sample[i]
    cat(sprintf('%-20s %-50s\n', row\$ClinicalQual, row\$Status))
  }}
}}

cat(sprintf('\n%-60s\n', '= Analysis Complete ='))
";
        }
    }
}
