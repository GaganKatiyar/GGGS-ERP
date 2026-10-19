using Gggs.Data;
using Gggs.Modules.Assets.Application;
using Gggs.Modules.Assets.Domain;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Engagement.Application;
using Gggs.Modules.Engagement.Domain;
using Gggs.Modules.Expense.Application;
using Gggs.Modules.Expense.Domain;
using Gggs.Modules.Helpdesk.Domain;
using Gggs.Modules.Identity.Application.Provisioning;
using Gggs.Modules.Identity.Application.Users;
using Gggs.Modules.Identity.Domain.Roles;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Rewards.Application;
using Gggs.Modules.Rewards.Domain;
using Gggs.Modules.Separation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.DevSeed;

/// <summary>
/// Development-only employee experience data (M10): assets assigned and in repair with a request
/// waiting; expense claims at every stage and a trip with an advance; helpdesk tickets across the
/// queue; a closed pulse survey with eNPS and an open one; recognition, points and a redemption to
/// fulfil; and one resignation working through clearance.
/// </summary>
internal static class ExperienceDemo
{
    private static readonly string[] Codes = ["EMP00001", "EMP00002", "EMP00003", "EMP00004", "EMP00005", "EMP00006", "EMP00007"];

    public static async Task SeedAsync(IServiceProvider scope)
    {
        await TopUpRolesAsync(scope);
        int assets = await scope.GetRequiredService<AssetSeeder>().SeedCurrentTenantAsync();
        int expenses = await scope.GetRequiredService<ExpenseSeeder>().SeedCurrentTenantAsync();
        int helpdesk = await scope.GetRequiredService<Gggs.Modules.Helpdesk.Application.HelpdeskSeeder>().SeedCurrentTenantAsync();
        int rewards = await scope.GetRequiredService<RewardSeeder>().SeedCurrentTenantAsync();
        Console.WriteLine($"Experience catalogues: {assets} asset, {expenses} expense, {helpdesk} helpdesk and {rewards} reward default(s) added.");

        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        IReadOnlyDictionary<string, Guid> people = await scope.GetRequiredService<IEmployeeDirectory>().ResolveCodesAsync(Codes);
        if (people.Count < Codes.Length)
        {
            Console.WriteLine("Demo employees missing; skipping the experience demo.");
            return;
        }

        await SeedAssetsAsync(scope, db, people);
        await SeedExpensesAsync(db, people);
        await SeedHelpdeskAsync(db, people);
        await SeedEngagementAsync(scope, db, people);
        await SeedRewardsAsync(db, people);
        await SeedSeparationAsync(scope, db, people);
    }

    private static async Task SeedAssetsAsync(IServiceProvider scope, AppDbContext db, IReadOnlyDictionary<string, Guid> people)
    {
        if (await db.Set<Asset>().AnyAsync())
        {
            Console.WriteLine("Assets already present.");
            return;
        }

        AssetService service = scope.GetRequiredService<AssetService>();
        Dictionary<string, Guid> categories = await db.Set<AssetCategory>().AsNoTracking().ToDictionaryAsync(row => row.Code, row => row.Id);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        async Task<Guid> Register(string category, string name, string make, string model, string serial, int monthsOld, decimal cost)
        {
            AssetView view = await service.RegisterAsync(
                new AssetBody(new AssetDetails(categories[category], name, make, model, serial, today.AddMonths(-monthsOld), cost, "Croma Business", today.AddMonths(36 - monthsOld), null, null)),
                CancellationToken.None);
            return view.Asset.Id;
        }

        string[] holders = ["EMP00001", "EMP00002", "EMP00003", "EMP00004", "EMP00005", "EMP00007"];
        for (int index = 0; index < holders.Length; index++)
        {
            Guid laptop = await Register("LAPTOP", "Dell Latitude 7440", "Dell", "Latitude 7440", $"DL7440-{1001 + index}", 6 + (index * 3), 98000m);
            await service.AssignAsync(laptop, new AssignBody(people[holders[index]], today.AddMonths(-5 + index), null), CancellationToken.None);
        }

        Guid monitor = await Register("MONITOR", "LG 27\" 4K monitor", "LG", "27UP850", "LG27-5501", 10, 32000m);
        await service.AssignAsync(monitor, new AssignBody(people["EMP00002"], today.AddMonths(-9), null), CancellationToken.None);
        Guid phone = await Register("PHONE", "iPhone 15", "Apple", "iPhone 15", "APL-15-7781", 8, 79900m);
        await service.AssignAsync(phone, new AssignBody(people["EMP00007"], today.AddMonths(-7), null), CancellationToken.None);
        Guid spare = await Register("LAPTOP", "MacBook Pro 14", "Apple", "MacBook Pro 14 M3", "MBP14-3301", 2, 189000m);
        await service.ActAsync(spare, "repair", new EventBody("Keyboard replacement under warranty", null, null), CancellationToken.None);
        await Register("LAPTOP", "Dell Latitude 7440", "Dell", "Latitude 7440", "DL7440-1099", 1, 98000m);
        await Register("HEADSET", "Jabra Evolve2 65", "Jabra", "Evolve2 65", "JB-6501", 3, 18500m);

        db.Add(AssetRequest.Raise(people["EMP00006"], categories["MONITOR"], "A second screen for design reviews."));
        await db.SaveChangesAsync();
        Console.WriteLine("Assets: 12 registered, 8 assigned, 1 in repair, 1 request waiting.");
    }

    private static async Task SeedExpensesAsync(AppDbContext db, IReadOnlyDictionary<string, Guid> people)
    {
        if (await db.Set<ExpenseClaim>().AnyAsync())
        {
            Console.WriteLine("Expense claims already present.");
            return;
        }

        Dictionary<string, Guid> categories = await db.Set<ExpenseCategory>().AsNoTracking().ToDictionaryAsync(row => row.Code, row => row.Id);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateTime now = DateTime.UtcNow;
        int next = 1;

        ExpenseClaim Claim(string person, string title, ClaimStatus status, Guid? tripId, params (string Category, int DaysAgo, string Description, decimal Amount, string? Flag)[] lines)
        {
            var claim = ExpenseClaim.Draft($"EXP-{today.Year}-{next++:00000}", people[person], title, tripId, null, "INR");
            db.Add(claim);
            decimal total = 0;
            foreach ((string category, int daysAgo, string description, decimal amount, string? flag) in lines)
            {
                db.Add(ExpenseLine.Add(claim.Id, new ClaimLineInput(categories[category], today.AddDays(-daysAgo), description, amount, null, true, category == "CLIENT", flag is null ? null : "Client dinner, approved by the account lead"), flag, today));
                total += amount;
            }

            claim.SetTotal(total);
            if (status != ClaimStatus.Draft)
            {
                claim.Submit(now.AddDays(-3));
            }

            if (status is ClaimStatus.Approved or ClaimStatus.Paid)
            {
                claim.Decide(approved: true, now.AddDays(-2));
            }

            if (status == ClaimStatus.Paid)
            {
                claim.Pay(PaymentMode.Direct, "NEFT-DEMO-4471", today.AddDays(-1));
            }

            return claim;
        }

        var trip = TravelRequest.Draft($"TRV-{today.Year}-00001", people["EMP00003"], new TravelDetails("Northwind workshop", "Bengaluru", "Mumbai", today.AddDays(-6), today.AddDays(-4), TravelMode.Air, 24000m, 8000m, null));
        trip.Submit();
        trip.Decide(approved: true);
        trip.PayAdvance(8000m, today.AddDays(-8));
        db.Add(trip);
        var upcoming = TravelRequest.Draft($"TRV-{today.Year}-00002", people["EMP00005"], new TravelDetails("Client go-live support", "Pune", "Delhi", today.AddDays(10), today.AddDays(13), TravelMode.Rail, 15000m, 0m, null));
        upcoming.Submit();
        db.Add(upcoming);

        Claim("EMP00002", "Client visit, Pune", ClaimStatus.Paid, null, ("TRAVEL", 9, "Cab to client site", 1450m, null), ("MEALS", 9, "Lunch", 620m, null));
        Claim("EMP00003", "Mumbai workshop", ClaimStatus.Approved, trip.Id, ("HOTEL", 5, "Hotel, two nights", 9800m, null), ("MEALS", 5, "Team dinner", 2400m, "over the 1500 limit"), ("LOCAL", 4, "Airport taxi", 900m, null));
        Claim("EMP00004", "Broadband, last month", ClaimStatus.PendingApproval, null, ("INTERNET", 12, "Home broadband", 1199m, null));
        Claim("EMP00005", "Conference ticket", ClaimStatus.Draft, null, ("OTHER", 2, "Angular India ticket", 4500m, null));
        await db.SaveChangesAsync();
        Console.WriteLine("Expenses: 4 claims (paid, approved, pending, draft) and 2 trips, one with an advance paid.");
    }

    private static async Task SeedHelpdeskAsync(AppDbContext db, IReadOnlyDictionary<string, Guid> people)
    {
        if (await db.Set<Ticket>().AnyAsync())
        {
            Console.WriteLine("Tickets already present.");
            return;
        }

        Dictionary<string, HelpdeskCategory> categories = await db.Set<HelpdeskCategory>().ToDictionaryAsync(row => row.Code);
        HelpdeskCategory it = categories["IT"];
        it.Update(it.Code, it.Name, it.Description, people["EMP00004"], it.ResponseHours, it.ResolutionHours, it.IsConfidential, true);
        DateTime now = DateTime.UtcNow;
        int next = 1;

        Ticket Raise(string person, string category, string subject, string description, TicketPriority priority, int hoursAgo)
        {
            var ticket = Ticket.Raise($"TKT-{next++:000000}", people[person], categories[category], subject, description, priority, now.AddHours(-hoursAgo));
            db.Add(ticket);
            return ticket;
        }

        Ticket vpn = Raise("EMP00002", "IT", "VPN disconnects every few minutes", "Since yesterday the VPN drops roughly every ten minutes when I am on the home network.", TicketPriority.High, 5);
        vpn.Responded(now.AddHours(-4));
        db.Add(TicketComment.Write(vpn.Id, people["EMP00004"], "Could you share the client version from Help > About?", false));
        db.Add(TicketComment.Write(vpn.Id, people["EMP00004"], "Probably the old 4.x client again.", true));
        Raise("EMP00005", "PAYROLL", "Payslip shows the wrong HRA", "My October payslip shows HRA at the old rate; my revision was effective from September.", TicketPriority.Medium, 30);
        Ticket letter = Raise("EMP00006", "HR", "Address proof letter for visa", "Please issue an employment letter addressed to the German consulate.", TicketPriority.Low, 60);
        letter.MoveTo(TicketStatus.Resolved, "Letter issued and shared under My documents.", now.AddHours(-20));
        letter.Rate(5, "Thanks, very quick!");
        Raise("EMP00003", "ADMIN", "Lost access card", "I lost my access card on the way home; please block it and issue a new one.", TicketPriority.Urgent, 2);
        Raise("EMP00007", "GRIEVANCE", "Concern about meeting conduct", "I would like to raise a concern about repeated remarks in team meetings.", TicketPriority.Medium, 10);
        await db.SaveChangesAsync();
        Console.WriteLine("Helpdesk: 5 tickets (one confidential), IT routed to EMP00004.");
    }

    private static async Task SeedEngagementAsync(IServiceProvider scope, AppDbContext db, IReadOnlyDictionary<string, Guid> people)
    {
        if (await db.Set<Survey>().AnyAsync())
        {
            Console.WriteLine("Surveys already present.");
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        IReadOnlyList<SurveyQuestion> pulse = EngagementService.PulseTemplate;
        var closed = Survey.Draft($"Q{((today.Month - 1) / 3) + 1} pulse (last quarter)", "How are we doing? Ten minutes, fully anonymous.", true, pulse, null, today.AddDays(-40), today.AddDays(-26));
        closed.Publish(today.AddDays(-40));
        closed.Open(people.Count);
        db.Add(closed);
        int[] scores = [10, 9, 9, 8, 7, 6, 10];
        string[] ideas = ["More time for learning.", "Clearer priorities between projects.", string.Empty, "Better laptops for the data team.", string.Empty, "Fewer meetings on Fridays.", string.Empty];
        int index = 0;
        foreach (Guid employee in people.Values)
        {
            var answers = new Dictionary<string, string>
            {
                ["enps"] = scores[index].ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["manager"] = (3 + (index % 3)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["workload"] = (2 + (index % 4)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["recognition"] = (3 + (index % 2)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["tools"] = "4",
                ["stay"] = index == 5 ? "Not sure" : "Yes",
            };
            if (ideas[index].Length > 0)
            {
                answers["improve"] = ideas[index];
            }

            var invitation = SurveyInvitation.Invite(closed.Id, employee);
            invitation.Respond(DateTime.UtcNow.AddDays(-30));
            db.Add(invitation);
            db.Add(SurveyResponse.Record(closed, employee, null, answers));
            closed.Responded();
            index++;
        }

        closed.Close(today.AddDays(-26));
        await db.SaveChangesAsync();

        EngagementService service = scope.GetRequiredService<EngagementService>();
        SurveyView open = await service.SaveAsync(null, new SurveyBody("Hybrid work check-in", "Tell us how the hybrid policy is working for you.", true, pulse, null, today, today.AddDays(14)), CancellationToken.None);
        await service.PublishAsync(open.Survey.Id, CancellationToken.None);
        Console.WriteLine("Engagement: a closed pulse with 7 responses (eNPS) and an open survey for everyone.");
    }

    private static async Task SeedRewardsAsync(AppDbContext db, IReadOnlyDictionary<string, Guid> people)
    {
        if (await db.Set<Recognition>().AnyAsync())
        {
            Console.WriteLine("Recognition already present.");
            return;
        }

        Dictionary<string, RecognitionType> types = await db.Set<RecognitionType>().AsNoTracking().ToDictionaryAsync(row => row.Code);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Add(RewardBudget.Create(today.Year, null, 50000));

        void Give(string giver, string recipient, string type, string message, int daysAgo)
        {
            var recognition = Recognition.Give(people[giver], people[recipient], types[type], message, true, null, today.AddDays(-daysAgo));
            recognition.Decide(approved: true);
            db.Add(recognition);
            if (recognition.Points > 0)
            {
                db.Add(PointEntry.Credit(people[recipient], PointEntryKind.Earned, recognition.Points, today.AddDays(-daysAgo), today.AddDays(-daysAgo).AddMonths(12), "rewards.recognition", recognition.Id, types[type].Name));
            }
        }

        Give("EMP00001", "EMP00003", "CUSTOMER_HERO", "Turned the Northwind escalation around in two days.", 20);
        Give("EMP00002", "EMP00004", "TEAM_PLAYER", "Stayed late to get the release over the line.", 12);
        Give("EMP00004", "EMP00002", "THANKS", "Thanks for the quick code review!", 9);
        Give("EMP00005", "EMP00006", "TEAM_PLAYER", "Brilliant onboarding sessions for the new joiners.", 6);
        Give("EMP00003", "EMP00005", "THANKS", "Covered my on-call shift at short notice.", 3);
        Give("EMP00006", "EMP00001", "THANKS", "For the clear roadmap walk-through.", 1);

        CatalogItem voucher = await db.Set<CatalogItem>().FirstAsync(row => row.PointsCost == 500);
        PointEntry credit = PointEntry.Credit(people["EMP00003"], PointEntryKind.Adjusted, 500, today.AddDays(-15), today.AddMonths(11), "rewards.adjustment", null, "Adjustment: Hackathon runner-up");
        db.Add(credit);
        PointEntry.SpendFifo([credit], 500);
        var redemption = Redemption.Request(people["EMP00003"], voucher, today.AddDays(-2));
        db.Add(redemption);
        db.Add(PointEntry.Debit(people["EMP00003"], PointEntryKind.Redeemed, 500, today.AddDays(-2), "rewards.redemption", redemption.Id, $"Redeemed {voucher.Name}"));
        await db.SaveChangesAsync();
        Console.WriteLine("Rewards: 6 recognitions on the feed, a company budget and a redemption to fulfil.");
    }

    private static async Task SeedSeparationAsync(IServiceProvider scope, AppDbContext db, IReadOnlyDictionary<string, Guid> people)
    {
        if (await db.Set<SeparationCase>().AnyAsync())
        {
            Console.WriteLine("Separations already present.");
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid leaver = people["EMP00007"];
        EmployeeSummary? person = await scope.GetRequiredService<IEmployeeDirectory>().GetAsync(leaver);
        var separation = SeparationCase.Open(leaver, SeparationType.Resignation, "Relocating to Hyderabad for family reasons.", today.AddDays(-20), 60, today.AddDays(25));
        separation.Approve(null);
        db.Add(separation);
        await scope.GetRequiredService<IEmployeeSeparations>().StartNoticeAsync(leaver, separation.NoticeDate);

        db.Add(ClearanceItem.Add(separation.Id, ClearanceArea.Manager, "Knowledge transfer and handover", person?.ManagerEmployeeId));
        db.Add(ClearanceItem.Add(separation.Id, ClearanceArea.It, "Revoke system access and collect accounts", null));
        db.Add(ClearanceItem.Add(separation.Id, ClearanceArea.Admin, "Return ID card and access card", null));
        db.Add(ClearanceItem.Add(separation.Id, ClearanceArea.Finance, "No dues: loans, advances and reimbursements", null));
        db.Add(ClearanceItem.Add(separation.Id, ClearanceArea.Hr, "Exit interview and documents", person?.HrbpEmployeeId));
        foreach (Asset asset in await db.Set<Asset>().AsNoTracking().Where(row => row.AssignedEmployeeId == leaver).ToListAsync())
        {
            db.Add(ClearanceItem.Add(separation.Id, ClearanceArea.Assets, $"Return {asset.Name} ({asset.Tag})", null, 45000m, "asset", asset.Id));
        }

        db.Add(AlumniProfile.Create(Guid.NewGuid(), "Arjun Mehta", "arjun.mehta@example.com", "Senior Developer", null, today.AddMonths(-8), SeparationType.Resignation, true));
        db.Add(AlumniProfile.Create(Guid.NewGuid(), "Kavya Nair", "kavya.nair@example.com", "QA Lead", null, today.AddMonths(-14), SeparationType.Resignation, true));
        await db.SaveChangesAsync();
        Console.WriteLine("Separation: EMP00007 serving notice with clearance open; 2 alumni.");
    }

    private static async Task TopUpRolesAsync(IServiceProvider scope)
    {
        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        IReadOnlyDictionary<string, Role> roles = await scope.GetRequiredService<IRoleRepository>().GetTemplateRolesAsync();
        int added = 0;
        foreach ((string code, Role role) in roles)
        {
            int before = role.Permissions.Count;
            foreach (PermissionAssignment permission in RoleTemplates.For(code))
            {
                role.Grant(permission.PermissionCode, permission.Scope);
            }

            added += role.Permissions.Count - before;
        }

        if (added > 0)
        {
            foreach (User user in await db.Set<User>().ToListAsync())
            {
                user.BumpPermissionVersion();
            }

            await db.SaveChangesAsync();
        }

        Console.WriteLine($"Experience roles: {added} permission(s) added from the templates.");
    }
}
