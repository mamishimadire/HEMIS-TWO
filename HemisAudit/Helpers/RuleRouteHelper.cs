namespace HemisAudit.Helpers
{
    public static class RuleRouteHelper
    {
        private static readonly int[] SupportedRuleNumbers =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 34, 35, 36, 37, 38, 39, 40, 41, 44, 45, 46, 47, 48, 51, 52, 53, 54, 55, 57, 58, 59, 60, 61, 62, 63, 64, 65
        };

        public static IReadOnlyList<int> GetSupportedRuleNumbers() => SupportedRuleNumbers;

        public static string GetControllerName(int ruleNumber) => ruleNumber switch
        {
            1 => "Rule10",
            2 => "Rule10",
            3 => "Rule10",
            4 => "Rule10",
            5 => "Rule10",
            6 => "Rule10",
            7 => "Rule10",
            8 => "Rule10",
            9 => "Rule10",
            10 => "Rule10",
            11 => "Rule11",
            12 => "Rule12",
            13 => "Rule13",
            14 => "Rule14",
            15 => "Rule15",
            16 => "Rule16",
            17 => "Rule17",
            18 => "Rule18",
            19 => "Rule19",
            20 => "Rule20",
            21 => "Rule21",
            22 => "Rule22",
            23 => "Rule23",
            24 => "Rule24",
            25 => "Rule25",
            26 => "Rule26",
            27 => "Rule27",
            28 => "Rule28",
            29 => "Rule29",
            30 => "Rule30",
            31 => "Rule31",
            32 => "Rule32",
            34 => "Rule34",
            35 => "Rule35",
            36 => "Rule36",
            _ => $"Rule{ruleNumber}"
        };

        public static string GetWorkspaceUrl(int ruleNumber, int? clientId = null)
        {
            var url = $"/Rule{ruleNumber}";
            return clientId.HasValue && clientId.Value > 0
                ? $"{url}?clientId={clientId.Value}"
                : url;
        }

        public static string GetRunUrl(int ruleNumber, int runId) => $"/Rule{ruleNumber}/Run/{runId}";
    }
}
