using Gggs.Modules.MasterData.Contracts;

namespace Gggs.Modules.MasterData.Application;

/// <summary>A list the platform knows about, and whether a tenant may add to it.</summary>
public sealed record LookupListDefinition(string Code, string Name, string? ParentList, bool TenantExtensible);

/// <summary>A value every new tenant starts with.</summary>
public sealed record LookupSeed(string Code, string Label, string? Parent = null);

/// <summary>
/// The lookup lists and their starting values, in code so they are reviewable in a diff.
/// <para>
/// Seeded into each tenant at provisioning (and topped up when this list grows): missing values are
/// added, existing ones - including a tenant's own edits to labels and order - are left alone.
/// </para>
/// </summary>
public static class LookupCatalog
{
    public static IReadOnlyList<LookupListDefinition> Lists { get; } =
    [
        new(LookupLists.Country, "Countries", null, TenantExtensible: false),
        new(LookupLists.State, "States and provinces", LookupLists.Country, TenantExtensible: true),
        new(LookupLists.Currency, "Currencies", null, TenantExtensible: false),
        new(LookupLists.EmploymentType, "Employment types", null, TenantExtensible: true),
        new(LookupLists.WorkMode, "Work modes", null, TenantExtensible: true),
        new(LookupLists.Gender, "Genders", null, TenantExtensible: true),
        new(LookupLists.MaritalStatus, "Marital statuses", null, TenantExtensible: true),
        new(LookupLists.BloodGroup, "Blood groups", null, TenantExtensible: false),
        new(LookupLists.Relationship, "Relationships", null, TenantExtensible: true),
        new(LookupLists.ExitReason, "Exit reasons", null, TenantExtensible: true),
        new(LookupLists.JobFamily, "Job families", null, TenantExtensible: true),
        new(LookupLists.Industry, "Industries", null, TenantExtensible: true),
    ];

    public static LookupListDefinition? Find(string listCode)
        => Lists.FirstOrDefault(list => string.Equals(list.Code, listCode, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyDictionary<string, IReadOnlyList<LookupSeed>> Seeds { get; } =
        new Dictionary<string, IReadOnlyList<LookupSeed>>(StringComparer.OrdinalIgnoreCase)
        {
            [LookupLists.Country] =
            [
                new("IN", "India"), new("US", "United States"), new("GB", "United Kingdom"),
                new("AE", "United Arab Emirates"), new("SG", "Singapore"), new("AU", "Australia"),
                new("CA", "Canada"), new("DE", "Germany"), new("FR", "France"), new("NL", "Netherlands"),
                new("IE", "Ireland"), new("JP", "Japan"), new("SA", "Saudi Arabia"), new("QA", "Qatar"),
                new("MY", "Malaysia"), new("PH", "Philippines"), new("LK", "Sri Lanka"), new("NP", "Nepal"),
                new("BD", "Bangladesh"), new("ZA", "South Africa"), new("NZ", "New Zealand"),
            ],
            [LookupLists.State] =
            [
                new("IN-AP", "Andhra Pradesh", "IN"), new("IN-AR", "Arunachal Pradesh", "IN"),
                new("IN-AS", "Assam", "IN"), new("IN-BR", "Bihar", "IN"), new("IN-CT", "Chhattisgarh", "IN"),
                new("IN-GA", "Goa", "IN"), new("IN-GJ", "Gujarat", "IN"), new("IN-HR", "Haryana", "IN"),
                new("IN-HP", "Himachal Pradesh", "IN"), new("IN-JH", "Jharkhand", "IN"),
                new("IN-KA", "Karnataka", "IN"), new("IN-KL", "Kerala", "IN"),
                new("IN-MP", "Madhya Pradesh", "IN"), new("IN-MH", "Maharashtra", "IN"),
                new("IN-MN", "Manipur", "IN"), new("IN-ML", "Meghalaya", "IN"), new("IN-MZ", "Mizoram", "IN"),
                new("IN-NL", "Nagaland", "IN"), new("IN-OR", "Odisha", "IN"), new("IN-PB", "Punjab", "IN"),
                new("IN-RJ", "Rajasthan", "IN"), new("IN-SK", "Sikkim", "IN"), new("IN-TN", "Tamil Nadu", "IN"),
                new("IN-TG", "Telangana", "IN"), new("IN-TR", "Tripura", "IN"),
                new("IN-UP", "Uttar Pradesh", "IN"), new("IN-UT", "Uttarakhand", "IN"),
                new("IN-WB", "West Bengal", "IN"), new("IN-AN", "Andaman and Nicobar Islands", "IN"),
                new("IN-CH", "Chandigarh", "IN"), new("IN-DH", "Dadra and Nagar Haveli and Daman and Diu", "IN"),
                new("IN-DL", "Delhi", "IN"), new("IN-JK", "Jammu and Kashmir", "IN"), new("IN-LA", "Ladakh", "IN"),
                new("IN-LD", "Lakshadweep", "IN"), new("IN-PY", "Puducherry", "IN"),
            ],
            [LookupLists.Currency] =
            [
                new("INR", "Indian Rupee"), new("USD", "US Dollar"), new("EUR", "Euro"), new("GBP", "Pound Sterling"),
                new("AED", "UAE Dirham"), new("SGD", "Singapore Dollar"), new("AUD", "Australian Dollar"),
                new("CAD", "Canadian Dollar"), new("JPY", "Japanese Yen"), new("SAR", "Saudi Riyal"),
            ],
            [LookupLists.EmploymentType] =
            [
                new("FULL_TIME", "Full-time"), new("PART_TIME", "Part-time"), new("CONTRACT", "Contract"),
                new("CONSULTANT", "Consultant"), new("INTERN", "Intern"), new("TEMPORARY", "Temporary"),
                new("APPRENTICE", "Apprentice"),
            ],
            [LookupLists.WorkMode] =
            [
                new("OFFICE", "Office"), new("REMOTE", "Remote"), new("HYBRID", "Hybrid"),
                new("CLIENT_SITE", "Client site"),
            ],
            [LookupLists.Gender] =
            [
                new("MALE", "Male"), new("FEMALE", "Female"), new("NON_BINARY", "Non-binary"),
                new("UNDISCLOSED", "Prefer not to say"),
            ],
            [LookupLists.MaritalStatus] =
            [
                new("SINGLE", "Single"), new("MARRIED", "Married"), new("SEPARATED", "Separated"),
                new("DIVORCED", "Divorced"), new("WIDOWED", "Widowed"),
            ],
            [LookupLists.BloodGroup] =
            [
                new("A_POS", "A+"), new("A_NEG", "A-"), new("B_POS", "B+"), new("B_NEG", "B-"),
                new("AB_POS", "AB+"), new("AB_NEG", "AB-"), new("O_POS", "O+"), new("O_NEG", "O-"),
            ],
            [LookupLists.Relationship] =
            [
                new("SPOUSE", "Spouse"), new("CHILD", "Child"), new("FATHER", "Father"), new("MOTHER", "Mother"),
                new("SIBLING", "Sibling"), new("PARTNER", "Partner"), new("GUARDIAN", "Guardian"), new("OTHER", "Other"),
            ],
            [LookupLists.ExitReason] =
            [
                new("RESIGNATION", "Resignation"), new("TERMINATION", "Termination"), new("RETIREMENT", "Retirement"),
                new("CONTRACT_END", "End of contract"), new("ABSCONDING", "Absconding"), new("DEATH", "Death"),
                new("MUTUAL", "Mutual separation"), new("RETRENCHMENT", "Retrenchment"),
            ],
            [LookupLists.JobFamily] =
            [
                new("ENGINEERING", "Engineering"), new("QUALITY", "Quality assurance"), new("PRODUCT", "Product"),
                new("DESIGN", "Design"), new("DATA", "Data and AI"), new("DEVOPS", "DevOps and infrastructure"),
                new("CONSULTING", "Consulting"), new("PROJECT_MGMT", "Project management"), new("SALES", "Sales"),
                new("MARKETING", "Marketing"), new("FINANCE", "Finance"), new("HR", "Human resources"),
                new("OPERATIONS", "Operations"), new("SUPPORT", "Customer support"), new("LEADERSHIP", "Leadership"),
            ],
            [LookupLists.Industry] =
            [
                new("IT_SERVICES", "IT services and consulting"), new("SOFTWARE", "Software products"),
                new("MANUFACTURING", "Manufacturing"), new("RETAIL", "Retail"), new("HEALTHCARE", "Healthcare"),
                new("EDUCATION", "Education"), new("CONSTRUCTION", "Construction"), new("LOGISTICS", "Logistics"),
                new("BPO", "BPO"), new("PROFESSIONAL_SERVICES", "Professional services"),
                new("TRADING", "Trading and distribution"), new("BFSI", "Banking, financial services and insurance"),
                new("OTHER", "Other"),
            ],
        };
}
