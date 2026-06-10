namespace Winche.Rules.Evaluation;

/// <summary>
/// Raised when a condition cannot be evaluated (a type error, missing field, null navigation, or a
/// <c>get()</c>/<c>exists()</c> with no document source). Firestore treats such errors as the rule failing,
/// so the evaluator catches it and the condition yields <c>false</c> (deny).
/// </summary>
public sealed class RuleEvaluationException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public RuleEvaluationException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public RuleEvaluationException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Creates the exception with no message.</summary>
    public RuleEvaluationException() { }
}
