namespace SignalCpf.UI.Helpers;

public sealed record CountryDialOption(string Name, string DialCode, string Iso)
{
    public string Display => $"{Name} ({DialCode})";
}

public static class CountryDialCodes
{
    public static IReadOnlyList<CountryDialOption> All { get; } =
    [
        new("United States", "+1", "US"),
        new("Canada", "+1", "CA"),
        new("United Kingdom", "+44", "GB"),
        new("China", "+86", "CN"),
        new("Hong Kong", "+852", "HK"),
        new("Taiwan", "+886", "TW"),
        new("Japan", "+81", "JP"),
        new("South Korea", "+82", "KR"),
        new("India", "+91", "IN"),
        new("Indonesia", "+62", "ID"),
        new("Malaysia", "+60", "MY"),
        new("Singapore", "+65", "SG"),
        new("Thailand", "+66", "TH"),
        new("Vietnam", "+84", "VN"),
        new("Philippines", "+63", "PH"),
        new("Australia", "+61", "AU"),
        new("New Zealand", "+64", "NZ"),
        new("Germany", "+49", "DE"),
        new("France", "+33", "FR"),
        new("Italy", "+39", "IT"),
        new("Spain", "+34", "ES"),
        new("Netherlands", "+31", "NL"),
        new("Belgium", "+32", "BE"),
        new("Switzerland", "+41", "CH"),
        new("Sweden", "+46", "SE"),
        new("Norway", "+47", "NO"),
        new("Denmark", "+45", "DK"),
        new("Finland", "+358", "FI"),
        new("Poland", "+48", "PL"),
        new("Russia", "+7", "RU"),
        new("Ukraine", "+380", "UA"),
        new("Turkey", "+90", "TR"),
        new("Saudi Arabia", "+966", "SA"),
        new("United Arab Emirates", "+971", "AE"),
        new("Israel", "+972", "IL"),
        new("Egypt", "+20", "EG"),
        new("South Africa", "+27", "ZA"),
        new("Nigeria", "+234", "NG"),
        new("Brazil", "+55", "BR"),
        new("Mexico", "+52", "MX"),
        new("Argentina", "+54", "AR"),
        new("Chile", "+56", "CL"),
        new("Colombia", "+57", "CO"),
        new("Peru", "+51", "PE"),
        new("Pakistan", "+92", "PK"),
        new("Bangladesh", "+880", "BD"),
        new("Ireland", "+353", "IE"),
        new("Portugal", "+351", "PT"),
        new("Austria", "+43", "AT"),
        new("Czechia", "+420", "CZ"),
        new("Greece", "+30", "GR"),
        new("Hungary", "+36", "HU"),
        new("Romania", "+40", "RO"),
        new("Macao", "+853", "MO"),
    ];

    public static CountryDialOption Default =>
        All.First(c => c.Iso == "CN");

    public static CountryDialOption? FindByDialCode(string? dialCode)
    {
        if (string.IsNullOrWhiteSpace(dialCode))
            return null;
        var code = dialCode.Trim();
        if (!code.StartsWith('+'))
            code = "+" + code.TrimStart('+');
        return All.FirstOrDefault(c => c.DialCode == code);
    }
}
