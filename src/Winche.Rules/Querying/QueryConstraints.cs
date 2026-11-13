using Winche.Rules.Expressions;

namespace Winche.Rules.Querying;

/// <summary>
/// A single guarantee a query makes about every possible result: the field at <see cref="Field"/> (a path
/// under <c>resource.data</c>) satisfies <c>field <see cref="Operator"/> <see cref="Value"/></c>. The consumer
/// maps each of its query's where-clauses to one of these.
/// </summary>
/// <param name="Field">The field path under <c>resource.data</c> (e.g. <c>["ownerId"]</c>).</param>
/// <param name="Operator">The operator the query enforces.</param>
/// <param name="Value">The value the query enforces.</param>
public sealed record QueryConstraint(IReadOnlyList<string> Field, ComparisonOperator Operator, Value Value);

/// <summary>
/// The constraints a <c>list</c> query carries — the collection it targets and the per-result guarantees
/// from its where-clauses. The analyzer proves these satisfy the read rule, else the query is rejected.
/// </summary>
/// <param name="Collection">The collection path queried (e.g. <c>users</c> or <c>users/u1/posts</c>) — or, for a
/// collection group query, the collection id (<c>posts</c>).</param>
/// <param name="Constraints">The constraints from the query's where-clauses.</param>
/// <param name="CollectionGroup">
/// True for a collection group query: every collection named <paramref name="Collection"/>, under any parent.
/// Only rules that cover every such collection govern it: <c>{path=**}/&lt;id&gt;/{doc}</c> (or a capture
/// for the id) and a bare <c>{path=**}</c>.
/// </param>
public sealed record QueryConstraints(string Collection, IReadOnlyList<QueryConstraint> Constraints, bool CollectionGroup = false);
