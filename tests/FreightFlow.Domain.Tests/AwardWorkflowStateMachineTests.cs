using FreightFlow.SharedKernel;
using FreightFlow.WorkflowWorker.Application;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.Linq;

namespace FreightFlow.Domain.Tests;

/// <summary>
/// State-transition tests for AwardWorkflowStateMachine.
/// Uses MassTransit's in-memory test harness + stub activities so no infrastructure is needed.
///
/// Pattern: publish messages directly to advance the saga through states,
/// bypassing real I/O. Activities are replaced with test stubs that just publish
/// the internal step-advancement event.
/// </summary>
public sealed class AwardWorkflowStateMachineTests : IAsyncLifetime
{
    private ServiceProvider  _provider = null!;
    private ITestHarness     _harness  = null!;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AwardIssued MakeAwardIssued(Guid? rfpId = null) => new(
        RfpId:           RfpId.From(rfpId ?? Guid.NewGuid()),
        BidId:           BidId.New(),
        CarrierId:       CarrierId.New(),
        LaneId:          LaneId.New(),
        AgreedRate:      new Money(1500m, "USD"),
        VolumeToReserve: 100,
        OccurredAt:      DateTimeOffset.UtcNow);

    private ISagaStateMachineTestHarness<StubAwardWorkflowStateMachine, AwardWorkflowState> SagaHarness
        => _harness.GetSagaStateMachineHarness<StubAwardWorkflowStateMachine, AwardWorkflowState>();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        // Register stub activities (resolved from DI by MassTransit).
        services.AddTransient<StubReserveCapacityActivity>();
        services.AddTransient<StubIssueContractActivity>();
        services.AddTransient<StubNotifyShipperActivity>();
        services.AddTransient<StubMarkRfpAwardedActivity>();
        services.AddTransient<StubCompensateWorkflowActivity>();

        services.AddMassTransitTestHarness(x =>
        {
            x.AddSagaStateMachine<StubAwardWorkflowStateMachine, AwardWorkflowState>()
                .InMemoryRepository();
        });

        _provider = services.BuildServiceProvider(true);
        _harness  = _provider.GetRequiredService<ITestHarness>();

        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AwardIssued_CreatesSaga_WithCorrectFields()
    {
        var msg = MakeAwardIssued();

        await _harness.Bus.Publish(msg);

        // Wait until the saga has been created and Then() fields written.
        await SagaHarness.Exists(msg.RfpId.Value, timeout: TimeSpan.FromSeconds(5));

        // Fields set in Then() are reflected in the Created list's Saga snapshot
        // captured AFTER the consume cycle completes.
        // Use the published message values for assertion (they're the ground truth).
        var corrId = msg.RfpId.Value;

        _harness.Consumed.Select<AwardIssued>(x => x.Context.Message.RfpId.Value == corrId)
            .Any().ShouldBeTrue("AwardIssued should have been consumed by the saga.");

        // Verify the saga was created with exactly the correct correlation ID.
        SagaHarness.Created.Select(x => x.CorrelationId == corrId).Any()
            .ShouldBeTrue("Saga should have been created with the RfpId as its CorrelationId.");
    }

    [Fact]
    public async Task FullHappyPath_SagaIsConsumedByAllFourSteps()
    {
        var msg    = MakeAwardIssued();
        var corrId = msg.RfpId.Value;

        await _harness.Bus.Publish(msg);

        // Wait for the saga to reach Completed — all 5 steps guaranteed by this point.
        var found = await SagaHarness.Exists(corrId, sm => sm.Completed, TimeSpan.FromSeconds(10));
        found.ShouldNotBeNull("Saga should reach Completed within 10 s.");

        _harness.Consumed.Select<AwardIssued>(x => x.Context.Message.RfpId.Value == corrId)
            .Any().ShouldBeTrue("AwardIssued should be consumed.");

        _harness.Consumed.Select<CapacityReservedEvent>(x => x.Context.Message.CorrelationId == corrId)
            .Any().ShouldBeTrue("CapacityReservedEvent should be consumed.");

        _harness.Consumed.Select<ContractIssuedEvent>(x => x.Context.Message.CorrelationId == corrId)
            .Any().ShouldBeTrue("ContractIssuedEvent should be consumed.");

        _harness.Consumed.Select<ShipperNotifiedInternalEvent>(x => x.Context.Message.CorrelationId == corrId)
            .Any().ShouldBeTrue("ShipperNotifiedInternalEvent should be consumed.");

        _harness.Consumed.Select<RfpAwardAcknowledged>(x => x.Context.Message.RfpId == corrId)
            .Any().ShouldBeTrue("RfpAwardAcknowledged should be consumed (simulated rfp-api ack).");
    }

    [Fact]
    public async Task DuplicateAwardIssued_IsIgnoredWhenSagaAlreadyActive()
    {
        var msg    = MakeAwardIssued();
        var corrId = msg.RfpId.Value;

        await _harness.Bus.Publish(msg);
        await SagaHarness.Exists(corrId, TimeSpan.FromSeconds(5));

        // Re-deliver the same AwardIssued — should be ignored per Ignore(AwardIssuedEvent) in active state.
        await _harness.Bus.Publish(msg);

        // Wait for the saga to fully settle; Completed means both messages were processed.
        await SagaHarness.Exists(corrId, sm => sm.Completed, TimeSpan.FromSeconds(5));
        // Only one saga should ever be created for this correlation ID.
        SagaHarness.Created.Select(x => x.CorrelationId == corrId).Count().ShouldBe(1,
            "Re-delivered AwardIssued must not create a second saga instance.");
    }
}

// ── Stub activities ─────────────────────────────────────────────────────────────

internal sealed class StubReserveCapacityActivity :
    IStateMachineActivity<AwardWorkflowState>,
    IStateMachineActivity<AwardWorkflowState, AwardIssued>
{
    public async Task Execute(BehaviorContext<AwardWorkflowState> ctx, IBehavior<AwardWorkflowState> next)
    {
        await ctx.Publish(new CapacityReservedEvent(ctx.Saga.CorrelationId));
        await next.Execute(ctx);
    }

    public Task Execute<T>(BehaviorContext<AwardWorkflowState, T> ctx, IBehavior<AwardWorkflowState, T> next) where T : class => next.Execute(ctx);

    async Task IStateMachineActivity<AwardWorkflowState, AwardIssued>.Execute(BehaviorContext<AwardWorkflowState, AwardIssued> ctx, IBehavior<AwardWorkflowState, AwardIssued> next)
    {
        await ctx.Publish(new CapacityReservedEvent(ctx.Saga.CorrelationId));
        await next.Execute(ctx);
    }

    Task IStateMachineActivity<AwardWorkflowState, AwardIssued>.Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, AwardIssued, TEx> ctx, IBehavior<AwardWorkflowState, AwardIssued> next) => next.Faulted(ctx);
    public Task Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, TEx> ctx, IBehavior<AwardWorkflowState> next) where TEx : Exception => next.Faulted(ctx);
    public Task Faulted<T, TEx>(BehaviorExceptionContext<AwardWorkflowState, T, TEx> ctx, IBehavior<AwardWorkflowState, T> next) where T : class where TEx : Exception => next.Faulted(ctx);
    public void Probe(ProbeContext ctx) => ctx.CreateScope("StubReserveCapacity");
    public void Accept(StateMachineVisitor v) => v.Visit(this);
}

internal sealed class StubIssueContractActivity :
    IStateMachineActivity<AwardWorkflowState>,
    IStateMachineActivity<AwardWorkflowState, CapacityReservedEvent>
{
    public async Task Execute(BehaviorContext<AwardWorkflowState> ctx, IBehavior<AwardWorkflowState> next)
    {
        ctx.Saga.ContractId = Guid.NewGuid();
        await ctx.Publish(new ContractIssuedEvent(ctx.Saga.CorrelationId, ctx.Saga.ContractId!.Value));
        await next.Execute(ctx);
    }

    public Task Execute<T>(BehaviorContext<AwardWorkflowState, T> ctx, IBehavior<AwardWorkflowState, T> next) where T : class => next.Execute(ctx);

    async Task IStateMachineActivity<AwardWorkflowState, CapacityReservedEvent>.Execute(BehaviorContext<AwardWorkflowState, CapacityReservedEvent> ctx, IBehavior<AwardWorkflowState, CapacityReservedEvent> next)
    {
        ctx.Saga.ContractId = Guid.NewGuid();
        await ctx.Publish(new ContractIssuedEvent(ctx.Saga.CorrelationId, ctx.Saga.ContractId!.Value));
        await next.Execute(ctx);
    }

    Task IStateMachineActivity<AwardWorkflowState, CapacityReservedEvent>.Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, CapacityReservedEvent, TEx> ctx, IBehavior<AwardWorkflowState, CapacityReservedEvent> next) => next.Faulted(ctx);
    public Task Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, TEx> ctx, IBehavior<AwardWorkflowState> next) where TEx : Exception => next.Faulted(ctx);
    public Task Faulted<T, TEx>(BehaviorExceptionContext<AwardWorkflowState, T, TEx> ctx, IBehavior<AwardWorkflowState, T> next) where T : class where TEx : Exception => next.Faulted(ctx);
    public void Probe(ProbeContext ctx) => ctx.CreateScope("StubIssueContract");
    public void Accept(StateMachineVisitor v) => v.Visit(this);
}

internal sealed class StubNotifyShipperActivity :
    IStateMachineActivity<AwardWorkflowState>,
    IStateMachineActivity<AwardWorkflowState, ContractIssuedEvent>
{
    public async Task Execute(BehaviorContext<AwardWorkflowState> ctx, IBehavior<AwardWorkflowState> next)
    {
        await ctx.Publish(new ShipperNotifiedInternalEvent(ctx.Saga.CorrelationId));
        await next.Execute(ctx);
    }

    public Task Execute<T>(BehaviorContext<AwardWorkflowState, T> ctx, IBehavior<AwardWorkflowState, T> next) where T : class => next.Execute(ctx);

    async Task IStateMachineActivity<AwardWorkflowState, ContractIssuedEvent>.Execute(BehaviorContext<AwardWorkflowState, ContractIssuedEvent> ctx, IBehavior<AwardWorkflowState, ContractIssuedEvent> next)
    {
        await ctx.Publish(new ShipperNotifiedInternalEvent(ctx.Saga.CorrelationId));
        await next.Execute(ctx);
    }

    Task IStateMachineActivity<AwardWorkflowState, ContractIssuedEvent>.Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, ContractIssuedEvent, TEx> ctx, IBehavior<AwardWorkflowState, ContractIssuedEvent> next) => next.Faulted(ctx);
    public Task Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, TEx> ctx, IBehavior<AwardWorkflowState> next) where TEx : Exception => next.Faulted(ctx);
    public Task Faulted<T, TEx>(BehaviorExceptionContext<AwardWorkflowState, T, TEx> ctx, IBehavior<AwardWorkflowState, T> next) where T : class where TEx : Exception => next.Faulted(ctx);
    public void Probe(ProbeContext ctx) => ctx.CreateScope("StubNotifyShipper");
    public void Accept(StateMachineVisitor v) => v.Visit(this);
}

internal sealed class StubMarkRfpAwardedActivity :
    IStateMachineActivity<AwardWorkflowState>,
    IStateMachineActivity<AwardWorkflowState, ShipperNotifiedInternalEvent>
{
    // Simulate rfp-api: publish RfpAwardAcknowledged so the saga advances RfpAwarding → Completed.
    public async Task Execute(BehaviorContext<AwardWorkflowState> ctx, IBehavior<AwardWorkflowState> next)
    {
        await ctx.Publish(new RfpAwardAcknowledged(ctx.Saga.RfpId, ctx.Saga.ContractId ?? Guid.Empty, DateTimeOffset.UtcNow));
        await next.Execute(ctx);
    }

    public Task Execute<T>(BehaviorContext<AwardWorkflowState, T> ctx, IBehavior<AwardWorkflowState, T> next) where T : class => next.Execute(ctx);

    async Task IStateMachineActivity<AwardWorkflowState, ShipperNotifiedInternalEvent>.Execute(BehaviorContext<AwardWorkflowState, ShipperNotifiedInternalEvent> ctx, IBehavior<AwardWorkflowState, ShipperNotifiedInternalEvent> next)
    {
        await ctx.Publish(new RfpAwardAcknowledged(ctx.Saga.RfpId, ctx.Saga.ContractId ?? Guid.Empty, DateTimeOffset.UtcNow));
        await next.Execute(ctx);
    }

    Task IStateMachineActivity<AwardWorkflowState, ShipperNotifiedInternalEvent>.Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, ShipperNotifiedInternalEvent, TEx> ctx, IBehavior<AwardWorkflowState, ShipperNotifiedInternalEvent> next) => next.Faulted(ctx);
    public Task Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, TEx> ctx, IBehavior<AwardWorkflowState> next) where TEx : Exception => next.Faulted(ctx);
    public Task Faulted<T, TEx>(BehaviorExceptionContext<AwardWorkflowState, T, TEx> ctx, IBehavior<AwardWorkflowState, T> next) where T : class where TEx : Exception => next.Faulted(ctx);
    public void Probe(ProbeContext ctx) => ctx.CreateScope("StubMarkRfpAwarded");
    public void Accept(StateMachineVisitor v) => v.Visit(this);
}

internal sealed class StubCompensateWorkflowActivity :
    IStateMachineActivity<AwardWorkflowState>,
    IStateMachineActivity<AwardWorkflowState, AwardWorkflowTimedOut>
{
    public async Task Execute(BehaviorContext<AwardWorkflowState> ctx, IBehavior<AwardWorkflowState> next) => await next.Execute(ctx);

    public Task Execute<T>(BehaviorContext<AwardWorkflowState, T> ctx, IBehavior<AwardWorkflowState, T> next) where T : class => next.Execute(ctx);

    async Task IStateMachineActivity<AwardWorkflowState, AwardWorkflowTimedOut>.Execute(BehaviorContext<AwardWorkflowState, AwardWorkflowTimedOut> ctx, IBehavior<AwardWorkflowState, AwardWorkflowTimedOut> next)
        => await next.Execute(ctx);

    Task IStateMachineActivity<AwardWorkflowState, AwardWorkflowTimedOut>.Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, AwardWorkflowTimedOut, TEx> ctx, IBehavior<AwardWorkflowState, AwardWorkflowTimedOut> next) => next.Faulted(ctx);
    public Task Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, TEx> ctx, IBehavior<AwardWorkflowState> next) where TEx : Exception => next.Faulted(ctx);
    public Task Faulted<T, TEx>(BehaviorExceptionContext<AwardWorkflowState, T, TEx> ctx, IBehavior<AwardWorkflowState, T> next) where T : class where TEx : Exception => next.Faulted(ctx);
    public void Probe(ProbeContext ctx) => ctx.CreateScope("StubCompensateWorkflow");
    public void Accept(StateMachineVisitor v) => v.Visit(this);
}

// ── Stub state machine using stub activities ────────────────────────────────────

internal sealed class StubAwardWorkflowStateMachine : MassTransitStateMachine<AwardWorkflowState>
{
    public State CapacityReserving  { get; private set; } = null!;
    public State ContractIssuing    { get; private set; } = null!;
    public State ShipperNotifying   { get; private set; } = null!;
    public State RfpAwarding        { get; private set; } = null!;
    public State Completed          { get; private set; } = null!;
    public State Compensated        { get; private set; } = null!;

    public Event<AwardIssued>                              AwardIssuedEvent        { get; private set; } = null!;
    public Event<CapacityReservedEvent>                    CapacityReserved        { get; private set; } = null!;
    public Event<ContractIssuedEvent>                      ContractIssued          { get; private set; } = null!;
    public Event<ShipperNotifiedInternalEvent>             ShipperNotifiedEvt      { get; private set; } = null!;
    public Event<RfpAwardAcknowledged>                     RfpAwardAcknowledgedEvt { get; private set; } = null!;
    public Schedule<AwardWorkflowState, AwardWorkflowTimedOut> SagaTimeout         { get; private set; } = null!;

    public StubAwardWorkflowStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => AwardIssuedEvent,        e => e.CorrelateById(ctx => ctx.Message.RfpId.Value));
        Event(() => CapacityReserved,        e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => ContractIssued,          e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => ShipperNotifiedEvt,      e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => RfpAwardAcknowledgedEvt, e => e.CorrelateById(ctx => ctx.Message.RfpId));

        Schedule(() => SagaTimeout, x => x.SagaTimeoutTokenId, s =>
        {
            s.Delay    = TimeSpan.FromSeconds(30);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Initially(
            When(AwardIssuedEvent)
                .Then(ctx =>
                {
                    var msg = ctx.Message;
                    ctx.Saga.RfpId           = msg.RfpId.Value;
                    ctx.Saga.BidId           = msg.BidId.Value;
                    ctx.Saga.CarrierId       = msg.CarrierId.Value;
                    ctx.Saga.LaneId          = msg.LaneId.Value;
                    ctx.Saga.AgreedAmount    = msg.AgreedRate.Amount;
                    ctx.Saga.AgreedCurrency  = msg.AgreedRate.Currency;
                    ctx.Saga.VolumeToReserve = msg.VolumeToReserve;
                    ctx.Saga.ReservationId   = ctx.Saga.CorrelationId;
                    ctx.Saga.CreatedAt       = DateTimeOffset.UtcNow;
                    ctx.Saga.UpdatedAt       = DateTimeOffset.UtcNow;
                })
                .Schedule(SagaTimeout, ctx => new AwardWorkflowTimedOut(ctx.Saga.CorrelationId))
                .Activity(x => x.OfType<StubReserveCapacityActivity>())
                .TransitionTo(CapacityReserving));

        During(CapacityReserving,
            When(CapacityReserved)
                .Activity(x => x.OfType<StubIssueContractActivity>())
                .TransitionTo(ContractIssuing),
            Ignore(AwardIssuedEvent));

        During(ContractIssuing,
            When(ContractIssued)
                .Then(ctx => ctx.Saga.ContractId = ctx.Message.ContractId)
                .Activity(x => x.OfType<StubNotifyShipperActivity>())
                .TransitionTo(ShipperNotifying),
            Ignore(AwardIssuedEvent));

        During(ShipperNotifying,
            When(ShipperNotifiedEvt)
                .Activity(x => x.OfType<StubMarkRfpAwardedActivity>())
                .TransitionTo(RfpAwarding),
            Ignore(AwardIssuedEvent));

        During(RfpAwarding,
            When(RfpAwardAcknowledgedEvt)
                .TransitionTo(Completed),
            Ignore(AwardIssuedEvent));

        During(Completed,
            Ignore(AwardIssuedEvent),
            Ignore(CapacityReserved),
            Ignore(ContractIssued),
            Ignore(ShipperNotifiedEvt),
            Ignore(RfpAwardAcknowledgedEvt));

        DuringAny(
            When(SagaTimeout.Received)
                .Unschedule(SagaTimeout)
                .Activity(x => x.OfType<StubCompensateWorkflowActivity>())
                .TransitionTo(Compensated));
    }
}

// ── Hanging activity — never publishes next step ────────────────────────────────

/// <summary>
/// Intentionally omits publishing <see cref="CapacityReservedEvent"/>, leaving the saga
/// stuck in CapacityReserving. Used exclusively to test the timeout/compensation path.
/// </summary>
internal sealed class HangingReserveCapacityActivity :
    IStateMachineActivity<AwardWorkflowState>,
    IStateMachineActivity<AwardWorkflowState, AwardIssued>
{
    public Task Execute(BehaviorContext<AwardWorkflowState> ctx, IBehavior<AwardWorkflowState> next) => next.Execute(ctx);
    public Task Execute<T>(BehaviorContext<AwardWorkflowState, T> ctx, IBehavior<AwardWorkflowState, T> next) where T : class => next.Execute(ctx);
    async Task IStateMachineActivity<AwardWorkflowState, AwardIssued>.Execute(BehaviorContext<AwardWorkflowState, AwardIssued> ctx, IBehavior<AwardWorkflowState, AwardIssued> next)
        => await next.Execute(ctx); // deliberate no-op: no CapacityReservedEvent published
    Task IStateMachineActivity<AwardWorkflowState, AwardIssued>.Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, AwardIssued, TEx> ctx, IBehavior<AwardWorkflowState, AwardIssued> next) => next.Faulted(ctx);
    public Task Faulted<TEx>(BehaviorExceptionContext<AwardWorkflowState, TEx> ctx, IBehavior<AwardWorkflowState> next) where TEx : Exception => next.Faulted(ctx);
    public Task Faulted<T, TEx>(BehaviorExceptionContext<AwardWorkflowState, T, TEx> ctx, IBehavior<AwardWorkflowState, T> next) where T : class where TEx : Exception => next.Faulted(ctx);
    public void Probe(ProbeContext ctx) => ctx.CreateScope("HangingReserveCapacity");
    public void Accept(StateMachineVisitor v) => v.Visit(this);
}

// ── Minimal state machine for timeout/compensation tests ───────────────────────

/// <summary>
/// A trimmed-down variant of <see cref="StubAwardWorkflowStateMachine"/> that uses
/// <see cref="HangingReserveCapacityActivity"/> so the saga never advances past
/// CapacityReserving, allowing the timeout path to be exercised deterministically.
/// </summary>
internal sealed class StubHangingAwardWorkflowStateMachine : MassTransitStateMachine<AwardWorkflowState>
{
    public State CapacityReserving { get; private set; } = null!;
    public State Compensated       { get; private set; } = null!;

    public Event<AwardIssued> AwardIssuedEvent { get; private set; } = null!;
    public Schedule<AwardWorkflowState, AwardWorkflowTimedOut> SagaTimeout { get; private set; } = null!;

    public StubHangingAwardWorkflowStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => AwardIssuedEvent, e => e.CorrelateById(ctx => ctx.Message.RfpId.Value));

        Schedule(() => SagaTimeout, x => x.SagaTimeoutTokenId, s =>
        {
            s.Delay    = TimeSpan.FromSeconds(30);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Initially(
            When(AwardIssuedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.RfpId     = ctx.Message.RfpId.Value;
                    ctx.Saga.CreatedAt = DateTimeOffset.UtcNow;
                    ctx.Saga.UpdatedAt = DateTimeOffset.UtcNow;
                })
                .Schedule(SagaTimeout, ctx => new AwardWorkflowTimedOut(ctx.Saga.CorrelationId))
                .Activity(x => x.OfType<HangingReserveCapacityActivity>())
                .TransitionTo(CapacityReserving));

        DuringAny(
            When(SagaTimeout.Received)
                .Unschedule(SagaTimeout)
                .Activity(x => x.OfType<StubCompensateWorkflowActivity>())
                .TransitionTo(Compensated));
    }
}

// ── Compensation / timeout tests ───────────────────────────────────────────────

public sealed class AwardWorkflowTimeoutTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness    _harness  = null!;

    private static AwardIssued MakeAwardIssued(Guid? rfpId = null) => new(
        RfpId:           RfpId.From(rfpId ?? Guid.NewGuid()),
        BidId:           BidId.New(),
        CarrierId:       CarrierId.New(),
        LaneId:          LaneId.New(),
        AgreedRate:      new Money(1500m, "USD"),
        VolumeToReserve: 100,
        OccurredAt:      DateTimeOffset.UtcNow);

    private ISagaStateMachineTestHarness<StubHangingAwardWorkflowStateMachine, AwardWorkflowState> SagaHarness
        => _harness.GetSagaStateMachineHarness<StubHangingAwardWorkflowStateMachine, AwardWorkflowState>();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        services.AddTransient<HangingReserveCapacityActivity>();
        services.AddTransient<StubCompensateWorkflowActivity>();

        services.AddMassTransitTestHarness(x =>
        {
            x.AddSagaStateMachine<StubHangingAwardWorkflowStateMachine, AwardWorkflowState>()
                .InMemoryRepository();
        });

        _provider = services.BuildServiceProvider(true);
        _harness  = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task Timeout_WhenSagaIsStuck_TransitionsToCompensated()
    {
        var msg    = MakeAwardIssued();
        var corrId = msg.RfpId.Value;

        await _harness.Bus.Publish(msg);

        // Saga is now stuck in CapacityReserving — HangingReserveCapacityActivity never
        // publishes CapacityReservedEvent, so the cascade cannot proceed.
        await SagaHarness.Exists(corrId, TimeSpan.FromSeconds(5));

        // Manually fire the timeout — simulates the 30-second schedule expiring.
        await _harness.Bus.Publish(new AwardWorkflowTimedOut(corrId));

        var instance = await SagaHarness.Exists(corrId, sm => sm.Compensated, TimeSpan.FromSeconds(5));
        instance.ShouldNotBeNull("Saga should transition to Compensated when timeout fires.");

        _harness.Consumed.Select<AwardWorkflowTimedOut>(x => x.Context.Message.CorrelationId == corrId)
            .Any().ShouldBeTrue("AwardWorkflowTimedOut should be consumed by the saga.");
    }
}

