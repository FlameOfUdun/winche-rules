namespace Winche.Rules.Evaluation;

/// <summary>
/// The port a consumer implements so rules can reference <b>other</b> documents via <c>get()</c>/<c>exists()</c>
/// (Firestore's cross-document conditions). Reads should be scoped/read-only, cached per request, and capped.
/// </summary>
public interface IDocumentSource
{
    /// <summary>Whether a document exists at <paramref name="path"/>.</summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// The document at <paramref name="path"/> as a map (typically <c>{data, id, __name__}</c>), or
    /// <see cref="RuleValue.Null"/> if it does not exist.
    /// </summary>
    Task<RuleValue> GetAsync(string path, CancellationToken ct = default);
}
