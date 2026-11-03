using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Insight.Contracts;
using Gggs.Modules.Studio.Application;
using Gggs.Modules.Studio.Domain;
using Shouldly;

namespace Gggs.Platform.UnitTests;

/// <summary>Accounts in outside directories: names, passwords, an account's life, and what goes on the wire.</summary>
public sealed class StudioAccountTests
{
    private const string Password = "Temp-Pass-word-1";

    private static readonly DateTime Now = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static readonly AccountRequest Priya = new("EMP042", "Priya", "Sharma", "Priya Sharma", null, Password);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Stands in for a directory: answers by method and address, and keeps what it was sent.</summary>
    private sealed class Directory(Func<HttpMethod, string, string, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler, IHttpClientFactory
    {
        public List<(HttpMethod Method, string Url, string Body, string? Auth)> Requests { get; } = [];

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (url.Contains("/token", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"token1\"}", Encoding.UTF8, "application/json") };
            }

            Requests.Add((request.Method, url, body, request.Headers.Authorization?.ToString()));
            (HttpStatusCode status, string answerBody) = answer(request.Method, url, body);
            return new HttpResponseMessage(status) { Content = new StringContent(answerBody, Encoding.UTF8, "application/json") };
        }
    }

    private static Dictionary<string, string> Microsoft(string? sku = null)
    {
        var settings = new Dictionary<string, string> { ["tenantId"] = "t1", ["clientId"] = "c1", ["clientSecret"] = "s1", ["domain"] = "acme.example", ["usageLocation"] = "in" };
        if (sku is not null)
        {
            settings["licenseSkuId"] = sku;
        }

        return settings;
    }

    [Theory]
    [InlineData(null, "Priya", "Sharma", "priya.sharma@acme.example")]
    [InlineData("{f}{last}", "Priya", "Sharma", "psharma@acme.example")]
    [InlineData("{first}", "Priya", "Sharma", "priya@acme.example")]
    [InlineData(null, "José", "O'Brien", "jose.obrien@acme.example")]
    [InlineData(null, "Priya", null, "priya@acme.example")]
    [InlineData(null, "प्रिया", null, "emp042@acme.example")]
    public void Logins_FollowThePattern(string? pattern, string first, string? last, string expected)
        => AccountNames.Logins(pattern, "acme.example", first, last, "EMP042", null)[0].ShouldBe(expected);

    [Fact]
    public void Logins_OfferNumberedNames_ThenStop()
    {
        IReadOnlyList<string> logins = AccountNames.Logins(null, "@ACME.example", "Priya", "Sharma", "EMP042", null);

        logins.ShouldBe(["priya.sharma@acme.example", "priya.sharma2@acme.example", "priya.sharma3@acme.example", "priya.sharma4@acme.example", "priya.sharma5@acme.example"]);
    }

    [Fact]
    public void Logins_TryTheirWorkEmailFirst_OnlyIfItIsOnThisDomain()
    {
        AccountNames.Logins(null, "acme.example", "Priya", "Sharma", "E1", "P.Sharma@acme.example")[0].ShouldBe("p.sharma@acme.example");
        AccountNames.Logins(null, "acme.example", "Priya", "Sharma", "E1", "priya@gmail.example")[0].ShouldBe("priya.sharma@acme.example");
    }

    [Fact]
    public void TemporaryPasswords_AreStrong_AndDiffer()
    {
        string[] passwords = [.. Enumerable.Range(0, 200).Select(_ => TemporaryPasswords.Generate())];

        passwords.Distinct().Count().ShouldBe(200);
        passwords.ShouldAllBe(password => password.Length == 16
            && password.Any(char.IsLower) && password.Any(char.IsUpper) && password.Any(char.IsDigit) && password.Any(letter => !char.IsLetterOrDigit(letter)));
    }

    [Fact]
    public void AnAccountRequest_NeverPrintsThePassword()
    {
        Priya.ToString().ShouldNotContain(Password);
        Priya.ToString().ShouldContain("EMP042");
    }

    [Fact]
    public void AnAccount_RetriesWithBackoff_ThenWaitsForSomeone()
    {
        var account = ProvisionedAccount.Request(Guid.NewGuid(), Guid.NewGuid(), Now);
        account.Status.ShouldBe(AccountStatus.Creating);
        account.NeedsAttention.ShouldBeFalse();

        account.RecordFailure("Refused.", Now);
        account.NextAttemptAt.ShouldBe(Now.AddMinutes(1));
        for (int attempt = 2; attempt <= AutomationRules.MaxAttempts; attempt++)
        {
            account.RecordFailure("Refused.", Now);
        }

        account.NextAttemptAt.ShouldBeNull();
        account.NeedsAttention.ShouldBeTrue();

        account.RetryNow(Now);
        account.NextAttemptAt.ShouldBe(Now);

        account.MarkCreated("id1", "priya@acme.example", "Created.");
        account.Status.ShouldBe(AccountStatus.Active);
        account.Attempts.ShouldBe(0);
        Should.Throw<DomainRuleException>(() => account.RetryNow(Now)).ErrorCode.ShouldBe("ACCOUNT_SETTLED");
    }

    [Fact]
    public void Disabling_RetriesToo_AndNothingMadeMeansNothingToDo()
    {
        var made = ProvisionedAccount.Request(Guid.NewGuid(), Guid.NewGuid(), Now);
        made.MarkCreated("id1", "priya@acme.example", null);
        made.RequestDisable(Now, Now);
        made.Status.ShouldBe(AccountStatus.Disabling);
        made.RecordFailure("Refused.", Now);
        made.NextAttemptAt.ShouldBe(Now.AddMinutes(1));
        made.MarkDisabled("Disabled.", Now);
        made.Status.ShouldBe(AccountStatus.Disabled);
        made.DisabledAt.ShouldBe(Now);

        var neverMade = ProvisionedAccount.Request(Guid.NewGuid(), Guid.NewGuid(), Now);
        neverMade.RequestDisable(Now, Now);
        neverMade.Status.ShouldBe(AccountStatus.Disabled);
        neverMade.NextAttemptAt.ShouldBeNull();
        neverMade.LastMessage!.ShouldContain("nothing to disable");
    }

    [Fact]
    public async Task Microsoft365_MakesTheAccount_WithTheEmployeeCode_AndForcesAPasswordChange()
    {
        var directory = new Directory((method, url, _) => method == HttpMethod.Get
            ? (HttpStatusCode.OK, "{\"value\":[]}")
            : url.EndsWith("/assignLicense", StringComparison.Ordinal) ? (HttpStatusCode.OK, "{}") : (HttpStatusCode.Created, "{\"id\":\"user1\"}"));

        AccountOutcome outcome = await new Microsoft365AccountsAdapter(directory).CreateAccountAsync(Microsoft("sku1"), Priya, Ct);

        outcome.Succeeded.ShouldBeTrue();
        outcome.Created.ShouldBeTrue();
        outcome.ExternalId.ShouldBe("user1");
        outcome.Login.ShouldBe("priya.sharma@acme.example");
        outcome.Message.ShouldNotContain(Password);

        directory.Requests[0].Url.ShouldContain("employeeId%20eq%20%27EMP042%27");
        JsonNode user = JsonNode.Parse(directory.Requests[1].Body)!;
        user["userPrincipalName"]!.GetValue<string>().ShouldBe("priya.sharma@acme.example");
        user["mailNickname"]!.GetValue<string>().ShouldBe("priya.sharma");
        user["employeeId"]!.GetValue<string>().ShouldBe("EMP042");
        user["usageLocation"]!.GetValue<string>().ShouldBe("IN");
        user["passwordProfile"]!["password"]!.GetValue<string>().ShouldBe(Password);
        user["passwordProfile"]!["forceChangePasswordNextSignIn"]!.GetValue<bool>().ShouldBeTrue();
        JsonNode.Parse(directory.Requests[2].Body)!["addLicenses"]![0]!["skuId"]!.GetValue<string>().ShouldBe("sku1");
    }

    [Fact]
    public async Task Microsoft365_LinksTheAccountAlreadyCarryingTheCode_AndMakesNoSecond()
    {
        var directory = new Directory((_, _, _) => (HttpStatusCode.OK, "{\"value\":[{\"id\":\"old1\",\"userPrincipalName\":\"p.sharma@acme.example\"}]}"));

        AccountOutcome outcome = await new Microsoft365AccountsAdapter(directory).CreateAccountAsync(Microsoft(), Priya, Ct);

        outcome.Succeeded.ShouldBeTrue();
        outcome.Created.ShouldBeFalse("it keeps its own password, so no first-sign-in email goes out");
        outcome.ExternalId.ShouldBe("old1");
        directory.Requests.ShouldNotContain(request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Microsoft365_TakesTheNextNameWhenOneIsTaken_AndStopsOnAnyOtherRefusal()
    {
        var taken = new Directory((method, _, body) => method == HttpMethod.Get
            ? (HttpStatusCode.OK, "{\"value\":[]}")
            : body.Contains("\"priya.sharma@", StringComparison.Ordinal)
                ? (HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Another object with the same value for property userPrincipalName already exists.\"}}")
                : (HttpStatusCode.Created, "{\"id\":\"user2\"}"));
        (await new Microsoft365AccountsAdapter(taken).CreateAccountAsync(Microsoft(), Priya, Ct)).Login.ShouldBe("priya.sharma2@acme.example");

        var refused = new Directory((method, _, _) => method == HttpMethod.Get ? (HttpStatusCode.OK, "{\"value\":[]}") : (HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"Insufficient privileges\"}}"));
        AccountOutcome outcome = await new Microsoft365AccountsAdapter(refused).CreateAccountAsync(Microsoft(), Priya, Ct);
        outcome.Succeeded.ShouldBeFalse();
        outcome.Message.ShouldContain("Insufficient privileges");
        refused.Requests.Count(request => request.Method == HttpMethod.Post).ShouldBe(1);
    }

    [Fact]
    public async Task Microsoft365_ALicenceThatFails_LeavesTheAccountMade_AndSaysSo()
    {
        var directory = new Directory((method, url, _) => method == HttpMethod.Get
            ? (HttpStatusCode.OK, "{\"value\":[]}")
            : url.EndsWith("/assignLicense", StringComparison.Ordinal) ? (HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"No licences left\"}}") : (HttpStatusCode.Created, "{\"id\":\"user1\"}"));

        AccountOutcome outcome = await new Microsoft365AccountsAdapter(directory).CreateAccountAsync(Microsoft("sku1"), Priya, Ct);

        outcome.Succeeded.ShouldBeTrue();
        outcome.Message.ShouldContain("no licence");
        outcome.Message.ShouldContain("No licences left");
    }

    [Fact]
    public async Task Microsoft365_DisablesAndSignsOut_NeverDeletes()
    {
        var directory = new Directory((_, _, _) => (HttpStatusCode.NoContent, string.Empty));

        (await new Microsoft365AccountsAdapter(directory).DisableAccountAsync(Microsoft(), "user1", Ct)).Succeeded.ShouldBeTrue();

        directory.Requests[0].Method.ShouldBe(HttpMethod.Patch);
        JsonNode.Parse(directory.Requests[0].Body)!["accountEnabled"]!.GetValue<bool>().ShouldBeFalse();
        directory.Requests[1].Url.ShouldEndWith("/users/user1/revokeSignInSessions");
        directory.Requests.ShouldNotContain(request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task Microsoft365_CheckingAnAccount_ChangesNothing()
    {
        var missing = new Directory((_, _, _) => (HttpStatusCode.NotFound, "{}"));
        (await new Microsoft365AccountsAdapter(missing).RunAsync("check", Microsoft(), new Dictionary<string, string> { ["login"] = "nobody@acme.example" }, Ct)).Message.ShouldContain("no account");

        var enabled = new Directory((_, _, _) => (HttpStatusCode.OK, "{\"accountEnabled\":true}"));
        (await new Microsoft365AccountsAdapter(enabled).RunAsync("check", Microsoft(), new Dictionary<string, string> { ["login"] = "priya@acme.example" }, Ct)).Message.ShouldContain("enabled");
        enabled.Requests.ShouldAllBe(request => request.Method == HttpMethod.Get);
    }

    [Fact]
    public void Google_SignsItsRequest_WithTheKeyAndActsForTheAdministrator()
    {
        using var rsa = RSA.Create(2048);
        var now = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

        string assertion = GoogleWorkspaceAccountsAdapter.SignedAssertion("svc@project.example", rsa.ExportPkcs8PrivateKeyPem(), "admin@acme.example", now);

        string[] parts = assertion.Split('.');
        parts.Length.ShouldBe(3);
        static byte[] Decode(string part) => Convert.FromBase64String(part.Replace('-', '+').Replace('_', '/').PadRight(part.Length + ((4 - (part.Length % 4)) % 4), '='));
        rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).ShouldBeTrue();
        using JsonDocument claims = JsonDocument.Parse(Decode(parts[1]));
        claims.RootElement.GetProperty("iss").GetString().ShouldBe("svc@project.example");
        claims.RootElement.GetProperty("sub").GetString().ShouldBe("admin@acme.example");
        claims.RootElement.GetProperty("aud").GetString().ShouldBe("https://oauth2.googleapis.com/token");
        claims.RootElement.GetProperty("exp").GetInt64().ShouldBe(now.AddHours(1).ToUnixTimeSeconds());
    }

    private static Dictionary<string, string> Google()
    {
        using var rsa = RSA.Create(2048);
        return new Dictionary<string, string>
        {
            ["serviceAccountKey"] = JsonSerializer.Serialize(new { client_email = "svc@project.example", private_key = rsa.ExportPkcs8PrivateKeyPem(), token_uri = "https://evil.example/token" }),
            ["adminEmail"] = "admin@acme.example",
            ["domain"] = "acme.example",
            ["orgUnit"] = "/Staff",
        };
    }

    [Fact]
    public async Task Google_MakesTheAccount_TakingTheNextNameOnAConflict()
    {
        var directory = new Directory((method, _, body) => method == HttpMethod.Get
            ? (HttpStatusCode.OK, "{}")
            : body.Contains("\"priya.sharma@", StringComparison.Ordinal) ? (HttpStatusCode.Conflict, "{}") : (HttpStatusCode.OK, "{\"id\":\"g1\"}"));

        AccountOutcome outcome = await new GoogleWorkspaceAccountsAdapter(directory, TimeProvider.System).CreateAccountAsync(Google(), Priya, Ct);

        outcome.Succeeded.ShouldBeTrue();
        outcome.Created.ShouldBeTrue();
        outcome.Login.ShouldBe("priya.sharma2@acme.example");
        directory.Requests[0].Url.ShouldContain("query=externalId%3A%27EMP042%27");
        directory.Requests.ShouldAllBe(request => request.Url.StartsWith("https://admin.googleapis.com/", StringComparison.Ordinal), "a key file's own token address is not followed");
        JsonNode user = JsonNode.Parse(directory.Requests[^1].Body)!;
        user["password"]!.GetValue<string>().ShouldBe(Password);
        user["changePasswordAtNextLogin"]!.GetValue<bool>().ShouldBeTrue();
        user["orgUnitPath"]!.GetValue<string>().ShouldBe("/Staff");
        user["externalIds"]![0]!["value"]!.GetValue<string>().ShouldBe("EMP042");
    }

    [Fact]
    public async Task Google_SomeoneWithOneName_UsesItForBoth_AndAnExistingAccountIsLinked()
    {
        JsonObject user = GoogleWorkspaceAccountsAdapter.NewUser("priya@acme.example", Priya with { LastName = null }, null);
        user["name"]!["familyName"]!.GetValue<string>().ShouldBe("Priya");
        user.ContainsKey("orgUnitPath").ShouldBeFalse();

        var directory = new Directory((_, _, _) => (HttpStatusCode.OK, "{\"users\":[{\"id\":\"g9\",\"primaryEmail\":\"p@acme.example\"}]}"));
        AccountOutcome outcome = await new GoogleWorkspaceAccountsAdapter(directory, TimeProvider.System).CreateAccountAsync(Google(), Priya, Ct);
        outcome.Created.ShouldBeFalse();
        outcome.ExternalId.ShouldBe("g9");
    }

    [Fact]
    public async Task Google_Suspends_NeverDeletes_AndABadKeyIsSaidPlainly()
    {
        var directory = new Directory((_, _, _) => (HttpStatusCode.OK, "{}"));
        var adapter = new GoogleWorkspaceAccountsAdapter(directory, TimeProvider.System);

        (await adapter.DisableAccountAsync(Google(), "g1", Ct)).Succeeded.ShouldBeTrue();
        directory.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Put);
        JsonNode.Parse(directory.Requests[0].Body)!["suspended"]!.GetValue<bool>().ShouldBeTrue();

        var bad = new Dictionary<string, string> { ["serviceAccountKey"] = "not json" };
        (await adapter.TestAsync(bad, Ct)).Message.ShouldContain("not a valid Google key");
        (await adapter.CreateAccountAsync(bad, Priya, Ct)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void DirectoryConnectors_CannotBeUsedByAutomations()
    {
        var directory = new Directory((_, _, _) => (HttpStatusCode.OK, "{}"));
        IIntegrationAdapter[] adapters = [new Microsoft365AccountsAdapter(directory), new GoogleWorkspaceAccountsAdapter(directory, TimeProvider.System)];

        adapters.ShouldAllBe(adapter => !adapter.Automatable && adapter.Channel == null && adapter.Category == "Identity");
        adapters.ShouldAllBe(adapter => adapter.Operations.Single().Key == "check");
    }
}
