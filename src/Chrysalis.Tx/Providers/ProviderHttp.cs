using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Chrysalis.Tx.Providers;

/// <summary>Applies the provider contract to requests made by one HTTP client.</summary>
/// <param name="client">The caller-owned client.</param>
/// <param name="provider">The fixed provider name.</param>
/// <param name="secrets">Configured credentials and their derived pieces.</param>
/// <param name="jsonOptions">Optional JSON options for successful responses.</param>
public sealed class ProviderHttp(HttpClient client, string provider, IReadOnlyList<string> secrets, JsonSerializerOptions? jsonOptions = null)
{
    private const int DetailLimit = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string[] _secrets = OrderSecrets(secrets);

    /// <summary>Sends through the headers. Dispose a successful response after reading it.</summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="operation">A fixed operation name.</param>
    /// <param name="isAbsent">Optional endpoint-specific recognition of a 404 body.</param>
    /// <param name="cancellationToken">Cancels sending or reading.</param>
    /// <returns>The successful response, or null for documented absence.</returns>
    public Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request, string operation,
        Func<string, bool>? isAbsent, CancellationToken cancellationToken) =>
        SendCoreAsync(request, operation, isAbsent, jsonRpc: false, cancellationToken);

    /// <summary>Sends a JSON-RPC request, recognizing a JSON-RPC error in an HTTP 400 answer.</summary>
    public async Task<HttpResponseMessage> SendJsonRpcAsync(HttpRequestMessage request, string operation,
        CancellationToken cancellationToken) =>
        (await SendCoreAsync(request, operation, null, jsonRpc: true, cancellationToken).ConfigureAwait(false))!;

    private async Task<HttpResponseMessage?> SendCoreAsync(HttpRequestMessage request, string operation,
        Func<string, bool>? isAbsent, bool jsonRpc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw TransportFailure(provider, operation, error, cancellationToken);
        }

        if (response.IsSuccessStatusCode && !cancellationToken.IsCancellationRequested) { return response; }
        using (response)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string raw, bool complete) = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
            if (complete && response.StatusCode == HttpStatusCode.NotFound && isAbsent?.Invoke(raw) == true) { return null; }
            string what = FormattableString.Invariant($"HTTP {(int)response.StatusCode}");
            if (complete && jsonRpc && response.StatusCode == HttpStatusCode.BadRequest && TryReadRpcError(raw) is { } rpc)
            {
                what += FormattableString.Invariant($" (JSON-RPC error {rpc.Code})");
                raw = rpc.Message ?? "";
            }
            TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
            if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date)
            {
                TimeSpan remaining = date - DateTimeOffset.UtcNow;
                retryAfter = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            }
            throw new ProviderStatusException($"{provider} {operation}: {what}", response.StatusCode, retryAfter, RedactOrdered(raw, _secrets));
        }
    }

    private static (long Code, string? Message)? TryReadRpcError(string raw)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out JsonElement error) &&
                error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out JsonElement code) &&
                code.ValueKind == JsonValueKind.Number && code.TryGetInt64(out long number))
            {
                string? detail = null;
                if (error.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String)
                {
                    try { detail = message.GetString(); }
                    catch (InvalidOperationException) { /* An invalid string escape must not hide the HTTP or RPC status. */ }
                }
                return (number, detail);
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            // An unreadable or incomplete JSON-RPC error still has an HTTP status.
        }
        return null;
    }

    private async Task<(string Text, bool Complete)> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = BodyDeadline(cancellationToken);
        int chars = WindowSize(_secrets);
        byte[] bytes = new byte[checked(chars * 4)];
        try
        {
            Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            int total = 0;
            while (total < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes.AsMemory(total), deadline.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return (StrictUtf8.GetString(bytes, 0, total), true);
                }
                total += read;
            }
            cancellationToken.ThrowIfCancellationRequested();
            // At the cap we have not seen EOF; the prefix is diagnostic only.
            return (Encoding.UTF8.GetString(bytes, 0, total), false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ("", false);
        }
    }

    /// <summary>Gets and parses a response. Null means documented absence, not a JSON null.</summary>
    public async Task<T?> GetJsonAsync<T>(string relativeUri, string operation, Func<string, bool>? isAbsent,
        CancellationToken cancellationToken) where T : class
    {
        using HttpRequestMessage request = new(HttpMethod.Get, relativeUri);
        using HttpResponseMessage? response = await SendAsync(request, operation, isAbsent, cancellationToken).ConfigureAwait(false);
        return response is null ? null : await ReadJsonAsync<T>(response, operation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Streams a successful response as JSON; a null body is unusable.</summary>
    public async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource deadline = BodyDeadline(cancellationToken);
        T? body;
        try
        {
            Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            body = await JsonSerializer.DeserializeAsync<T>(stream, jsonOptions ?? JsonSerializerOptions.Web, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw TransportFailure(provider, operation, error, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return body ?? throw Unusable(provider, operation, "null JSON body");
    }

    private CancellationTokenSource BodyDeadline(CancellationToken cancellationToken)
    {
        CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(client.Timeout);
        return deadline;
    }

    /// <summary>Observes a whole batch, giving caller cancellation precedence over request failures.</summary>
    public static async Task<T[]> AwaitBatchAsync<T>(Task<T[]> batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        T[] results;
        try { results = await batch.ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Provider operation cancelled.", cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }

    /// <summary>Translates a transport or read failure without retaining the foreign exception.</summary>
    public static Exception TransportFailure(string provider, string operation, Exception error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (cancellationToken.IsCancellationRequested) { return new OperationCanceledException("Provider operation cancelled.", cancellationToken); }
        bool transient = false;
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
        {
            if (cause is HttpRequestException or IOException or SocketException or OperationCanceledException or TimeoutException)
            {
                transient = true;
                break;
            }
        }
        string type = error.GetType().Name;
        string what = error switch
        {
            HttpRequestException http => $"{type} ({http.HttpRequestError})",
            SocketException socket => $"{type} ({socket.SocketErrorCode})",
            JsonException json => FormattableString.Invariant($"unusable response ({type}, line {json.LineNumber}, byte {json.BytePositionInLine})"),
            _ => type
        };
        return new ProviderException($"{provider} {operation}: {what}", transient);
    }

    /// <summary>Reads one page at a time, validating its length before adding any item.</summary>
    public static async Task ReadAllPagesAsync<T>(Func<int, CancellationToken, Task<List<T>>> readPage, int firstPage,
        int pageSize, Func<List<T>, CancellationToken, Task> addPage, string provider, string operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readPage);
        ArgumentNullException.ThrowIfNull(addPage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        for (int page = firstPage; ; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<T> items = await readPage(page, cancellationToken).ConfigureAwait(false);
            if (items.Count > pageSize) { throw Unusable(provider, operation, "oversized page"); }
            cancellationToken.ThrowIfCancellationRequested();
            await addPage(items, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (items.Count < pageSize) { return; }
            if (page == int.MaxValue) { throw Unusable(provider, operation, "page number exhausted"); }
        }
    }

    /// <summary>Maps data, translating conversion errors without retaining foreign text.</summary>
    public static TOut Map<TIn, TOut>(TIn input, Func<TIn, TOut> map, string provider, string operation)
    {
        ArgumentNullException.ThrowIfNull(map);
        try
        {
            return map(input);
        }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentException or InvalidOperationException
            or KeyNotFoundException or InvalidCastException or IndexOutOfRangeException or JsonException)
        {
            throw Unusable(provider, operation, error.GetType().Name);
        }
    }

    /// <summary>Builds a non-transient failure from fixed wording, not response text.</summary>
    public static ProviderException Unusable(string provider, string operation, string what) =>
        new($"{provider} {operation}: unusable response: {what}", isTransient: false);

    /// <summary>Masks configured credentials and limits diagnostics to 1,024 characters.</summary>
    public static string Redact(string? text, IReadOnlyList<string> secrets) => RedactOrdered(text, OrderSecrets(secrets));
    /// <summary>Masks this client's configured credentials in a diagnostic.</summary>
    public string RedactDetail(string? text) => RedactOrdered(text, _secrets);

    private static string[] OrderSecrets(IReadOnlyList<string> secrets) => [.. secrets.Where(secret => !string.IsNullOrEmpty(secret))
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(secret => secret.Length)];

    private static int WindowSize(string[] ordered) => checked(DetailLimit + (ordered.Length > 0 ? ordered[0].Length - 1 : 0));

    private static string RedactOrdered(string? text, string[] ordered)
    {
        if (string.IsNullOrEmpty(text)) { return ""; }
        ReadOnlySpan<char> original = text.AsSpan(0, Math.Min(text.Length, WindowSize(ordered)));
        char[] masked = original.ToArray();
        foreach (string secret in ordered)
        {
            // Search the original, so overlapping credentials are both covered.
            int start = 0;
            while (start < original.Length)
            {
                int match = original[start..].IndexOf(secret, StringComparison.OrdinalIgnoreCase);
                if (match < 0) { break; }
                masked.AsSpan(start + match, secret.Length).Fill('*');
                start += match + 1;
            }
            for (int length = Math.Min(original.Length, secret.Length - 1); length >= 4; length--)
            {
                if (!secret.AsSpan().StartsWith(original[^length..], StringComparison.OrdinalIgnoreCase)) { continue; }
                masked.AsSpan(^length).Fill('*');
                break;
            }
        }
        // A credential made of masking characters cannot safely appear even in the mask.
        return ordered.Any(secret => secret.All(ch => ch == '*')) ? "" : new string(masked, 0, Math.Min(DetailLimit, masked.Length));
    }

    /// <summary>Extracts raw, encoded and decoded URL credential pieces.</summary>
    public static IEnumerable<string> SecretsOf(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        List<string> result = [uri.OriginalString];
        foreach (string part in uri.Host.Split('.').Prepend(uri.Host).Concat(uri.AbsolutePath.Split('/')))
        {
            AddSecret(result, part, 8);
        }
        AddUserInfo(uri.UserInfo);
        if (uri.Query.Length != 0) { AddQuery(uri.Query[1..]); }

        // Uri canonicalizes escapes such as %7E. An endpoint may echo the original component alone.
        string[] original = uri.OriginalString.Trim().Split('#', 2)[0].Split('?', 2);
        if (original.Length == 2) { AddQuery(original[1]); }
        ReadOnlySpan<char> rest = original[0].AsSpan();
        rest = rest[(rest.IndexOf(':') + 1)..];
        if (rest.Length >= 2 && rest[0] is '/' or '\\' && rest[1] is '/' or '\\')
        {
            rest = rest[2..];
            int slash = rest.IndexOfAny('/', '\\');
            ReadOnlySpan<char> authority = slash < 0 ? rest : rest[..slash];
            int at = authority.LastIndexOf('@');
            if (at >= 0) { AddUserInfo(authority[..at].ToString()); }
            if (slash >= 0)
            {
                foreach (string segment in rest[slash..].ToString().Split(['/', '\\'])) { AddSecret(result, segment, 8); }
            }
        }
        return result;

        void AddUserInfo(string userInfo)
        {
            foreach (string part in userInfo.Split(':', 2)) { AddSecret(result, part, 1); }
        }

        void AddQuery(string query)
        {
            foreach (string parameter in query.Split('&'))
            {
                string[] pair = parameter.Split('=', 2);
                AddSecret(result, pair[0], 8);
                if (pair.Length == 2)
                {
                    string value = pair[1];
                    AddSecret(result, value, 1);
                    // Some query parsers treat a literal '+' as space, but not an escaped plus.
                    if (value.Contains('+', StringComparison.Ordinal)) { AddSecret(result, value.Replace('+', ' '), 1); }
                }
            }
        }
    }

    private static void AddSecret(List<string> result, string raw, int minLength)
    {
        string decoded = Uri.UnescapeDataString(raw);
        if (decoded.Length < minLength) { return; }
        result.Add(raw);
        result.Add(decoded);
        result.Add(Uri.EscapeDataString(decoded));
    }

    /// <summary>Extracts a whole header and its individual credentials.</summary>
    public static IEnumerable<string> SecretsOfHeader(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        List<string> result = [value];
        string[] words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2 && (string.Equals(words[0], "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(words[0], "Basic", StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(words[1]);
        }
        foreach (string parameter in value.Split([',', ';']))
        {
            int equals = parameter.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0) { result.Add(parameter[(equals + 1)..].Trim().Trim('"')); }
        }
        return result;
    }
}
