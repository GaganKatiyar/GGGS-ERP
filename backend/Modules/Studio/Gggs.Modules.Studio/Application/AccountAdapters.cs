using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Gggs.Modules.Insight.Contracts;

namespace Gggs.Modules.Studio.Application;

/// <summary>Sign-in names for a new account, in the order to try them.</summary>
public static partial class AccountNames
{
    public const string DefaultPattern = "{first}.{last}";

    private const int MaxSuffix = 5;

    /// <summary>
    /// The person's work email if it is already on this domain, then the pattern, then the pattern with
    /// 2, 3, 4 and 5 after it for a second person of the same name.
    /// </summary>
    public static IReadOnlyList<string> Logins(string? pattern, string domain, string firstName, string? lastName, string employeeCode, string? preferredLogin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        string host = domain.Trim().TrimStart('@').ToLowerInvariant();
        var logins = new List<string>();
        if (preferredLogin is { Length: > 0 } && preferredLogin.Trim().EndsWith($"@{host}", StringComparison.OrdinalIgnoreCase))
        {
            logins.Add(preferredLogin.Trim().ToLowerInvariant());
        }

        string first = Clean(firstName);
        string last = Clean(lastName);
        string local = Dots().Replace(
            (string.IsNullOrWhiteSpace(pattern) ? DefaultPattern : pattern.Trim()).ToLowerInvariant()
                .Replace("{first}", first, StringComparison.Ordinal)
                .Replace("{last}", last, StringComparison.Ordinal)
                .Replace("{f}", first.Length > 0 ? first[..1] : string.Empty, StringComparison.Ordinal)
                .Replace("{l}", last.Length > 0 ? last[..1] : string.Empty, StringComparison.Ordinal),
            ".").Trim('.', '-', '_');
        local = NotAllowed().Replace(local, string.Empty);
        if (local.Length == 0)
        {
            // A name written only in characters a sign-in name cannot hold still gets an account.
            local = Clean(employeeCode);
        }

        logins.Add($"{local}@{host}");
        for (int suffix = 2; suffix <= MaxSuffix; suffix++)
        {
            logins.Add($"{local}{suffix.ToString(CultureInfo.InvariantCulture)}@{host}");
        }

        return [.. logins.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Lower-case letters and digits only, with accents taken off: "José O'Brien" gives "jose", "obrien".</summary>
    private static string Clean(string? name)
    {
        var plain = new StringBuilder();
        foreach (char letter in (name ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(letter))
            {
                plain.Append(char.ToLowerInvariant(letter));
            }
        }

        return plain.ToString();
    }

    [GeneratedRegex(@"\.{2,}")]
    private static partial Regex Dots();

    [GeneratedRegex(@"[^a-z0-9._-]")]
    private static partial Regex NotAllowed();
}

/// <summary>Passwords good for one first sign-in. Nothing keeps them: they exist only until the email has gone.</summary>
public static class TemporaryPasswords
{
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!#%*-_=+?";

    /// <summary>Sixteen random characters, always with all four kinds that directories ask for, and none that are mistaken for one another.</summary>
    public static string Generate()
    {
        string all = Lower + Upper + Digits + Symbols;
        char[] password = new char[16];
        password[0] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        password[1] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        password[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        password[3] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];
        for (int index = 4; index < password.Length; index++)
        {
            password[index] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        RandomNumberGenerator.Shuffle<char>(password);
        return new string(password);
    }
}

/// <summary>
/// Microsoft 365 accounts through Microsoft Graph. The account carries the employee code, so trying
/// again after a failure finds the one already made instead of making a second.
/// </summary>
internal sealed class Microsoft365AccountsAdapter(IHttpClientFactory http) : IAccountAdapter
{
    private const string Graph = "https://graph.microsoft.com/v1.0";

    public string Key => "microsoft-365-accounts";

    public string Name => "Microsoft 365 accounts";

    public string Category => "Identity";

    public string Description => "Creates a Microsoft 365 account for each new joiner and disables it when they leave. The app registration needs the User.ReadWrite.All application permission.";

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("tenantId", "Directory (tenant) ID"),
        new("clientId", "Application (client) ID"),
        new("clientSecret", "Client secret", Secret: true),
        new("domain", "Email domain", Hint: "example.com, verified in Microsoft 365"),
        new("loginPattern", "Sign-in name pattern", Required: false, Hint: "{first}.{last} if left empty. Also {f} and {l} for initials"),
        new("usageLocation", "Usage location", Required: false, Hint: "Two-letter country code, e.g. IN. Needed to assign a licence"),
        new("licenseSkuId", "Licence SKU ID", Required: false, Hint: "A GUID from your subscriptions; no licence is assigned if left empty"),
    ];

    public IReadOnlyList<IntegrationOperation> Operations { get; } =
        [new("check", "Check an account", "Says whether an account exists and is enabled. Changes nothing.", [new("login", "Sign-in name", Hint: "priya.sharma@example.com")])];

    public static JsonObject NewUser(string login, AccountRequest account, string? usageLocation)
    {
        var user = new JsonObject
        {
            ["accountEnabled"] = true,
            ["displayName"] = account.DisplayName,
            ["givenName"] = account.FirstName,
            ["mailNickname"] = login[..login.IndexOf('@', StringComparison.Ordinal)],
            ["userPrincipalName"] = login,
            ["employeeId"] = account.EmployeeCode,
            ["passwordProfile"] = new JsonObject { ["forceChangePasswordNextSignIn"] = true, ["password"] = account.TemporaryPassword },
        };
        if (!string.IsNullOrWhiteSpace(account.LastName))
        {
            user["surname"] = account.LastName;
        }

        if (!string.IsNullOrWhiteSpace(usageLocation))
        {
            user["usageLocation"] = usageLocation.Trim().ToUpperInvariant();
        }

        return user;
    }

    public async Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        if (await TokenAsync(settings, cancellationToken) is not { } token)
        {
            return new IntegrationResult(false, "Microsoft refused the client credentials.");
        }

        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"{Graph}/users?$top=1&$select=id", token, null, cancellationToken);
        return response.IsSuccessStatusCode
            ? new IntegrationResult(true, "Signed in, and the app can read the directory. Only a real hire proves it may create accounts.")
            : new IntegrationResult(false, await StudioHttp.RefusalAsync(response, "Microsoft Graph", cancellationToken));
    }

    public async Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        if (await TokenAsync(settings, cancellationToken) is not { } token)
        {
            return new IntegrationResult(false, "Microsoft refused the client credentials.");
        }

        string login = parameters.GetValueOrDefault("login")?.Trim() ?? string.Empty;
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"{Graph}/users/{Uri.EscapeDataString(login)}?$select=displayName,accountEnabled", token, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new IntegrationResult(true, $"There is no account {login}.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new IntegrationResult(false, await StudioHttp.RefusalAsync(response, "Microsoft Graph", cancellationToken));
        }

        JsonElement user = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return new IntegrationResult(true, $"{login} exists and is {(user.TryGetProperty("accountEnabled", out JsonElement enabled) && enabled.ValueKind == JsonValueKind.True ? "enabled" : "disabled")}.");
    }

    public async Task<AccountOutcome> CreateAccountAsync(IReadOnlyDictionary<string, string> settings, AccountRequest account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (await TokenAsync(settings, cancellationToken) is not { } token)
        {
            return new AccountOutcome(false, "Microsoft refused the client credentials.");
        }

        string code = account.EmployeeCode.Replace("'", "''", StringComparison.Ordinal);
        using (HttpResponseMessage found = await SendAsync(HttpMethod.Get, $"{Graph}/users?$filter={Uri.EscapeDataString($"employeeId eq '{code}'")}&$select=id,userPrincipalName&$count=true", token, null, cancellationToken))
        {
            if (!found.IsSuccessStatusCode)
            {
                return new AccountOutcome(false, await StudioHttp.RefusalAsync(found, "Microsoft Graph", cancellationToken));
            }

            JsonElement users = (await found.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("value");
            if (users.GetArrayLength() > 0)
            {
                return new AccountOutcome(true, "An account with this employee code already exists, so it was linked as it is.", users[0].GetProperty("id").GetString(), users[0].GetProperty("userPrincipalName").GetString());
            }
        }

        foreach (string login in AccountNames.Logins(settings.GetValueOrDefault("loginPattern"), settings.GetValueOrDefault("domain") ?? string.Empty, account.FirstName, account.LastName, account.EmployeeCode, account.PreferredLogin))
        {
            using HttpResponseMessage created = await SendAsync(HttpMethod.Post, $"{Graph}/users", token, NewUser(login, account, settings.GetValueOrDefault("usageLocation")), cancellationToken);
            if (created.IsSuccessStatusCode)
            {
                string id = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetString()!;
                return new AccountOutcome(true, await LicenseAsync(settings, token, id, cancellationToken), id, login, Created: true);
            }

            string refusal = await StudioHttp.RefusalAsync(created, "Microsoft Graph", cancellationToken);
            if (!refusal.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return new AccountOutcome(false, refusal);
            }
        }

        return new AccountOutcome(false, "Every sign-in name to try for this person is already taken.");
    }

    /// <summary>Shuts the account and every open session; nothing is deleted.</summary>
    public async Task<IntegrationResult> DisableAccountAsync(IReadOnlyDictionary<string, string> settings, string externalId, CancellationToken cancellationToken = default)
    {
        if (await TokenAsync(settings, cancellationToken) is not { } token)
        {
            return new IntegrationResult(false, "Microsoft refused the client credentials.");
        }

        string id = Uri.EscapeDataString(externalId);
        using HttpResponseMessage response = await SendAsync(HttpMethod.Patch, $"{Graph}/users/{id}", token, new JsonObject { ["accountEnabled"] = false }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new IntegrationResult(false, await StudioHttp.RefusalAsync(response, "Microsoft Graph", cancellationToken));
        }

        using HttpResponseMessage sessions = await SendAsync(HttpMethod.Post, $"{Graph}/users/{id}/revokeSignInSessions", token, null, cancellationToken);
        return new IntegrationResult(true, sessions.IsSuccessStatusCode ? "Disabled, and signed out everywhere." : "Disabled, but open sessions could not be signed out. They end when their sign-in expires.");
    }

    private async Task<string> LicenseAsync(IReadOnlyDictionary<string, string> settings, string token, string userId, CancellationToken cancellationToken)
    {
        if (settings.GetValueOrDefault("licenseSkuId") is not { Length: > 0 } sku)
        {
            return "Created.";
        }

        var body = new JsonObject { ["addLicenses"] = new JsonArray { new JsonObject { ["skuId"] = sku.Trim() } }, ["removeLicenses"] = new JsonArray() };
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"{Graph}/users/{Uri.EscapeDataString(userId)}/assignLicense", token, body, cancellationToken);
        return response.IsSuccessStatusCode ? "Created, with its licence." : $"Created, but with no licence. {await StudioHttp.RefusalAsync(response, "Microsoft Graph", cancellationToken)}";
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Graph answers a filter on employeeId only with this advanced-query header.
        request.Headers.Add("ConsistencyLevel", "eventual");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.CreateClient(StudioHttp.ClientName).SendAsync(request, cancellationToken);
    }

    private async Task<string?> TokenAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        string tenant = Uri.EscapeDataString(settings.GetValueOrDefault("tenantId") ?? string.Empty);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.GetValueOrDefault("clientId") ?? string.Empty,
            ["client_secret"] = settings.GetValueOrDefault("clientSecret") ?? string.Empty,
            ["scope"] = "https://graph.microsoft.com/.default",
            ["grant_type"] = "client_credentials",
        });
        using HttpResponseMessage response = await http.CreateClient(StudioHttp.ClientName).PostAsync(new Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token"), content, cancellationToken);
        return response.IsSuccessStatusCode && (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).TryGetProperty("access_token", out JsonElement token) ? token.GetString() : null;
    }
}

/// <summary>
/// Google Workspace accounts through the Admin SDK Directory API, as a service account acting for an
/// administrator (domain-wide delegation). The account carries the employee code as an external id,
/// so trying again finds the one already made.
/// </summary>
internal sealed class GoogleWorkspaceAccountsAdapter(IHttpClientFactory http, TimeProvider timeProvider) : IAccountAdapter
{
    private const string Directory = "https://admin.googleapis.com/admin/directory/v1";
    private const string Scope = "https://www.googleapis.com/auth/admin.directory.user";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";

    public string Key => "google-workspace-accounts";

    public string Name => "Google Workspace accounts";

    public string Category => "Identity";

    public string Description => "Creates a Google Workspace account for each new joiner and suspends it when they leave, through a service account with domain-wide delegation.";

    public IReadOnlyList<IntegrationField> Fields { get; } =
    [
        new("serviceAccountKey", "Service account key (JSON)", Secret: true, Hint: "The whole key file, delegated the admin.directory.user scope"),
        new("adminEmail", "Act as administrator", Hint: "admin@example.com, a user-management administrator"),
        new("domain", "Email domain", Hint: "example.com"),
        new("orgUnit", "Organisational unit", Required: false, Hint: "/Staff; the top level if left empty"),
        new("loginPattern", "Sign-in name pattern", Required: false, Hint: "{first}.{last} if left empty. Also {f} and {l} for initials"),
    ];

    public IReadOnlyList<IntegrationOperation> Operations { get; } =
        [new("check", "Check an account", "Says whether an account exists and is suspended. Changes nothing.", [new("login", "Email address", Hint: "priya.sharma@example.com")])];

    public static JsonObject NewUser(string login, AccountRequest account, string? orgUnit)
    {
        var user = new JsonObject
        {
            ["primaryEmail"] = login,

            // Google requires a family name; someone with one name uses it for both.
            ["name"] = new JsonObject { ["givenName"] = account.FirstName, ["familyName"] = string.IsNullOrWhiteSpace(account.LastName) ? account.FirstName : account.LastName },
            ["password"] = account.TemporaryPassword,
            ["changePasswordAtNextLogin"] = true,
            ["externalIds"] = new JsonArray { new JsonObject { ["value"] = account.EmployeeCode, ["type"] = "organization" } },
        };
        if (!string.IsNullOrWhiteSpace(orgUnit))
        {
            user["orgUnitPath"] = orgUnit.Trim();
        }

        return user;
    }

    /// <summary>The signed request Google swaps for an access token, valid for an hour.</summary>
    public static string SignedAssertion(string clientEmail, string privateKeyPem, string adminEmail, DateTimeOffset now)
    {
        string header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        string claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = clientEmail,
            scope = Scope,
            aud = TokenUrl,
            sub = adminEmail,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddHours(1).ToUnixTimeSeconds(),
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        byte[] signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{claims}.{Encode(signature)}";
    }

    public async Task<IntegrationResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        (string? token, string? refused) = await TokenAsync(settings, cancellationToken);
        if (token is null)
        {
            return new IntegrationResult(false, refused ?? "Google refused the service account.");
        }

        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"{Directory}/users?domain={Uri.EscapeDataString(settings.GetValueOrDefault("domain") ?? string.Empty)}&maxResults=1", token, null, cancellationToken);
        return response.IsSuccessStatusCode
            ? new IntegrationResult(true, "Signed in, and the service account can read the directory.")
            : new IntegrationResult(false, await StudioHttp.RefusalAsync(response, "Google", cancellationToken));
    }

    public async Task<IntegrationResult> RunAsync(string operation, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        (string? token, string? refused) = await TokenAsync(settings, cancellationToken);
        if (token is null)
        {
            return new IntegrationResult(false, refused ?? "Google refused the service account.");
        }

        string login = parameters.GetValueOrDefault("login")?.Trim() ?? string.Empty;
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"{Directory}/users/{Uri.EscapeDataString(login)}", token, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new IntegrationResult(true, $"There is no account {login}.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new IntegrationResult(false, await StudioHttp.RefusalAsync(response, "Google", cancellationToken));
        }

        JsonElement user = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return new IntegrationResult(true, $"{login} exists and is {(user.TryGetProperty("suspended", out JsonElement suspended) && suspended.ValueKind == JsonValueKind.True ? "suspended" : "active")}.");
    }

    public async Task<AccountOutcome> CreateAccountAsync(IReadOnlyDictionary<string, string> settings, AccountRequest account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        (string? token, string? refused) = await TokenAsync(settings, cancellationToken);
        if (token is null)
        {
            return new AccountOutcome(false, refused ?? "Google refused the service account.");
        }

        string domain = settings.GetValueOrDefault("domain") ?? string.Empty;
        using (HttpResponseMessage found = await SendAsync(HttpMethod.Get, $"{Directory}/users?domain={Uri.EscapeDataString(domain)}&query={Uri.EscapeDataString($"externalId:'{account.EmployeeCode.Replace("'", string.Empty, StringComparison.Ordinal)}'")}&maxResults=1", token, null, cancellationToken))
        {
            if (!found.IsSuccessStatusCode)
            {
                return new AccountOutcome(false, await StudioHttp.RefusalAsync(found, "Google", cancellationToken));
            }

            JsonElement page = await found.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (page.TryGetProperty("users", out JsonElement users) && users.GetArrayLength() > 0)
            {
                return new AccountOutcome(true, "An account with this employee code already exists, so it was linked as it is.", users[0].GetProperty("id").GetString(), users[0].GetProperty("primaryEmail").GetString());
            }
        }

        foreach (string login in AccountNames.Logins(settings.GetValueOrDefault("loginPattern"), domain, account.FirstName, account.LastName, account.EmployeeCode, account.PreferredLogin))
        {
            using HttpResponseMessage created = await SendAsync(HttpMethod.Post, $"{Directory}/users", token, NewUser(login, account, settings.GetValueOrDefault("orgUnit")), cancellationToken);
            if (created.IsSuccessStatusCode)
            {
                return new AccountOutcome(true, "Created.", (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetString(), login, Created: true);
            }

            if (created.StatusCode != HttpStatusCode.Conflict)
            {
                return new AccountOutcome(false, await StudioHttp.RefusalAsync(created, "Google", cancellationToken));
            }
        }

        return new AccountOutcome(false, "Every sign-in name to try for this person is already taken.");
    }

    /// <summary>Suspends the account, which also ends its sessions; nothing is deleted.</summary>
    public async Task<IntegrationResult> DisableAccountAsync(IReadOnlyDictionary<string, string> settings, string externalId, CancellationToken cancellationToken = default)
    {
        (string? token, string? refused) = await TokenAsync(settings, cancellationToken);
        if (token is null)
        {
            return new IntegrationResult(false, refused ?? "Google refused the service account.");
        }

        using HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"{Directory}/users/{Uri.EscapeDataString(externalId)}", token, new JsonObject { ["suspended"] = true }, cancellationToken);
        return response.IsSuccessStatusCode ? new IntegrationResult(true, "Suspended.") : new IntegrationResult(false, await StudioHttp.RefusalAsync(response, "Google", cancellationToken));
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.CreateClient(StudioHttp.ClientName).SendAsync(request, cancellationToken);
    }

    /// <summary>Always asks Google's own token address: a key file's token_uri is tenant input and is not followed.</summary>
    private async Task<(string? Token, string? Refused)> TokenAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        string assertion;
        try
        {
            using JsonDocument key = JsonDocument.Parse(settings.GetValueOrDefault("serviceAccountKey") ?? string.Empty);
            assertion = SignedAssertion(key.RootElement.GetProperty("client_email").GetString()!, key.RootElement.GetProperty("private_key").GetString()!, settings.GetValueOrDefault("adminEmail") ?? string.Empty, timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or ArgumentException or CryptographicException or InvalidOperationException)
        {
            return (null, "The service account key is not a valid Google key file.");
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = assertion,
        });
        using HttpResponseMessage response = await http.CreateClient(StudioHttp.ClientName).PostAsync(new Uri(TokenUrl), content, cancellationToken);
        return response.IsSuccessStatusCode && (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).TryGetProperty("access_token", out JsonElement token)
            ? (token.GetString(), null)
            : (null, "Google refused the service account. Check the key and its domain-wide delegation.");
    }
}
