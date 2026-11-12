using Microsoft.Extensions.DependencyInjection;
using Winche.Rules;
using Winche.Rules.DependencyInjection;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Json;
using Winche.Rules.Querying;
using Winche.Rules.Text;

// =============================================================================
// Winche.Rules — feature walkthrough
//
// Demonstrates: keyed per-type engines, single-resource evaluation, query
// authorization, cross-resource get()/exists(), rules-as-data (JSON), per-type
// comparers, and — the headline — hot-swapping a type's rules at runtime with
// no restart, through the built-in IRuleSetRepository seam.
// =============================================================================

// ----- RuleSets --------------------------------------------------------------

// Rules are written in the rules language (the same text the console's editor and `winche rules deploy`
// use) and parsed into a RuleSet.
// "documents": owners can always read/write their own doc. Public reads are only
// allowed in v2 — we start on v1 (owner-only) and hot-swap to v2 later.
// "reports/{id}" shows a cross-resource condition backed by IRuleResourceProvider.
static RuleSet DocumentsRules(bool allowPublicReads) => RuleText.ParseRuleSet($$"""
    match documents/{docId} {
      allow read: if {{(allowPublicReads ? "resource.public == true || " : "")}}resource.ownerId == request.auth.uid;
      allow write: if request.resource.ownerId == request.auth.uid;
    }

    // A report is readable only if team "teams/t1" exists and is active.
    match reports/{reportId} {
      allow read: if exists('teams/t1') && get('teams/t1').active == true;
    }
    """);

// "orders": a shipped order is readable. Its engine uses a case-insensitive
// comparer, so the rule's "SHIPPED" matches a resource whose status is "shipped".
static RuleSet OrdersRules() => RuleText.ParseRuleSet("""
    match orders/{orderId} {
      allow read: if resource.status == 'SHIPPED';
    }
    """);

// ----- Composition root ------------------------------------------------------

var services = new ServiceCollection();

// One isolated, hot-swappable engine per resource type. Each has its own ruleset,
// its own comparer, and its own IMutableRuleSetRepository — keys are never merged.
services.AddWincheRules("documents", o => o.WithMutableRuleSetRepository(DocumentsRules(allowPublicReads: false)));
services.AddWincheRules("orders", o => o.WithMutableRuleSetRepository(OrdersRules()).WithRuleValueComparer(new CaseInsensitiveComparer()));

using var sp = services.BuildServiceProvider();

var documents = sp.GetRequiredKeyedService<RuleEngine>("documents");
var orders = sp.GetRequiredKeyedService<RuleEngine>("orders");

Console.WriteLine("Winche.Rules — feature walkthrough");
Console.WriteLine(new string('=', 60));

// ----- 1. Single-resource evaluation (get / create) --------------------------

Section("1. Single-resource evaluation (documents, rules v1 = owner-only)");

var aliceDoc = Doc(ownerId: "alice", isPublic: true);

Show("alice reads her own doc",
    await documents.AllowsAsync(RuleOperation.Get, "documents/d1", Get(uid: "alice", resource: aliceDoc)));

Show("bob reads alice's PUBLIC doc (denied — v1 has no public rule yet)",
    await documents.AllowsAsync(RuleOperation.Get, "documents/d1", Get(uid: "bob", resource: aliceDoc)));

Show("alice creates a doc she owns",
    await documents.AllowsAsync(RuleOperation.Create, "documents/d2",
        Create(uid: "alice", incoming: Doc(ownerId: "alice", isPublic: false))));

Show("mallory creates a doc owned by alice (denied)",
    await documents.AllowsAsync(RuleOperation.Create, "documents/d2",
        Create(uid: "mallory", incoming: Doc(ownerId: "alice", isPublic: false))));

// ----- 2. HOT-SWAP: add public reads at runtime, no restart ------------------

Section("2. Hot-swap the documents ruleset at runtime (v1 -> v2)");

var documentsRepo = sp.GetRequiredKeyedService<IMutableRuleSetRepository>("documents");
Console.WriteLine("   ... swapping in v2 (owner OR public) via IMutableRuleSetRepository.Update()");
documentsRepo.Update(DocumentsRules(allowPublicReads: true));

Show("bob reads alice's PUBLIC doc (NOW allowed — same engine, new rules)",
    await documents.AllowsAsync(RuleOperation.Get, "documents/d1", Get(uid: "bob", resource: aliceDoc)));

Show("bob reads alice's PRIVATE doc (still denied)",
    await documents.AllowsAsync(RuleOperation.Get, "documents/d1",
        Get(uid: "bob", resource: Doc(ownerId: "alice", isPublic: false))));

Show("orders engine is untouched by the documents swap (shipped order readable)",
    await orders.AllowsAsync(RuleOperation.Get, "orders/o1",
        new RuleRequest { Resource = OrderStatus("shipped") }));

// ----- 3. Query authorization (list) -----------------------------------------

Section("3. Query authorization (static analysis, never runs the query)");

var listReq = new RuleRequest
{
    Request = RuleValue.Map(new Dictionary<string, RuleValue>
    {
        ["method"] = RuleValue.String("list"),
        ["auth"] = RuleValue.Map(new Dictionary<string, RuleValue> { ["uid"] = RuleValue.String("alice") }),
    }),
};

var ownerQuery = new QueryConstraints("documents",
    [new QueryConstraint(["ownerId"], ComparisonOperator.Eq, RuleValue.String("alice"))]);
var openQuery = new QueryConstraints("documents", []);

Show("list documents WHERE ownerId == alice (provably safe)", documents.Allows(ownerQuery, listReq));
Show("list ALL documents (rejected — cannot prove per-result safety)", documents.Allows(openQuery, listReq));

// ----- 4. Cross-resource conditions (get / exists) ---------------------------

Section("4. Cross-resource get()/exists() via IRuleResourceProvider");

var provider = new DemoResourceProvider(teamActive: true);
Show("read report while team t1 is active",
    await documents.AllowsAsync(RuleOperation.Get, "reports/r1",
        new RuleRequest { Provider = provider }));

var inactiveProvider = new DemoResourceProvider(teamActive: false);
Show("read report while team t1 is inactive (denied)",
    await documents.AllowsAsync(RuleOperation.Get, "reports/r1",
        new RuleRequest { Provider = inactiveProvider }));

// ----- 5. Rules as data (JSON round-trip) ------------------------------------

Section("5. Rules as data — JSON serialize/deserialize round-trip");

var json = RuleJson.Serialize(OrdersRules());
Console.WriteLine("   orders ruleset as JSON:");
Console.WriteLine("   " + json);
var restored = RuleJson.DeserializeRuleSet(json);
Show("deserialized ruleset equals the original", restored.Equals(OrdersRules()));

// ----- 6. Per-type comparer --------------------------------------------------

Section("6. Per-type comparer (orders uses a case-insensitive comparer)");

Show("orders: status \"shipped\" matches rule \"SHIPPED\" (case-insensitive comparer)",
    await orders.AllowsAsync(RuleOperation.Get, "orders/o1",
        new RuleRequest { Resource = OrderStatus("shipped") }));
Show("orders: status \"pending\" does not match",
    await orders.AllowsAsync(RuleOperation.Get, "orders/o1",
        new RuleRequest { Resource = OrderStatus("pending") }));

Console.WriteLine();
Console.WriteLine(new string('=', 60));
Console.WriteLine("Done. Rules changed at runtime with no recompile and no restart.");
return 0;

// ----- helpers ---------------------------------------------------------------

static void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine(new string('-', title.Length));
}

static void Show(string label, bool allowed) =>
    Console.WriteLine($"   [{(allowed ? "ALLOW" : "DENY ")}] {label}");

// A document resource map.
static RuleValue Doc(string ownerId, bool isPublic) => RuleValue.Map(new Dictionary<string, RuleValue>
{
    ["ownerId"] = RuleValue.String(ownerId),
    ["public"] = RuleValue.Bool(isPublic),
});

static RuleValue OrderStatus(string status) => RuleValue.Map(new Dictionary<string, RuleValue>
{
    ["status"] = RuleValue.String(status),
});

// A get/read request carrying the existing resource and the caller's uid.
static RuleRequest Get(string uid, RuleValue resource) => new()
{
    Resource = resource,
    Request = RuleValue.Map(new Dictionary<string, RuleValue>
    {
        ["method"] = RuleValue.String("get"),
        ["auth"] = RuleValue.Map(new Dictionary<string, RuleValue> { ["uid"] = RuleValue.String(uid) }),
    }),
};

// A create request carrying the incoming (post-write) resource as request.resource.
static RuleRequest Create(string uid, RuleValue incoming) => new()
{
    Request = RuleValue.Map(new Dictionary<string, RuleValue>
    {
        ["method"] = RuleValue.String("create"),
        ["auth"] = RuleValue.Map(new Dictionary<string, RuleValue> { ["uid"] = RuleValue.String(uid) }),
        ["resource"] = incoming,
    }),
};

// ----- supporting types ------------------------------------------------------

/// A comparer that treats strings case-insensitively and otherwise defers to the default semantics.
internal sealed class CaseInsensitiveComparer : IRuleValueComparer
{
    public bool AreEqual(RuleValue a, RuleValue b)
    {
        if (a.Kind == RuleValueKind.String && b.Kind == RuleValueKind.String)
            return string.Equals(a.AsString, b.AsString, StringComparison.OrdinalIgnoreCase);
        return a.Equals(b);
    }

    public bool TryCompare(RuleValue a, RuleValue b, out int result)
    {
        if (a.Kind == RuleValueKind.String && b.Kind == RuleValueKind.String)
        {
            result = string.Compare(a.AsString, b.AsString, StringComparison.OrdinalIgnoreCase);
            return true;
        }
        return a.TryCompareTo(b, out result);
    }
}

/// A demo store for get()/exists(): knows a single team resource whose active flag is configurable.
internal sealed class DemoResourceProvider(bool teamActive) : IRuleResourceProvider
{
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(path == "teams/t1");

    public Task<RuleValue> GetAsync(string path, CancellationToken ct = default)
        => Task.FromResult(path == "teams/t1"
            ? RuleValue.Map(new Dictionary<string, RuleValue> { ["active"] = RuleValue.Bool(teamActive) })
            : RuleValue.Null);
}
