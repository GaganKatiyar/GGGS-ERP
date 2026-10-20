using System.Text.Encodings.Web;
using System.Text.Json;
using Gggs.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Gggs.BuildingBlocks.UnitTests;

/// <summary>A lapsed step-up window is reported as "confirm it's you", any other refusal as before.</summary>
public sealed class StepUpResultHandlerTests
{
    private static readonly AuthorizationPolicy Policy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();

    private static async Task<(int Status, string Body)> HandleAsync(PolicyAuthorizationResult result)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddAuthentication(options => options.DefaultForbidScheme = ForbidHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ForbidHandler>(ForbidHandler.SchemeName, _ => { }).Services
            .BuildServiceProvider();
        await new StepUpResultHandler().HandleAsync(_ => Task.CompletedTask, context, Policy, result);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_lapsed_step_up_window_asks_the_person_to_confirm_it_is_them()
    {
        PolicyAuthorizationResult forbidden = PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed([new AuthorizationFailureReason(Substitute.For<IAuthorizationHandler>(), AuthorizationFailureReasons.StepUpRequired)]));

        (int status, string body) = await HandleAsync(forbidden);

        status.ShouldBe(StatusCodes.Status403Forbidden);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(StepUpResultHandler.ErrorCode);
    }

    [Fact]
    public async Task A_missing_permission_is_not_dressed_up_as_a_step_up()
    {
        PolicyAuthorizationResult forbidden = PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed([new AuthorizationFailureReason(Substitute.For<IAuthorizationHandler>(), AuthorizationFailureReasons.MissingPermission)]));

        (int status, string body) = await HandleAsync(forbidden);

        status.ShouldBe(StatusCodes.Status403Forbidden);
        body.ShouldNotContain(StepUpResultHandler.ErrorCode);
    }

    /// <summary>The ordinary forbid: a bare 403.</summary>
    private sealed class ForbidHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
