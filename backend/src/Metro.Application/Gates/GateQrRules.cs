using System.Text;
using Metro.Application.Abstractions.Gates;

namespace Metro.Application.Gates;

public static class GateQrRules
{
    public static string NormalizeName(string name) => string.Join(' ', name.Normalize(NormalizationForm.FormKC)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static void Identity(Guid request, Guid operation, string action, string name, DateOnly birth, string reason, string key)
    {
        GateRules.Key(request, key);
        if (operation == Guid.Empty || operation == request || action is not ("DISPLAY" or "REPLACE") ||
            string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl) ||
            birth == DateOnly.MinValue || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw new GateException("INVALID_QR_SUPPORT_REQUEST", 400);
    }
    public static void Validate(GateQrChallengeRequest r, string key) => Identity(r.RequestId,r.OperationId,r.Action,r.FullName,r.BirthDate,r.Reason,key);
    public static void Validate(GateQrAccessRequest r, string key)
    {
        Identity(r.RequestId,r.OperationId,r.Action,r.FullName,r.BirthDate,r.Reason,key);
        if (r.VerificationMethod == "EMAIL")
        {
            if (r.ChallengeId is null || r.ChallengeId == Guid.Empty || r.EmailCode is not { Length: 6 } ||
                r.EmailCode.Any(c => c is < '0' or > '9') || r.NoEmailAccess || r.DocumentMatchesProfile || r.EvidenceReference is not null)
                throw new GateException("INVALID_QR_EMAIL_PROOF", 400);
        }
        else if (r.VerificationMethod != "DOCUMENT" || !r.NoEmailAccess || !r.DocumentMatchesProfile ||
            string.IsNullOrWhiteSpace(r.EvidenceReference) || r.EvidenceReference.Length > 1000 || r.ChallengeId is not null || r.EmailCode is not null)
            throw new GateException("QR_DOCUMENT_EVIDENCE_REQUIRED", 400);
    }
}
