using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Rewards.Domain;
using Gggs.Modules.Rewards.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Rewards.Application;

/// <summary>The caller's points, allowance and what the catalogue offers.</summary>
internal sealed class MyRewardsTool(RewardService rewards) : IAiTool
{
    public string Name => "my_rewards";

    public string Description => "This person's reward points, points expiring soon, peer allowance left this month, the recognition types they can give and the catalogue.";

    public JsonElement InputSchema { get; } = AiJson.Schema();

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Self.RewardView;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        WalletView wallet = await rewards.WalletAsync(cancellationToken);
        IReadOnlyList<TypeItem> types = await rewards.TypesAsync(cancellationToken);
        IReadOnlyList<CatalogItemView> catalog = await rewards.CatalogAsync(includeInactive: false, cancellationToken);
        return AiToolOutput.Data(new
        {
            wallet.Balance,
            wallet.ExpiringSoon,
            wallet.NextExpiry,
            wallet.PeerAllowanceLeft,
            RecognitionTypes = types.Where(type => type.IsActive).Select(type => new { type.Code, type.Name, Kind = type.Kind.ToString(), type.Points }),
            Catalogue = catalog.Select(item => new { item.Name, item.PointsCost, item.Stock }),
        });
    }
}

/// <summary>Drafts a recognition for a colleague; it is sent only when the person approves it.</summary>
internal sealed class GiveRecognitionTool(AppDbContext dbContext, IEmployeeDirectory employees, RewardService rewards) : IAiActionTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => "give_recognition";

    public string Description => "Drafts a recognition for a colleague: their employee code (find it with search_directory), the recognition type code (from my_rewards) and a short message in the person's voice.";

    public JsonElement InputSchema { get; } = AiJson.Schema(
        ("employeeCode", "string", "The colleague's employee code", true),
        ("typeCode", "string", "Recognition type code, e.g. THANKS", true),
        ("message", "string", "The message, one or two sentences", true));

    public AiToolKind Kind => AiToolKind.Action;

    public string? Permission => Permissions.Self.RecognitionGive;

    public Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("give_recognition is an action; it is proposed, not run.");

    public async Task<AiProposal> ProposeAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        string code = (AiJson.Text(input, "employeeCode") ?? string.Empty).Trim();
        IReadOnlyDictionary<string, Guid> resolved = await employees.ResolveCodesAsync([code], cancellationToken);
        Guid recipient = resolved.Values.FirstOrDefault();
        EmployeeSummary person = recipient == Guid.Empty ? throw new DomainRuleException("AI_RECIPIENT", $"No employee has the code '{code}'.") : await employees.GetAsync(recipient, cancellationToken) ?? throw new DomainRuleException("AI_RECIPIENT", $"No employee has the code '{code}'.");
        string typeCode = (AiJson.Text(input, "typeCode") ?? string.Empty).Trim().ToUpperInvariant();
        RecognitionType type = await dbContext.RecognitionTypes.AsNoTracking().FirstOrDefaultAsync(row => row.Code == typeCode && row.IsActive, cancellationToken)
            ?? throw new DomainRuleException("AI_RECOGNITION_TYPE", $"There is no recognition type '{typeCode}'.");
        string message = (AiJson.Text(input, "message") ?? string.Empty).Trim();
        if (message.Length is 0 or > 1000)
        {
            throw new DomainRuleException("AI_RECOGNITION_MESSAGE", "Write a message of up to 1,000 characters.");
        }

        return new AiProposal($"Recognise {person.DisplayName} with {type.Name} ({type.Points} points): \"{message}\"", JsonSerializer.Serialize(new GiveBody(person.Id, type.Id, message), Json));
    }

    public async Task<string> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        GiveBody body = JsonSerializer.Deserialize<GiveBody>(payloadJson, Json) ?? throw new DomainRuleException("AI_PAYLOAD", "The proposal could not be read.");
        RecognitionItem given = await rewards.GiveAsync(body, cancellationToken);
        return given.Status == RecognitionStatus.Pending ? $"Recognition for {given.RecipientName} sent for approval." : $"{given.RecipientName} has been recognised (+{given.Points} points).";
    }
}
