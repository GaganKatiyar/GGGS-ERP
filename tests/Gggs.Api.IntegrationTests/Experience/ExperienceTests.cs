using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Gggs.Modules.Separation.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Gggs.Api.IntegrationTests.DeliveryApi;
using static Gggs.Api.IntegrationTests.PlatformServicesApi;

namespace Gggs.Api.IntegrationTests;

/// <summary>
/// M10 end to end: the asset register, expense claims and travel through reimbursement and
/// settlement, the helpdesk with SLA and a knowledge base, anonymous surveys with eNPS, rewards
/// with a points wallet and catalogue, and separation from resignation to settlement and alumni.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class ExperienceTests(PeopleOsApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string, DataScope)[] Employee =
    [
        (Permissions.Self.AssetView, DataScope.Own),
        (Permissions.Self.ExpenseClaimCreate, DataScope.Own),
        (Permissions.Self.TravelRequestCreate, DataScope.Own),
        (Permissions.Self.TicketCreate, DataScope.Own),
        (Permissions.Self.SurveyRespond, DataScope.Own),
        (Permissions.Self.RewardView, DataScope.Own),
        (Permissions.Self.RecognitionGive, DataScope.Own),
        (Permissions.Self.ResignationSubmit, DataScope.Own),
    ];

    private async Task<(HttpClient Admin, Guid Department, Guid Person, HttpClient Self)> PersonAsync()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid person = await CoreHrApi.HireAsync(admin, department);
        HttpClient self = await factory.CreateScopedUserClientAsync(Email("emp"), person, Employee);
        return (admin, department, person, self);
    }

    private static async Task<Guid> CategoryAsync(HttpClient client, string path, string code)
        => (await client.GetFromJsonAsync<JsonElement[]>(path, Ct))!.First(row => row.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();

    private static async Task<Guid> AssignLaptopAsync(HttpClient admin, Guid employee, decimal cost)
    {
        Guid laptop = await CategoryAsync(admin, "/api/v1/assets/categories", "LAPTOP");
        JsonElement registered = await OkAsync(await admin.PostAsJsonAsync("/api/v1/assets", new
        {
            Details = new { CategoryId = laptop, Name = "ThinkPad T14", Make = "Lenovo", Model = "T14", SerialNumber = $"SN-{Guid.NewGuid():N}"[..14], PurchaseDate = Iso(Today.AddMonths(-6)), PurchaseCost = cost },
        }, Ct));
        Guid id = registered.GetProperty("asset").GetProperty("id").GetGuid();
        await OkAsync(await admin.PostAsJsonAsync($"/api/v1/assets/{id}/assign", new { EmployeeId = employee }, Ct));
        return id;
    }

    [Fact]
    public async Task An_asset_is_assigned_seen_by_its_holder_and_returned()
    {
        (HttpClient admin, _, Guid person, HttpClient self) = await PersonAsync();
        using (admin)
        using (self)
        {
            Guid asset = await AssignLaptopAsync(admin, person, 90000m);
            JsonElement[] mine = (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/assets", Ct))!;
            mine.ShouldContain(row => row.GetProperty("id").GetGuid() == asset);

            JsonElement returned = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/assets/{asset}/return", new { Condition = "Good", Note = "Back in stock" }, Ct));
            returned.GetProperty("asset").GetProperty("status").GetString().ShouldBe("InStock");
            (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/me/assets", Ct))!.ShouldNotContain(row => row.GetProperty("id").GetGuid() == asset);
        }
    }

    [Fact]
    public async Task A_claim_is_checked_against_policy_approved_and_reimbursed_through_payroll()
    {
        (HttpClient admin, _, Guid person, HttpClient self) = await PersonAsync();
        using (admin)
        using (self)
        {
            Guid meals = await CategoryAsync(self, "/api/v1/expenses/categories", "MEALS");
            JsonElement draft = await OkAsync(await self.PostAsJsonAsync("/api/v1/me/expenses", new { Title = "Client visit, Pune" }, Ct));
            Guid claim = draft.GetProperty("claim").GetProperty("id").GetGuid();

            object over = new { CategoryId = meals, Date = Iso(Today), Description = "Team dinner", Amount = 2400m, HasReceipt = true, Billable = false };
            HttpResponseMessage refused = await self.PutAsJsonAsync($"/api/v1/me/expenses/{claim}/lines", new[] { over }, Ct);
            refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await CoreHrApi.ErrorCodeAsync(refused)).ShouldBe("EXPENSE_POLICY");

            JsonElement saved = await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/expenses/{claim}/lines", new object[]
            {
                new { CategoryId = meals, Date = Iso(Today), Description = "Team dinner", Amount = 2400m, HasReceipt = true, Billable = false, Justification = "Client dinner for six" },
                new { CategoryId = meals, Date = Iso(Today), Description = "Lunch", Amount = 400m, HasReceipt = false, Billable = true },
            }, Ct));
            saved.GetProperty("claim").GetProperty("total").GetDecimal().ShouldBe(2800m);
            saved.GetProperty("claim").GetProperty("policyFlags").GetInt32().ShouldBe(1);

            JsonElement submitted = await OkAsync(await self.PostAsync($"/api/v1/me/expenses/{claim}/submit", null, Ct));
            submitted.GetProperty("claim").GetProperty("status").GetString().ShouldBe("Approved");
            (await self.PostAsJsonAsync($"/api/v1/expenses/claims/{claim}/pay", new { Mode = "Payroll" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            JsonElement paid = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/expenses/claims/{claim}/pay", new { Mode = "Payroll" }, Ct));
            paid.GetProperty("claim").GetProperty("status").GetString().ShouldBe("Paid");
            JsonElement[] inputs = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/payroll/inputs?period={Iso(new DateOnly(Today.Year, Today.Month, 1))}&employeeId={person}", Ct))!;
            inputs.ShouldContain(input => input.GetProperty("kind").GetString() == "Reimbursement" && input.GetProperty("amount").GetDecimal() == 2800m);
        }
    }

    [Fact]
    public async Task A_trip_is_advanced_and_settled_against_its_claims()
    {
        (HttpClient admin, _, Guid person, HttpClient self) = await PersonAsync();
        using (admin)
        using (self)
        {
            JsonElement trip = await OkAsync(await self.PostAsJsonAsync("/api/v1/me/travel", new
            {
                Details = new { Purpose = "Client workshop", FromCity = "Bengaluru", ToCity = "Mumbai", DepartOn = Iso(Today.AddDays(-3)), ReturnOn = Iso(Today.AddDays(-1)), Mode = "Air", EstimatedCost = 20000m, AdvanceRequested = 5000m },
                Submit = true,
            }, Ct));
            Guid tripId = trip.GetProperty("id").GetGuid();
            trip.GetProperty("status").GetString().ShouldBe("Approved");
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/expenses/travel/{tripId}/advance", new { Amount = 5000m }, Ct));

            Guid hotel = await CategoryAsync(self, "/api/v1/expenses/categories", "HOTEL");
            Guid claim = (await OkAsync(await self.PostAsJsonAsync("/api/v1/me/expenses", new { Title = "Mumbai trip", TravelRequestId = tripId }, Ct))).GetProperty("claim").GetProperty("id").GetGuid();
            await OkAsync(await self.PutAsJsonAsync($"/api/v1/me/expenses/{claim}/lines", new[]
            {
                new { CategoryId = hotel, Date = Iso(Today.AddDays(-2)), Description = "Hotel, two nights", Amount = 3000m, HasReceipt = true, Billable = true },
            }, Ct));
            await OkAsync(await self.PostAsync($"/api/v1/me/expenses/{claim}/submit", null, Ct));

            JsonElement settled = await OkAsync(await admin.PostAsync($"/api/v1/expenses/travel/{tripId}/settle", null, Ct));
            settled.GetProperty("status").GetString().ShouldBe("Settled");
            settled.GetProperty("settlementAmount").GetDecimal().ShouldBe(-2000m);
            JsonElement[] inputs = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/payroll/inputs?period={Iso(new DateOnly(Today.Year, Today.Month, 1))}&employeeId={person}", Ct))!;
            inputs.ShouldContain(input => input.GetProperty("kind").GetString() == "Deduction" && input.GetProperty("amount").GetDecimal() == 2000m);
            JsonElement settledClaim = await OkAsync(await self.GetAsync($"/api/v1/expenses/claims/{claim}", Ct));
            settledClaim.GetProperty("claim").GetProperty("status").GetString().ShouldBe("Paid");
        }
    }

    [Fact]
    public async Task A_ticket_is_worked_with_internal_notes_resolved_rated_and_closed()
    {
        (HttpClient admin, _, _, HttpClient self) = await PersonAsync();
        using (admin)
        using (self)
        {
            JsonElement[] articles = (await self.GetFromJsonAsync<JsonElement[]>("/api/v1/helpdesk/knowledge?search=password", Ct))!;
            articles.ShouldNotBeEmpty();
            articles[0].GetProperty("title").GetString()!.ShouldContain("password", Case.Insensitive);

            Guid it = await CategoryAsync(self, "/api/v1/helpdesk/categories", "IT");
            JsonElement raised = await OkAsync(await self.PostAsJsonAsync("/api/v1/me/tickets", new { CategoryId = it, Subject = "VPN keeps dropping", Description = "Disconnects every 10 minutes.", Priority = "High" }, Ct));
            Guid ticket = raised.GetProperty("ticket").GetProperty("id").GetGuid();
            raised.GetProperty("ticket").GetProperty("number").GetString()!.ShouldStartWith("TKT-");
            DateTime created = raised.GetProperty("ticket").GetProperty("createdDate").GetDateTime();
            (raised.GetProperty("ticket").GetProperty("responseDueAt").GetDateTime() - created).TotalHours.ShouldBe(2, 0.1);

            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/helpdesk/tickets/{ticket}/comments", new { Body = "Likely the old client version.", IsInternal = true }, Ct));
            (await self.PostAsJsonAsync($"/api/v1/helpdesk/tickets/{ticket}/comments", new { Body = "Sneaky note", IsInternal = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            JsonElement replied = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/helpdesk/tickets/{ticket}/comments", new { Body = "Please update the VPN client to 5.2.", IsInternal = false }, Ct));
            replied.GetProperty("ticket").GetProperty("status").GetString().ShouldBe("InProgress");
            replied.GetProperty("ticket").GetProperty("firstRespondedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

            JsonElement requesterView = await OkAsync(await self.GetAsync($"/api/v1/helpdesk/tickets/{ticket}", Ct));
            requesterView.GetProperty("comments").GetArrayLength().ShouldBe(1);

            (await admin.PostAsJsonAsync($"/api/v1/helpdesk/tickets/{ticket}/status", new { Status = "Resolved" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/helpdesk/tickets/{ticket}/status", new { Status = "Resolved", Resolution = "Client updated to 5.2." }, Ct));
            await OkAsync(await self.PostAsJsonAsync($"/api/v1/me/tickets/{ticket}/rate", new { Rating = 5, Comment = "Quick fix" }, Ct));
            JsonElement closed = await OkAsync(await self.PostAsync($"/api/v1/me/tickets/{ticket}/close", null, Ct));
            closed.GetProperty("ticket").GetProperty("status").GetString().ShouldBe("Closed");
            closed.GetProperty("ticket").GetProperty("satisfactionRating").GetInt32().ShouldBe(5);
        }
    }

    [Fact]
    public async Task Confidential_tickets_are_hidden_from_helpdesk_viewers()
    {
        (HttpClient admin, _, Guid person, HttpClient self) = await PersonAsync();
        Guid agentId = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        using HttpClient agent = await factory.CreateScopedUserClientAsync(Email("agent"), agentId, (Permissions.Helpdesk.TicketView, DataScope.Tenant));
        using (admin)
        using (self)
        {
            Guid grievance = await CategoryAsync(self, "/api/v1/helpdesk/categories", "GRIEVANCE");
            Guid hr = await CategoryAsync(self, "/api/v1/helpdesk/categories", "HR");
            Guid secret = (await OkAsync(await self.PostAsJsonAsync("/api/v1/me/tickets", new { CategoryId = grievance, Subject = "A concern", Description = "Details." }, Ct))).GetProperty("ticket").GetProperty("id").GetGuid();
            Guid open = (await OkAsync(await self.PostAsJsonAsync("/api/v1/me/tickets", new { CategoryId = hr, Subject = "Address letter", Description = "For a visa." }, Ct))).GetProperty("ticket").GetProperty("id").GetGuid();

            JsonElement[] seen = (await agent.GetFromJsonAsync<JsonElement[]>("/api/v1/helpdesk/tickets", Ct))!;
            seen.ShouldContain(row => row.GetProperty("id").GetGuid() == open);
            seen.ShouldNotContain(row => row.GetProperty("id").GetGuid() == secret);
            (await agent.GetAsync($"/api/v1/helpdesk/tickets/{secret}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/helpdesk/tickets", Ct))!.ShouldContain(row => row.GetProperty("id").GetGuid() == secret);
            _ = person;
        }
    }

    [Fact]
    public async Task An_anonymous_survey_reports_only_once_enough_people_answer()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        List<HttpClient> people = [];
        for (int index = 0; index < 3; index++)
        {
            Guid employee = await CoreHrApi.HireAsync(admin, department);
            people.Add(await factory.CreateScopedUserClientAsync(Email($"s{index}"), employee, Employee));
        }

        using (admin)
        {
            JsonElement[] pulse = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/engagement/templates/pulse", Ct))!;
            JsonElement draft = await OkAsync(await admin.PostAsJsonAsync("/api/v1/engagement/surveys", new
            {
                Title = $"Pulse {Guid.NewGuid():N}"[..14],
                IsAnonymous = true,
                Questions = pulse,
                AudienceDepartmentIds = new[] { department },
                OpensOn = Iso(Today),
                ClosesOn = Iso(Today.AddDays(14)),
            }, Ct));
            Guid survey = draft.GetProperty("survey").GetProperty("id").GetGuid();
            JsonElement published = await OkAsync(await admin.PostAsync($"/api/v1/engagement/surveys/{survey}/publish", null, Ct));
            published.GetProperty("survey").GetProperty("status").GetString().ShouldBe("Open");
            published.GetProperty("survey").GetProperty("invitedCount").GetInt32().ShouldBe(3);

            int[] scores = [10, 9, 3];
            for (int index = 0; index < 3; index++)
            {
                HttpClient person = people[index];
                (await person.GetFromJsonAsync<JsonElement[]>("/api/v1/me/surveys", Ct))!.ShouldContain(row => row.GetProperty("id").GetGuid() == survey && !row.GetProperty("responded").GetBoolean());
                var answers = new Dictionary<string, string> { ["enps"] = scores[index].ToString(System.Globalization.CultureInfo.InvariantCulture), ["manager"] = "4", ["workload"] = "3", ["recognition"] = "4", ["tools"] = "5", ["stay"] = "Yes" };
                (await person.PostAsJsonAsync($"/api/v1/me/surveys/{survey}/respond", new { Answers = answers }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
                if (index == 1)
                {
                    JsonElement early = await OkAsync(await admin.GetAsync($"/api/v1/engagement/surveys/{survey}/results", Ct));
                    early.GetProperty("suppressed").GetBoolean().ShouldBeTrue();
                }
            }

            (await people[0].PostAsJsonAsync($"/api/v1/me/surveys/{survey}/respond", new { Answers = new Dictionary<string, string> { ["enps"] = "5" } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            JsonElement results = await OkAsync(await admin.GetAsync($"/api/v1/engagement/surveys/{survey}/results", Ct));
            results.GetProperty("suppressed").GetBoolean().ShouldBeFalse();
            results.GetProperty("promoters").GetInt32().ShouldBe(2);
            results.GetProperty("detractors").GetInt32().ShouldBe(1);
            results.GetProperty("enps").GetDouble().ShouldBe(33.3, 0.05);
            results.GetProperty("byDepartment").GetArrayLength().ShouldBe(1);
        }

        foreach (HttpClient person in people)
        {
            person.Dispose();
        }
    }

    [Fact]
    public async Task Recognition_fills_the_wallet_within_the_allowance_and_points_buy_rewards()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid department = await CoreHrApi.DepartmentAsync(admin);
        Guid giverId = await CoreHrApi.HireAsync(admin, department);
        Guid receiverId = await CoreHrApi.HireAsync(admin, department);
        using HttpClient giver = await factory.CreateScopedUserClientAsync(Email("giver"), giverId, Employee);
        using HttpClient receiver = await factory.CreateScopedUserClientAsync(Email("receiver"), receiverId, Employee);
        using (admin)
        {
            JsonElement[] types = (await giver.GetFromJsonAsync<JsonElement[]>("/api/v1/rewards/types", Ct))!;
            Guid teamPlayer = types.First(type => type.GetProperty("code").GetString() == "TEAM_PLAYER").GetProperty("id").GetGuid();
            Guid customerHero = types.First(type => type.GetProperty("code").GetString() == "CUSTOMER_HERO").GetProperty("id").GetGuid();

            for (int index = 0; index < 5; index++)
            {
                JsonElement given = await OkAsync(await giver.PostAsJsonAsync("/api/v1/rewards/recognitions", new { RecipientEmployeeId = receiverId, TypeId = teamPlayer, Message = $"Thanks for the help #{index}" }, Ct));
                given.GetProperty("status").GetString().ShouldBe("Approved");
            }

            HttpResponseMessage overAllowance = await giver.PostAsJsonAsync("/api/v1/rewards/recognitions", new { RecipientEmployeeId = receiverId, TypeId = teamPlayer, Message = "One more" }, Ct);
            (await CoreHrApi.ErrorCodeAsync(overAllowance)).ShouldBe("RECOGNITION_ALLOWANCE");
            (await giver.PostAsJsonAsync("/api/v1/rewards/recognitions", new { RecipientEmployeeId = receiverId, TypeId = customerHero, Message = "Not my report" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await giver.PostAsJsonAsync("/api/v1/rewards/recognitions", new { RecipientEmployeeId = giverId, TypeId = teamPlayer, Message = "Me" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

            JsonElement wallet = await OkAsync(await receiver.GetAsync("/api/v1/me/rewards", Ct));
            wallet.GetProperty("balance").GetInt32().ShouldBe(500);
            (await receiver.GetFromJsonAsync<JsonElement[]>("/api/v1/rewards/feed", Ct))!.ShouldContain(row => row.GetProperty("recipientEmployeeId").GetGuid() == receiverId);

            JsonElement[] catalog = (await receiver.GetFromJsonAsync<JsonElement[]>("/api/v1/rewards/catalog", Ct))!;
            Guid voucher = catalog.First(item => item.GetProperty("pointsCost").GetInt32() == 500).GetProperty("id").GetGuid();
            Guid hoodie = catalog.First(item => item.GetProperty("pointsCost").GetInt32() == 1200).GetProperty("id").GetGuid();
            (await CoreHrApi.ErrorCodeAsync(await receiver.PostAsJsonAsync("/api/v1/me/rewards/redeem", new { ItemId = hoodie }, Ct))).ShouldBe("POINTS_INSUFFICIENT");

            JsonElement redemption = await OkAsync(await receiver.PostAsJsonAsync("/api/v1/me/rewards/redeem", new { ItemId = voucher }, Ct));
            (await OkAsync(await receiver.GetAsync("/api/v1/me/rewards", Ct))).GetProperty("balance").GetInt32().ShouldBe(0);
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/rewards/redemptions/{redemption.GetProperty("id").GetGuid()}/cancel", new { Note = "Out of vouchers" }, Ct));
            (await OkAsync(await receiver.GetAsync("/api/v1/me/rewards", Ct))).GetProperty("balance").GetInt32().ShouldBe(500);

            await OkAsync(await admin.PostAsJsonAsync("/api/v1/rewards/points/adjust", new { EmployeeId = receiverId, Points = 700, Note = "Hackathon winner" }, Ct));
            JsonElement hoodieRedemption = await OkAsync(await receiver.PostAsJsonAsync("/api/v1/me/rewards/redeem", new { ItemId = hoodie }, Ct));
            JsonElement fulfilled = await OkAsync(await admin.PostAsJsonAsync($"/api/v1/rewards/redemptions/{hoodieRedemption.GetProperty("id").GetGuid()}/fulfil", new { Note = "Size M" }, Ct));
            fulfilled.GetProperty("status").GetString().ShouldBe("Fulfilled");
            JsonElement[] ledger = (await receiver.GetFromJsonAsync<JsonElement[]>("/api/v1/me/rewards/ledger", Ct))!;
            ledger.Sum(entry => entry.GetProperty("points").GetInt32()).ShouldBe(0);
        }
    }

    [Fact]
    public async Task A_resignation_runs_through_clearance_settlement_and_letters()
    {
        (HttpClient admin, _, Guid person, HttpClient self) = await PersonAsync();
        using (admin)
        using (self)
        {
            Guid structure = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/payroll/structures", Ct))!
                .First(row => row.GetProperty("code").GetString() == "STANDARD").GetProperty("id").GetGuid();
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/payroll/employees/{person}/salary", new { StructureId = structure, AnnualCtc = 1200000m, EffectiveFrom = "2024-01-15", Reason = "Hire" }, Ct));
            await AssignLaptopAsync(admin, person, 60000m);

            DateOnly lastDay = Today.AddDays(30);
            JsonElement resigned = await OkAsync(await self.PostAsJsonAsync("/api/v1/me/separation", new { Reason = "Moving to another city", RequestedLastDay = Iso(lastDay) }, Ct));
            Guid caseId = resigned.GetProperty("case").GetProperty("id").GetGuid();
            resigned.GetProperty("case").GetProperty("status").GetString().ShouldBe("Approved");
            resigned.GetProperty("case").GetProperty("shortfallDays").GetInt32().ShouldBe(30);
            JsonElement[] clearance = [.. resigned.GetProperty("clearance").EnumerateArray()];
            clearance.ShouldContain(item => item.GetProperty("area").GetString() == "Assets" && item.GetProperty("recoveryAmount").GetDecimal() > 0);
            (await CoreHrApi.ErrorCodeAsync(await self.PostAsJsonAsync("/api/v1/me/separation", new { Reason = "Again" }, Ct))).ShouldBe("SEPARATION_OPEN");
            (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{person}", Ct)).GetProperty("status").GetString().ShouldBe("OnNotice");

            await OkAsync(await self.PutAsJsonAsync("/api/v1/me/separation/interview", new { PrimaryReason = "Relocation", OverallRating = 4, WouldRecommend = true, WouldReturn = true, WhatWentWell = "The team" }, Ct));
            JsonElement calculated = await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/calculate", null, Ct));
            string[] codes = [.. calculated.GetProperty("settlement").GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("code").GetString()!)];
            codes.ShouldContain("SALARY");
            codes.ShouldContain("NOTICE_RECOVERY");
            codes.ShouldContain("RECOVERY");
            (await CoreHrApi.ErrorCodeAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/approve", null, Ct))).ShouldBe("SETTLEMENT_CLEARANCE");

            foreach (JsonElement item in clearance)
            {
                string status = item.GetProperty("area").GetString() == "Assets" ? "Waived" : "Cleared";
                await OkAsync(await admin.PostAsJsonAsync($"/api/v1/separation/clearance/{item.GetProperty("id").GetGuid()}", new { Status = status }, Ct));
            }

            await OkAsync(await admin.PutAsJsonAsync($"/api/v1/separation/cases/{caseId}/terms", new { LastWorkingDay = Iso(lastDay), ShortfallWaived = true, EligibleForRehire = true }, Ct));
            JsonElement recalculated = await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/calculate", null, Ct));
            codes = [.. recalculated.GetProperty("settlement").GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("code").GetString()!)];
            codes.ShouldNotContain("NOTICE_RECOVERY");
            codes.ShouldNotContain("RECOVERY");
            await OkAsync(await admin.PostAsJsonAsync($"/api/v1/separation/cases/{caseId}/settlement/lines", new { Code = "BONUS", Label = "Pro-rata bonus", IsDeduction = false, Amount = 15000m }, Ct));
            await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/settlement/approve", null, Ct));
            JsonElement paid = await SettlementPayroll.PayAsync(factory, admin, caseId, "NEFT-123");
            paid.GetProperty("status").GetString().ShouldBe("Paid");
            paid.GetProperty("net").GetDecimal().ShouldBeGreaterThan(15000m);

            JsonElement lettered = await OkAsync(await admin.PostAsync($"/api/v1/separation/cases/{caseId}/letters", null, Ct));
            lettered.GetProperty("case").GetProperty("relievingLetterId").ValueKind.ShouldNotBe(JsonValueKind.Null);
            lettered.GetProperty("case").GetProperty("experienceLetterId").ValueKind.ShouldNotBe(JsonValueKind.Null);
            JsonElement mine = await OkAsync(await self.GetAsync("/api/v1/me/separation", Ct));
            mine.GetProperty("settlement").GetProperty("status").GetString().ShouldBe("Paid");
        }
    }

    [Fact]
    public async Task An_exit_takes_effect_after_the_last_day_and_joins_the_alumni()
    {
        HttpClient admin = await factory.CreateAdminClientAsync();
        Guid person = await CoreHrApi.HireAsync(admin, await CoreHrApi.DepartmentAsync(admin));
        using (admin)
        {
            JsonElement started = await OkAsync(await admin.PostAsJsonAsync("/api/v1/separation/cases", new
            {
                EmployeeId = person,
                Type = "ContractEnd",
                Reason = "Fixed-term contract ended",
                NoticeDate = Iso(Today.AddDays(-30)),
                LastWorkingDay = Iso(Today.AddDays(-1)),
            }, Ct));
            Guid caseId = started.GetProperty("case").GetProperty("id").GetGuid();

            int exited = 0;
            await factory.RunAsWorkerAsync(PeopleOsApiFactory.SeededTenantId, async services => exited = await services.GetRequiredService<SeparationService>().SweepAsync(Ct));
            exited.ShouldBeGreaterThanOrEqualTo(1);

            (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/separation/cases/{caseId}", Ct)).GetProperty("case").GetProperty("status").GetString().ShouldBe("Completed");
            (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/employees/{person}", Ct)).GetProperty("status").GetString().ShouldBe("Exited");
            (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/separation/alumni", Ct))!.ShouldContain(row => row.GetProperty("employeeId").GetGuid() == person);
        }
    }

    [Theory]
    [InlineData("/api/v1/assets/dashboard")]
    [InlineData("/api/v1/assets")]
    [InlineData("/api/v1/expenses/dashboard")]
    [InlineData("/api/v1/expenses/claims")]
    [InlineData("/api/v1/expenses/travel")]
    [InlineData("/api/v1/helpdesk/dashboard")]
    [InlineData("/api/v1/helpdesk/tickets")]
    [InlineData("/api/v1/helpdesk/knowledge")]
    [InlineData("/api/v1/engagement/dashboard")]
    [InlineData("/api/v1/engagement/surveys")]
    [InlineData("/api/v1/rewards/dashboard")]
    [InlineData("/api/v1/rewards/settings")]
    [InlineData("/api/v1/rewards/rules")]
    [InlineData("/api/v1/rewards/budgets")]
    [InlineData("/api/v1/rewards/recognitions")]
    [InlineData("/api/v1/rewards/redemptions")]
    [InlineData("/api/v1/separation/dashboard")]
    [InlineData("/api/v1/separation/cases")]
    [InlineData("/api/v1/separation/clearance")]
    [InlineData("/api/v1/separation/alumni")]
    public async Task Every_experience_list_and_dashboard_answers_an_administrator(string path)
    {
        using HttpClient admin = await factory.CreateAdminClientAsync();
        (await admin.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
