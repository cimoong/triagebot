using System.Text;
using System.Text.RegularExpressions;

namespace TriageBot.Core.Domain;

/// <summary>
/// Central, UI-independent guardrail for untrusted ticket input. Enforces length bounds, rejects empty
/// values, and sanitizes control characters BEFORE any text is persisted or sent to an LLM. Kept in Core
/// (no dependencies) so the same rules apply everywhere a ticket can enter the system — the Blazor form,
/// tests, and any future API — rather than living only in a UI attribute that a new caller could bypass.
/// </summary>
public static partial class TicketValidator
{
    public const int MinSubjectLength = 3;
    public const int MaxSubjectLength = 200;
    public const int MinBodyLength = 5;
    // A support ticket body is prose, not a document. Capping it bounds prompt size (and therefore token
    // cost) and blunts prompt-injection payloads that rely on burying instructions in a long wall of text.
    public const int MaxBodyLength = 5000;
    public const int MaxEmailLength = 254; // RFC 5321 max length of an email address.

    /// <summary>The outcome of validating one ticket. <see cref="IsValid"/> with an empty error list on success.</summary>
    public readonly record struct ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
    {
        public static ValidationResult Ok { get; } = new(true, Array.Empty<string>());
        public static ValidationResult Fail(IReadOnlyList<string> errors) => new(false, errors);
        public string ErrorSummary => string.Join(" ", Errors);
    }

    /// <summary>
    /// Validates the raw subject/body/email of a ticket. Call <see cref="Sanitize"/> on each field before
    /// persisting; validation is done on the sanitized values in the caller for a single source of truth.
    /// </summary>
    public static ValidationResult Validate(string? subject, string? body, string? email)
    {
        var errors = new List<string>();

        var s = subject?.Trim() ?? string.Empty;
        if (s.Length < MinSubjectLength || s.Length > MaxSubjectLength)
            errors.Add($"Subject must be {MinSubjectLength}–{MaxSubjectLength} characters.");

        var b = body?.Trim() ?? string.Empty;
        if (b.Length < MinBodyLength || b.Length > MaxBodyLength)
            errors.Add($"Body must be {MinBodyLength}–{MaxBodyLength} characters.");

        var e = email?.Trim() ?? string.Empty;
        if (e.Length == 0 || e.Length > MaxEmailLength || !EmailRegex().IsMatch(e))
            errors.Add("A valid requester email is required.");

        return errors.Count == 0 ? ValidationResult.Ok : ValidationResult.Fail(errors);
    }

    /// <summary>
    /// Removes control characters (except tab/newline/carriage-return) and trims surrounding whitespace.
    /// This strips things like NUL and other C0 control codes that have no place in a support ticket and
    /// could be used to smuggle payloads past naive display or logging. It does NOT attempt to "detect"
    /// prompt injection in the wording — that is defended in depth at the prompt layer (the model is told
    /// to treat ticket text as untrusted data) and by the human-in-the-loop approval gate.
    /// </summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsControl(ch) && ch is not ('\t' or '\n' or '\r'))
                continue;
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
