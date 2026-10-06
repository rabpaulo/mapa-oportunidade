using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ceara.Api;

// Local detection of common identifiers, not a guarantee that text is anonymous.
// Reject instead of rewriting a question whose meaning may depend on those data.
public static partial class GeminiPrivacy
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    public const string RequestError = "O filtro de privacidade bloqueou a pergunta ou o histórico. Remova nomes pessoais, documentos, telefones, e-mails e endereços. Use Nova conversa para limpar o histórico.";
    private const string PayloadError = "O filtro de privacidade bloqueou dados na consulta ao assistente. Reformule usando município, ramo e estatísticas, sem dados pessoais.";

    [GeneratedRegex(@"[\p{L}\p{N}._%+\-]+@[\p{L}\p{N}.\-]+\.[\p{L}]{2,}", Options, 100)]
    private static partial Regex Email();
    [GeneratedRegex(@"(?<!\d)(?:\d{10,14}|\d{3}[.\s\-]?\d{3}[.\s\-]?\d{3}[.\s\-]?\d{2}|\d{2}[.\s]?\d{3}[.\s]?\d{3}[/\s]?\d{4}[\-\s]?\d{2})(?!\d)", Options, 100)]
    private static partial Regex Document();
    [GeneratedRegex(@"(?<!\d)(?:\+?55[.\s\-]*)?(?:\(\s*\d{2}\s*\)|\d{2})[.\s\-]*\d{4,5}[.\s\-]*\d{4}(?!\d)", Options, 100)]
    private static partial Regex Phone();
    [GeneratedRegex(@"(?<!\d)(?:\d{5}[.\s\-]\d{4}|\d{4}-\d{4})(?!\d)", Options, 100)]
    private static partial Regex LocalPhone();
    [GeneratedRegex(@"(?<!\d)\d{5}-\d{3}(?!\d)|\b(?:cpf|rg|cnpj|cep|telefone|tel|celular|whatsapp|e-?mail)\s*[:=]?\s*[+\d(]", Options, 100)]
    private static partial Regex LabeledIdentifier();
    [GeneratedRegex(@"\b(?:rua|avenida|av\.|travessa|estrada|rodovia|alameda|praça|praca|logradouro|endereço|endereco)\s+[^\r\n!?;]{0,100}?\b(?:\d{1,6}|s/n)\b", Options, 100)]
    private static partial Regex Address();
    [GeneratedRegex(@"\b(?:meu\s+nome(?:\s+completo)?\s+[eé]|nome(?:\s+completo)?\s*[:=]|(?:paciente|titular|respons[aá]vel)\s*[:=])\s*\p{L}", Options, 100)]
    private static partial Regex LabeledName();

    public static void EnsureSafeRequest(ChatRequest request)
    {
        EnsureText(request.Pergunta, RequestError);
        foreach (var message in request.Historico) EnsureText(message.Text, RequestError);
    }

    public static void EnsureSafePayload(JsonObject payload)
    {
        CheckParts(payload["systemInstruction"]?["parts"] as JsonArray);
        if (payload["contents"] is JsonArray contents)
            foreach (var content in contents) CheckParts(content?["parts"] as JsonArray);
    }

    private static void CheckParts(JsonArray? parts)
    {
        if (parts is null) return;
        foreach (var part in parts)
        {
            CheckValues(part?["text"]);
            CheckValues(part?["functionCall"]?["args"]);
            CheckValues(part?["functionResponse"]?["response"]);
            // Provider IDs and thought signatures must be echoed byte-for-byte.
        }
    }

    private static void CheckValues(JsonNode? node)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                EnsureText(text, PayloadError);
                break;
            case JsonValue value:
                EnsureText(value.ToJsonString(), PayloadError);
                break;
            case JsonObject obj:
                foreach (var property in obj)
                {
                    EnsureText(property.Key, PayloadError);
                    CheckValues(property.Value);
                }
                break;
            case JsonArray array:
                foreach (var item in array) CheckValues(item);
                break;
        }
    }

    private static void EnsureText(string text, string error)
    {
        try
        {
            var normalized = new StringBuilder(text.Length);
            foreach (var rune in text.Normalize(NormalizationForm.FormKC).EnumerateRunes())
            {
                var category = Rune.GetUnicodeCategory(rune);
                if (category == UnicodeCategory.Format) continue;
                normalized.Append(category == UnicodeCategory.DecimalDigitNumber
                    ? ((char)('0' + (int)Rune.GetNumericValue(rune))).ToString() : rune.ToString());
            }
            var value = normalized.ToString();
            if (Email().IsMatch(value) || Document().IsMatch(value) || Phone().IsMatch(value)
                || LabeledIdentifier().IsMatch(value) || Address().IsMatch(value) || LabeledName().IsMatch(value))
                throw new ApiError(error, 422);
            foreach (Match match in LocalPhone().Matches(value))
            {
                // Do not reject ordinary year ranges such as 2020-2025.
                var years = match.Value.Split('-');
                if (years.Length == 2 && years.All(y => int.TryParse(y, out var year) && year is >= 1800 and <= 2200)) continue;
                throw new ApiError(error, 422);
            }
        }
        catch (Exception exception) when (exception is RegexMatchTimeoutException or ArgumentException)
        {
            throw new ApiError(error, 422);
        }
    }
}
