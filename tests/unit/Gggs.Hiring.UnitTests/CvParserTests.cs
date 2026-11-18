using Gggs.Modules.Recruitment.Domain;
using Shouldly;

namespace Gggs.Hiring.UnitTests;

/// <summary>Reading a CV's text into the Add a candidate form: what each rule picks out, and what it leaves alone.</summary>
public sealed class CvParserTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);

    private static readonly string[] Skills = ["Selenium", "Playwright", "Java", "TestNG", "API testing", "Postman", "SQL", "Jenkins", "Git", "Agile", "C#", ".NET", "Angular"];

    private const string Sample = """
        Riya Sharma
        Senior QA Automation Engineer
        Pune, Maharashtra | riya.sharma@example.com | +91 98765 43210
        linkedin.com/in/riya-sharma-qa

        SUMMARY
        QA automation engineer with 9 years of experience building test frameworks.

        EXPERIENCE
        Infosys Ltd — Senior QA Automation Engineer
        Jun 2021 – Present
        - Built a Playwright and Java framework covering 1,200 regression tests.

        Tata Consultancy Services — QA Engineer
        Jul 2017 – May 2021
        - Selenium WebDriver and TestNG suites; Jenkins pipelines.

        EDUCATION
        B.Tech, Computer Science, Pune University
        2013 – 2017

        SKILLS
        Selenium, Playwright, Java, TestNG, API testing, Postman, SQL, Jenkins, Git, Agile, Cucumber

        Current CTC: 12 LPA | Expected CTC: 16 LPA | Notice period: 60 days
        """;

    [Fact]
    public void A_typical_CV_fills_every_field()
    {
        CvFields cv = CvParser.Parse(Sample, Skills, [], Today);

        cv.FirstName.ShouldBe("Riya");
        cv.LastName.ShouldBe("Sharma");
        cv.Email.ShouldBe("riya.sharma@example.com");
        cv.Mobile.ShouldBe("+91 98765 43210");
        cv.LinkedInUrl.ShouldBe("https://linkedin.com/in/riya-sharma-qa");
        cv.CurrentCompany.ShouldBe("Infosys Ltd");
        cv.CurrentTitle.ShouldBe("Senior QA Automation Engineer");
        cv.ExperienceYears.ShouldBe(9.4m, "from Jul 2017; the degree's 2013 – 2017 is not a job");
        cv.NoticePeriodDays.ShouldBe(60);
        cv.CurrentCtc.ShouldBe(1_200_000m);
        cv.ExpectedCtc.ShouldBe(1_600_000m);
        cv.Location.ShouldBe("Pune");
        cv.Found.ShouldBe(13);
    }

    [Fact]
    public void Skills_take_the_organisations_own_names_then_the_rest_of_the_skills_line()
    {
        CvFields cv = CvParser.Parse(Sample, Skills, [], Today);

        cv.Skills.ShouldContain("API testing");
        cv.Skills.ShouldContain("Cucumber", "listed on the skills line though not in the catalogue");
        cv.Skills.ShouldNotContain("Angular");
        cv.Skills.Count(skill => skill.Equals("Selenium", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
    }

    [Theory]
    [InlineData("Current CTC: 9.5 lakhs", 950_000)]
    [InlineData("Current CTC - ₹18,00,000", 1_800_000)]
    [InlineData("Current CTC: 1.2 Cr", 12_000_000)]
    [InlineData("Current salary: 14", 1_400_000)]
    public void CTC_is_read_in_rupees_a_year(string line, int rupees)
        => CvParser.Parse("Asha Rao\n" + line, [], [], Today).CurrentCtc.ShouldBe(rupees);

    [Theory]
    [InlineData("Notice period: 2 months", 60)]
    [InlineData("Notice period - 30 days", 30)]
    [InlineData("Immediate joiner", 0)]
    public void Notice_period_is_read_in_days(string line, int days)
        => CvParser.Parse("Asha Rao\n" + line, [], [], Today).NoticePeriodDays.ShouldBe(days);

    [Fact]
    public void A_labelled_or_capitalised_name_is_read_as_written()
    {
        CvParser.Parse("CURRICULUM VITAE\nName: Arjun Nair\narjun@example.com", [], [], Today).FirstName.ShouldBe("Arjun");

        CvFields capitals = CvParser.Parse("ROHAN DAS\nrohan@example.com", [], [], Today);
        capitals.FirstName.ShouldBe("Rohan");
        capitals.LastName.ShouldBe("Das");
    }

    [Fact]
    public void An_office_the_organisation_has_is_found_as_a_location()
        => CvParser.Parse("Meera Iyer\nKannauj HQ, Uttar Pradesh", [], ["Kannauj HQ"], Today).Location.ShouldBe("Kannauj HQ");

    [Fact]
    public void Old_city_names_become_todays()
        => CvParser.Parse("Meera Iyer\nBangalore | meera@example.com", [], [], Today).Location.ShouldBe("Bengaluru");

    [Fact]
    public void Experience_falls_back_to_what_the_CV_states()
        => CvParser.Parse("Vikram Shah\n6.5 years of experience in payroll", [], [], Today).ExperienceYears.ShouldBe(6.5m);

    [Fact]
    public void A_CV_with_no_text_fills_nothing()
    {
        CvFields cv = CvParser.Parse(string.Empty, Skills, [], Today);

        cv.Found.ShouldBe(0);
        cv.Skills.ShouldBeEmpty();
    }
}
