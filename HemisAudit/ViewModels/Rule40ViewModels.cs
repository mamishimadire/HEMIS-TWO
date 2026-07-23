namespace HemisAudit.ViewModels
{
    public class Rule40ColumnPair
    {
        public string ValpacCol { get; set; } = "";
        public string AsciiCol  { get; set; } = "";
        public string Label     { get; set; } = "";
    }

    public class Rule40FieldValue
    {
        public string ValpacValue { get; set; } = "—";
        public string AsciiValue  { get; set; } = "—";
        public string Match       { get; set; } = "AGREE";
    }

    public class Rule40ReconcRow
    {
        public int    RowNumber      { get; set; }
        public string StaffNumber    { get; set; } = "";
        public string OverallResult  { get; set; } = "AGREE";
        public string DisagreeDetail { get; set; } = "";
        public Dictionary<string, Rule40FieldValue> Fields { get; set; } = new();
    }

    public class Rule40ValidationRequest
    {
        public int    ClientId    { get; set; }
        public int?   RunId       { get; set; }
        public string Server      { get; set; } = "";
        public string Database    { get; set; } = "";
        public string Driver      { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ValpacTable { get; set; } = "";
        public string AsciiTable  { get; set; } = "";
        public List<Rule40ColumnPair>? Pairs { get; set; }
    }

    public class Rule40ValidationSummary
    {
        public bool    Success               { get; set; }
        public string? Error                 { get; set; }
        public int?    SavedRunId            { get; set; }
        public int     ClientId              { get; set; }
        public string  Status               { get; set; } = "";
        public string  Timestamp            { get; set; } = "";
        public string  Server               { get; set; } = "";
        public string  Database             { get; set; } = "";
        public string  ValpacTable          { get; set; } = "";
        public string  AsciiTable           { get; set; } = "";
        public int     TotalCount           { get; set; }
        public int     AgreeCount           { get; set; }
        public int     DisagreeCount        { get; set; }
        public int     MissingInAsciiCount  { get; set; }
        public int     MissingInValpacCount { get; set; }
        public decimal ExceptionRate        { get; set; }
        public List<Rule40ColumnPair> Pairs      { get; set; } = new();
        public List<Rule40ReconcRow>  ReviewRows  { get; set; } = new();
        public List<Rule40ReconcRow>  AgreeSample { get; set; } = new();
    }

    public class Rule40VerifyRequest
    {
        public string Server      { get; set; } = "";
        public string Database    { get; set; } = "";
        public string Driver      { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ValpacTable { get; set; } = "";
        public string AsciiTable  { get; set; } = "";
    }

    public class Rule40VerifyResult
    {
        public bool   Success     { get; set; }
        public int    ValpacCount { get; set; }
        public int    AsciiCount  { get; set; }
        public string? Error      { get; set; }
    }

    public class Rule40TableDiscoveryResult
    {
        public bool         Success         { get; set; }
        public List<string> Tables          { get; set; } = new();
        public string?      AutoValpacTable { get; set; }
        public string?      AutoAsciiTable  { get; set; }
        public string?      Error           { get; set; }
    }

    public class Rule40WorkspaceState
    {
        public int       ClientId                  { get; set; }
        public int?      RunId                     { get; set; }
        public string    Server                    { get; set; } = "";
        public string    Database                  { get; set; } = "";
        public string    Driver                    { get; set; } = "ODBC Driver 17 for SQL Server";
        public string    ValpacTable               { get; set; } = "";
        public string    AsciiTable                { get; set; } = "";
        public string    CurrentStatus             { get; set; } = "";
        public bool      HasDataAnalystSignoff     { get; set; }
        public bool      CurrentUserHasSignedOff   { get; set; }
        public string    CurrentUserSignoffComment { get; set; } = "";
        public string    CurrentUserEngagementRole { get; set; } = "";
        public bool      IsWorkspaceSaved          { get; set; }
        public bool      ResultsVisible            { get; set; }
        public string?   LastEditedByUserName      { get; set; }
        public DateTime? LastEditedAt              { get; set; }
        public Rule40ValidationSummary? Summary    { get; set; }
    }

    public class Rule40SignoffInput
    {
        public int    ClientId { get; set; }
        public int?   RunId    { get; set; }
        public string Comment  { get; set; } = "";
    }

    public class Rule40GetColumnsRequest
    {
        public string Server    { get; set; } = "";
        public string Database  { get; set; } = "";
        public string Driver    { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
    }
}
