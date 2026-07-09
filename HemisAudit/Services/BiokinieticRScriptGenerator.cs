using System;

namespace HemisAudit.Services
{
    public static class BiokinieticRScriptGenerator
    {
        public static string Generate(string server, string database, string sourceTable, 
            string targetTable, string qualColumn, string surnameColumn)
        {
            return $@"
# Biokinetic Qualification & Surname Validation Script
# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
# Purpose: Validate QUALIFICATION values from Biokinetic table against Clinical_Production table

# ============================================================================
# CONFIGURATION SECTION
# ============================================================================

SERVER_NAME <- ""{server}""
DATABASE_NAME <- ""{database}""
SOURCE_TABLE <- ""{sourceTable}""
TARGET_TABLE <- ""{targetTable}""
QUALIFICATION_COL <- ""{qualColumn}""
SURNAME_COL <- ""{surnameColumn}""

# ============================================================================
# UTILITY FUNCTIONS
# ============================================================================

# Normalize text for comparison
norm <- function(x) {{
    if (is.na(x)) return(NA_character_)
    tolower(trimws(as.character(x)))
}}

# Safely access column value
col_val <- function(df, col_name, row_idx) {{
    if (col_name %in% names(df) && row_idx <= nrow(df)) {{
        return(df[[col_name]][row_idx])
    }}
    return(NA)
}}

# ============================================================================
# CONNECTION & DATA LOADING
# ============================================================================

library(RODBC)

# Establish connection
connection_string <- paste0(
    ""Driver={{ODBC Driver 17 for SQL Server}};Server="", SERVER_NAME, 
    "";Database="", DATABASE_NAME, 
    "";Trusted_Connection=yes;""
)

chan <- odbcDriverConnect(connection_string)

# Load source data (Biokinetic)
cat(""Loading source data from"", SOURCE_TABLE, ""...\\n"")
source_query <- paste0(
    ""SELECT ["", QUALIFICATION_COL, ""], ["", SURNAME_COL, ""] FROM ["", 
    SOURCE_TABLE, ""] ORDER BY ["", QUALIFICATION_COL, ""]""
)
source_data <- sqlQuery(chan, source_query)
cat(""Loaded"", nrow(source_data), ""rows from"", SOURCE_TABLE, ""\\n"")

# Load target data (Clinical_Production)
cat(""Loading reference data from"", TARGET_TABLE, ""...\\n"")
target_query <- paste0(
    ""SELECT DISTINCT ["", QUALIFICATION_COL, ""], ["", SURNAME_COL, ""] FROM ["", 
    TARGET_TABLE, ""] WHERE ["", QUALIFICATION_COL, ""] IS NOT NULL ORDER BY ["", QUALIFICATION_COL, ""]""
)
target_data <- sqlQuery(chan, target_query)
cat(""Loaded"", nrow(target_data), ""unique qualifications from"", TARGET_TABLE, ""\\n"")

# ============================================================================
# QUALIFICATION REFERENCE SET
# ============================================================================

# Create reference set from Clinical_Production
reference_qualifications <- unique(target_data[[QUALIFICATION_COL]])
reference_surnames <- target_data[[SURNAME_COL]]

cat(""\\nReference Set Summary:\\n"")
cat(""Total unique qualifications in"", TARGET_TABLE, "":"", length(reference_qualifications), ""\\n"")

# ============================================================================
# VALIDATION ANALYSIS
# ============================================================================

cat(""\\nPerforming Validation Analysis...\\n\\n"")

# Initialize results
results <- data.frame(
    BiokinieticQualification = character(),
    BiokinieticSurname = character(),
    Status = character(),
    ProductionQualification = character(),
    ProductionSurname = character(),
    stringsAsFactors = FALSE
)

# Process each source record
for (i in 1:nrow(source_data)) {{
    source_qual <- source_data[[QUALIFICATION_COL]][i]
    source_surname <- source_data[[SURNAME_COL]][i]
    
    # Check if qualification exists in reference set
    is_found <- FALSE
    matched_qual <- NA
    matched_surname <- NA
    
    if (!is.na(source_qual)) {{
        # Find match in target table
        match_idx <- which(target_data[[QUALIFICATION_COL]] == source_qual)
        
        if (length(match_idx) > 0) {{
            is_found <- TRUE
            matched_qual <- target_data[[QUALIFICATION_COL]][match_idx[1]]
            matched_surname <- target_data[[SURNAME_COL]][match_idx[1]]
        }}
    }}
    
    # Determine validation status
    validation_status <- if (is_found) ""PASS"" else ""FAIL""
    
    # Add to results
    results <- rbind(results, data.frame(
        BiokinieticQualification = source_qual,
        BiokinieticSurname = source_surname,
        Status = validation_status,
        ProductionQualification = matched_qual,
        ProductionSurname = matched_surname,
        stringsAsFactors = FALSE
    ))
}}

# ============================================================================
# SUMMARY STATISTICS
# ============================================================================

total_validated <- nrow(results)
pass_count <- sum(results$Status == ""PASS"", na.rm = TRUE)
fail_count <- sum(results$Status == ""FAIL"", na.rm = TRUE)
exception_rate <- if (total_validated > 0) (fail_count / total_validated * 100) else 0

cat(""\\n============================================\\n"")
cat(""VALIDATION SUMMARY REPORT\\n"")
cat(""============================================\\n"")
cat(""Total Records Validated: "", total_validated, ""\\n"")
cat(""PASS Count: "", pass_count, ""\\n"")
cat(""FAIL Count: "", fail_count, ""\\n"")
cat(""Exception Rate: "", round(exception_rate, 2), ""%\\n"")
cat(""Overall Status: "", if (fail_count == 0) ""PASS"" else ""FAIL"", ""\\n"")
cat(""============================================\\n\\n"")

# ============================================================================
# DETAILED RESULTS - FAILURES ONLY
# ============================================================================

failures <- subset(results, Status == ""FAIL"")

if (nrow(failures) > 0) {{
    cat(""Detailed Failure Analysis (showing first 20 failures):\\n"")
    cat(""---\\n"")
    
    display_count <- min(20, nrow(failures))
    for (i in 1:display_count) {{
        cat(""Row "", i, "":\\n"")
        cat(""  Biokinetic QUALIFICATION: "", failures$BiokinieticQualification[i], ""\\n"")
        cat(""  Biokinetic Surname: "", failures$BiokinieticSurname[i], ""\\n"")
        cat(""  Status: "", failures$Status[i], ""\\n"")
        cat(""  Note: QUALIFICATION not found in Clinical_Production table\\n\\n"")
    }}
    
    if (nrow(failures) > 20) {{
        cat(""... and "", nrow(failures) - 20, "" more failures\\n"")
    }}
}} else {{
    cat(""✓ All records passed validation!\\n\\n"")
}}

# ============================================================================
# CLEANUP
# ============================================================================

close(chan)
cat(""Validation analysis complete.\\n"")
";
        }
    }
}
