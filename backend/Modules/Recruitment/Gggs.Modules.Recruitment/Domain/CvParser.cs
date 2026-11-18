using System.Globalization;
using System.Text.RegularExpressions;

namespace Gggs.Modules.Recruitment.Domain;

/// <summary>
/// What a CV gave for the Add a candidate form. Every field is a guess for a person to check: null
/// means the CV did not say, and the form leaves that field for them.
/// </summary>
public sealed record CvFields(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Mobile,
    string? CurrentCompany,
    string? CurrentTitle,
    decimal? ExperienceYears,
    int? NoticePeriodDays,
    decimal? CurrentCtc,
    decimal? ExpectedCtc,
    string? Location,
    string? LinkedInUrl,
    IReadOnlyList<string> Skills)
{
    /// <summary>How many of the form's thirteen fields the CV filled.</summary>
    public int Found =>
        new object?[] { FirstName, LastName, Email, Mobile, CurrentCompany, CurrentTitle, ExperienceYears, NoticePeriodDays, CurrentCtc, ExpectedCtc, Location, LinkedInUrl }.Count(value => value is not null)
        + (Skills.Count > 0 ? 1 : 0);
}

/// <summary>
/// Reads a CV's plain text into candidate fields with simple rules: no outside service, no cost per
/// CV. Built for the common Indian CV: contact details at the top, a work history with date ranges
/// (latest first or last), a skills line, and CTC and notice period written out. Pure, so the rules
/// are tested on their own; anything it is unsure of it leaves empty rather than guessing wrong.
/// </summary>
public static partial class CvParser
{
    /// <summary>Cities looked for near the top of a CV, besides the organisation's own offices.</summary>
    public static IReadOnlyList<string> Cities { get; } =
    [
        "Bengaluru", "Bangalore", "Mumbai", "Pune", "Hyderabad", "Chennai", "New Delhi", "Delhi", "Noida", "Gurugram", "Gurgaon", "Kolkata",
        "Ahmedabad", "Kanpur", "Lucknow", "Kannauj", "Jaipur", "Indore", "Chandigarh", "Kochi", "Coimbatore", "Nagpur", "Bhopal", "Vadodara",
        "Surat", "Thiruvananthapuram", "Visakhapatnam", "Bhubaneswar", "Mysuru", "Mysore", "Mohali", "Dehradun", "Patna", "Ranchi", "Goa",
        "Remote", "Dubai", "Singapore", "London",
    ];

    private static readonly Dictionary<string, string> CitySpellings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bangalore"] = "Bengaluru",
        ["Gurgaon"] = "Gurugram",
        ["Mysore"] = "Mysuru",
    };

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["feb"] = 2, ["mar"] = 3, ["apr"] = 4, ["may"] = 5, ["jun"] = 6,
        ["jul"] = 7, ["aug"] = 8, ["sep"] = 9, ["sept"] = 9, ["oct"] = 10, ["nov"] = 11, ["dec"] = 12,
    };

    private static readonly string[] NotNames = ["resume", "curriculum", "vitae", "profile", "cv", "summary", "contact"];

    public static CvFields Parse(string? text, IEnumerable<string> knownSkills, IEnumerable<string> knownPlaces, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(knownSkills);
        ArgumentNullException.ThrowIfNull(knownPlaces);
        string body = (text ?? string.Empty).Replace(' ', ' ');
        List<string> lines = [.. body.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];

        string? email = Email().Match(body) is { Success: true } e ? e.Value.ToLowerInvariant() : null;
        string? mobile = Mobile(body);
        string? linkedIn = LinkedIn().Match(body) is { Success: true } l ? (l.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? l.Value : "https://" + l.Value) : null;
        (string? first, string? last) = Name(lines);

        List<Job> jobs = Jobs(lines, today);
        Job? latest = jobs.OrderByDescending(job => job.End).ThenByDescending(job => job.Start).FirstOrDefault();
        decimal? experience = Experience(jobs, body, today);
        string? title = latest?.Title;
        if (title is null && lines.Count > 1 && lines[1].Length < 70 && !lines[1].Contains('@', StringComparison.Ordinal) && !Digits().IsMatch(lines[1]))
        {
            title = lines[1];
        }

        return new CvFields(
            first,
            last,
            email,
            mobile,
            latest?.Company,
            title,
            experience,
            Notice(body),
            Ctc(body, "current"),
            Ctc(body, "expected"),
            Location(lines, body, knownPlaces),
            linkedIn,
            Skills(body, lines, knownSkills));
    }

    private static string? Mobile(string body)
    {
        Match indian = IndianMobile().Match(body);
        if (indian.Success)
        {
            return Spaces().Replace(indian.Value, " ").Trim();
        }

        Match international = InternationalMobile().Match(body);
        if (!international.Success)
        {
            return null;
        }

        int digits = international.Value.Count(char.IsAsciiDigit);
        return digits is >= 7 and <= 15 ? Spaces().Replace(international.Value, " ").Trim() : null;
    }

    /// <summary>"Name: Riya Sharma", or the first short line of words only near the top.</summary>
    private static (string? First, string? Last) Name(List<string> lines)
    {
        string? name = lines.Take(8).Select(line => NameLabel().Match(line)).FirstOrDefault(match => match.Success)?.Groups[1].Value
            ?? lines.Take(5).FirstOrDefault(line =>
                NameLine().IsMatch(line)
                && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is >= 1 and <= 4
                && !NotNames.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)));
        if (name is null)
        {
            return (null, null);
        }

        // An all-capitals name reads better in title case: "RIYA SHARMA" becomes "Riya Sharma".
        if (name.Equals(name.ToUpperInvariant(), StringComparison.Ordinal))
        {
            name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        }

        string[] parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 ? string.Join(' ', parts[1..]) : null);
    }

    private sealed record Job(DateOnly Start, DateOnly End, string? Company, string? Title);

    /// <summary>Lines with a date range ("Jun 2021 – Present"), with the company and title from that line or the one above.</summary>
    private static List<Job> Jobs(List<string> lines, DateOnly today)
    {
        var jobs = new List<Job>();
        bool inEducation = false;
        for (int i = 0; i < lines.Count; i++)
        {
            // A degree's years ("2013 – 2017") are not a job: skip the education section.
            if (lines[i].Length <= 40 && EducationHeading().IsMatch(lines[i]))
            {
                inEducation = true;
                continue;
            }

            if (lines[i].Length <= 40 && OtherHeading().IsMatch(lines[i]))
            {
                inEducation = false;
                continue;
            }

            if (inEducation)
            {
                continue;
            }

            Match range = DateRange().Match(lines[i]);
            if (!range.Success || ParseDate(range.Groups[1].Value, today) is not { } start || ParseDate(range.Groups[2].Value, today) is not { } end || end < start)
            {
                continue;
            }

            string head = lines[i].Replace(range.Value, string.Empty, StringComparison.Ordinal).Trim(' ', '|', ',', '(', ')', '-', '–');
            if (head.Length < 3 && i > 0)
            {
                head = lines[i - 1];
            }

            string[] split = HeadSplit().Split(head, 2);
            string? company = Clean(split[0]);
            string? title = split.Length > 1 ? Clean(split[1]) : null;
            jobs.Add(new Job(start, end, company, title));
        }

        return jobs;
    }

    private static string? Clean(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim(' ', '|', ',', '-', '–', '—', ':');
        return trimmed.Length is >= 2 and <= 200 ? trimmed : null;
    }

    private static DateOnly? ParseDate(string text, DateOnly today)
    {
        string value = text.Trim();
        if (Present().IsMatch(value))
        {
            return today;
        }

        Match month = MonthYear().Match(value);
        if (month.Success)
        {
            string name = month.Groups[1].Value;
            if (Months.TryGetValue(name.Length >= 4 ? name[..4] : name, out int m) || Months.TryGetValue(name[..3], out m))
            {
                return new DateOnly(int.Parse(month.Groups[2].Value, CultureInfo.InvariantCulture), m, 1);
            }
        }

        Match numeric = NumericMonthYear().Match(value);
        if (numeric.Success && int.Parse(numeric.Groups[1].Value, CultureInfo.InvariantCulture) is >= 1 and <= 12 and var mm)
        {
            return new DateOnly(int.Parse(numeric.Groups[2].Value, CultureInfo.InvariantCulture), mm, 1);
        }

        return Year().Match(value) is { Success: true } year ? new DateOnly(int.Parse(year.Value, CultureInfo.InvariantCulture), 1, 1) : null;
    }

    /// <summary>From the earliest job to today, or "9 years of experience" when there is no dated history.</summary>
    private static decimal? Experience(List<Job> jobs, string body, DateOnly today)
    {
        if (jobs.Count > 0)
        {
            DateOnly earliest = jobs.Min(job => job.Start);
            decimal years = decimal.Round((today.DayNumber - earliest.DayNumber) / 365.25m, 1);
            if (years is > 0 and <= 60)
            {
                return years;
            }
        }

        return StatedExperience().Match(body) is { Success: true } stated && decimal.TryParse(stated.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) && value <= 60
            ? value
            : null;
    }

    private static int? Notice(string body)
    {
        Match notice = NoticePeriod().Match(body);
        if (notice.Success && int.TryParse(notice.Groups[1].Value, CultureInfo.InvariantCulture, out int amount))
        {
            int days = notice.Groups[2].Value.StartsWith("month", StringComparison.OrdinalIgnoreCase) ? amount * 30 : amount;
            return days is >= 0 and <= 365 ? days : null;
        }

        return ImmediateJoiner().IsMatch(body) ? 0 : null;
    }

    /// <summary>"Current CTC: 12 LPA", "Expected CTC - ₹16,00,000", "CTC 9.5 lakhs": always in rupees a year.</summary>
    private static decimal? Ctc(string body, string which)
    {
        Match match = Regex.Match(
            body,
            which + @"\s*(?:ctc|salary|package)?\s*(?:p\.?a\.?)?\s*[:\-–]?\s*(?:₹|rs\.?|inr)?\s*([\d.,]+)\s*(lpa|lakhs?|lacs?|l\b|cr\b|crores?|k\b)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
        {
            return null;
        }

        string unit = match.Groups[2].Value.ToLowerInvariant();
        decimal rupees = unit switch
        {
            "" when amount <= 200 => amount * 100_000,
            "" => amount,
            "k" => amount * 1_000,
            _ when unit.StartsWith("cr", StringComparison.Ordinal) => amount * 10_000_000,
            _ => amount * 100_000,
        };
        return rupees is > 0 and < 1_000_000_000 ? decimal.Round(rupees, 0) : null;
    }

    /// <summary>"Location: Pune", else an office or city named in the first lines (where contact details sit).</summary>
    private static string? Location(List<string> lines, string body, IEnumerable<string> knownPlaces)
    {
        List<string> places = [.. knownPlaces.Where(place => !string.IsNullOrWhiteSpace(place)).Select(place => place.Trim()).Concat(Cities).Distinct(StringComparer.OrdinalIgnoreCase)];
        Match labelled = LocationLabel().Match(body);
        string search = labelled.Success ? labelled.Groups[1].Value : string.Join(' ', lines.Take(8));
        string? place = places.FirstOrDefault(candidate => Regex.IsMatch(search, @"(?<![A-Za-z])" + Regex.Escape(candidate) + @"(?![A-Za-z])", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
        if (place is null && labelled.Success)
        {
            place = Clean(labelled.Groups[1].Value.Split(',', '|')[0]);
        }

        return place is null ? null : CitySpellings.GetValueOrDefault(place, place);
    }

    /// <summary>The organisation's own skill names found anywhere, then whatever else the skills line lists.</summary>
    private static List<string> Skills(string body, List<string> lines, IEnumerable<string> knownSkills)
    {
        var found = new List<string>();
        foreach (string skill in knownSkills.Where(skill => !string.IsNullOrWhiteSpace(skill)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Regex.IsMatch(body, @"(?<![A-Za-z0-9])" + Regex.Escape(skill.Trim()) + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                found.Add(skill.Trim());
            }
        }

        int heading = lines.FindIndex(line => SkillsHeading().IsMatch(line));
        if (heading >= 0)
        {
            string listed = SkillsHeading().Replace(lines[heading], string.Empty);
            if (listed.Trim().Length == 0 && heading + 1 < lines.Count)
            {
                listed = lines[heading + 1];
            }

            foreach (string item in listed.Split([',', '|', '•', ';', '·'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(25))
            {
                if (item.Length is >= 2 and <= 40 && !found.Contains(item, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(item);
                }
            }
        }

        return [.. found.Take(30)];
    }

    [GeneratedRegex(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<!\d)(?:\+?91[\s\-]?)?0?[6-9]\d{4}[\s\-]?\d{5}(?!\d)")]
    private static partial Regex IndianMobile();

    [GeneratedRegex(@"\+\d{1,3}[\s\-]?\(?\d{1,4}\)?[\d\s\-]{5,12}\d")]
    private static partial Regex InternationalMobile();

    [GeneratedRegex(@"(?:https?://)?(?:www\.)?linkedin\.com/in/[A-Za-z0-9\-_%]+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkedIn();

    [GeneratedRegex(@"^(?:full\s+)?name\s*[:\-]\s*([A-Za-z][A-Za-z .'\-]{1,60})$", RegexOptions.IgnoreCase)]
    private static partial Regex NameLabel();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z.'\-]*(?: [A-Za-z][A-Za-z.'\-]*){0,3}$")]
    private static partial Regex NameLine();

    [GeneratedRegex(@"([A-Za-z]{3,9}\.?\s*'?\d{4}|\d{1,2}[/\-]\d{4}|(?<!\d)\d{4}(?!\d))\s*(?:–|—|-|to|till)\s*(present|current|till date|now|date|[A-Za-z]{3,9}\.?\s*'?\d{4}|\d{1,2}[/\-]\d{4}|(?<!\d)\d{4}(?!\d))", RegexOptions.IgnoreCase)]
    private static partial Regex DateRange();

    [GeneratedRegex(@"^(present|current|till date|now|date)$", RegexOptions.IgnoreCase)]
    private static partial Regex Present();

    [GeneratedRegex(@"([A-Za-z]{3,9})\.?\s*'?(\d{4})")]
    private static partial Regex MonthYear();

    [GeneratedRegex(@"(\d{1,2})[/\-](\d{4})")]
    private static partial Regex NumericMonthYear();

    [GeneratedRegex(@"\d{4}")]
    private static partial Regex Year();

    [GeneratedRegex(@"\s+[—–\-|@]\s+|\s+at\s+|,\s+")]
    private static partial Regex HeadSplit();

    [GeneratedRegex(@"(\d{1,2}(?:\.\d)?)\+?\s*(?:years|yrs)", RegexOptions.IgnoreCase)]
    private static partial Regex StatedExperience();

    [GeneratedRegex(@"notice(?:\s+period)?\s*(?:of)?\s*[:\-–]?\s*(\d{1,3})\s*(days?|months?)", RegexOptions.IgnoreCase)]
    private static partial Regex NoticePeriod();

    [GeneratedRegex(@"immediate(?:ly)?\s+(?:joiner|available|joining)", RegexOptions.IgnoreCase)]
    private static partial Regex ImmediateJoiner();

    [GeneratedRegex(@"(?:current\s+)?location\s*[:\-–]\s*([^\n|]{2,60})", RegexOptions.IgnoreCase)]
    private static partial Regex LocationLabel();

    [GeneratedRegex(@"^(?:key\s+|technical\s+|core\s+)?skills?\b(?:\s+set)?\s*[:\-–]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SkillsHeading();

    [GeneratedRegex(@"^(?:education|academics?|academic\s+(?:details|qualifications?)|qualifications?|educational\s+qualifications?)\s*:?$", RegexOptions.IgnoreCase)]
    private static partial Regex EducationHeading();

    [GeneratedRegex(@"^(?:(?:work|professional|employment)\s+)?(?:experience|history)|^(?:projects?|skills?|certifications?|achievements?|awards?|summary|profile|personal\s+details)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherHeading();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\d{5,}")]
    private static partial Regex Digits();
}
