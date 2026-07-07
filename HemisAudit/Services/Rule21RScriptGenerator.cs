using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule21RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule21ValidationRequest req)
    {
        var studTable        = RString(req.StudTable);
        var qualTable        = RString(req.QualTable);
        var nalTable         = RString(req.NalTable);
        var fteCol           = RString(req.StudFirstTimeColumn ?? "_010");
        var fteValue         = RString(req.StudFirstTimeValue ?? "F");
        var stud007Col       = RString(req.Stud007Column ?? "_007");
        var studQualRefCol   = RString(req.StudQualRefColumn ?? "_001");
        var nalCategoryCol   = RString(req.NalCategoryColumn ?? "Category");
        var nalCategoryValue = RString(req.NalCategoryValue ?? "C");

        return RScriptScaffold.BuildDataLoadingPrelude() + $@"

norm <- function(x) toupper(trimws(as.character(x)))

safe_names <- function(dt) {{
  setnames(dt, old = names(dt), new = gsub('^_', 'X', names(dt)))
  invisible(dt)
}}

force_char_trim <- function(dt, cols) {{
  for (col in cols) if (col %in% names(dt))
    set(dt, j = col, value = trimws(as.character(dt[[col]])))
  invisible(dt)
}}

col_val <- function(dt, col, default = NA_character_) {{
  if (!is.null(col) && nchar(col) > 0 && col %in% names(dt)) dt[[col]] else rep(default, nrow(dt))
}}

print_summary <- function(dt, rule_label) {{
  cat(sprintf('\n-- %s --\n', rule_label))
  cat(sprintf('Total rows: %d\n', nrow(dt)))
  if ('Status' %in% names(dt)) {{
    tbl <- dt[, .N, by = Status]
    for (i in seq_len(nrow(tbl))) cat(sprintf('  %-16s: %d\n', tbl$Status[i], tbl$N[i]))
  }}
}}

stud_table         <- '{studTable}'
qual_table         <- '{qualTable}'
nal_table          <- '{nalTable}'
fte_col            <- '{fteCol}'
fte_value          <- '{fteValue}'
stud_007_col       <- '{stud007Col}'
stud_qual_ref_col  <- '{studQualRefCol}'
nal_category_col   <- '{nalCategoryCol}'
nal_category_value <- '{nalCategoryValue}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
nal  <- if (nchar(nal_table) > 0 && table_exists(nal_table)) copy(ds[[nal_table]]) else data.table()

sc   <- gsub('^_', 'X', stud_007_col)
ftc  <- gsub('^_', 'X', fte_col)
sqrc <- gsub('^_', 'X', stud_qual_ref_col)
force_char_trim(stud, c(sc, ftc, sqrc))

fte <- stud[norm(col_val(.SD, ftc)) == norm(fte_value)]
cat(sprintf('First-time entering students: %d\n', nrow(fte)))

if (nrow(nal) > 0) {{
  safe_names(nal)
  nalc <- gsub('^_', 'X', nal_category_col)
  nal_nonaligned <- nal[norm(col_val(.SD, nalc)) == norm(nal_category_value)]
  nal_quals <- unique(nal_nonaligned[, .(NAL_QUAL = norm(col_val(.SD, 'X001')))])
  fte[, QUAL := norm(col_val(.SD, sqrc))]
  exceptions <- fte[QUAL %in% nal_quals$NAL_QUAL,
                    .(StudentNo = col_val(.SD, sc), QualCode = QUAL, Status = 'FAIL - Linked to non-aligned qual')]
}} else {{
  cat('NAL table not provided; skipping non-aligned qualification check.\n')
  exceptions <- data.table()
}}

print_summary(exceptions, 'Rule 21: FTE vs Non-Aligned Quals')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }
}
