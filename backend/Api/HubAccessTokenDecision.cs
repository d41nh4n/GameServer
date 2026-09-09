using Microsoft.Extensions.Primitives;

namespace GamePanel.Api;

/// <summary>
/// Czysta logika decyzyjna dla query-tokenów SignalR huba (CR-00C).
/// Nie parsuje surowego QueryString (Request.Query jest już zdekodowany) i nie
/// zależy od HttpRequest — przyjmuje już wyekstrahowane fakty, więc jest łatwa
/// do testów jednostkowych. Zasady (fail-closed):
///   - tylko na ścieżce huba (onHubPath),
///   - nagłówek Authorization ma zawsze pierwszeństwo (obecny → null),
///   - akceptuj dokładnie jeden, niepusty token z query (count==1, niepusty),
///   - w przeciwnym razie null (nic nie nadpisujemy → normalny przepływ auth).
/// </summary>
public sealed class HubAccessTokenDecision
{
    private HubAccessTokenDecision() { }

    private static bool Present(StringValues? v)
    {
        var sv = v ?? StringValues.Empty;
        return !StringValues.IsNullOrEmpty(sv);
    }

    /// <summary>
    /// Zwraca token z query do przekazania jako ctx.Token, lub null, gdy nie należy
    /// nadpisywać (header obecny / nie hub / brak jednoznacznego tokenu).
    /// </summary>
    public static string? Resolve(bool onHubPath, StringValues? authorizationHeader, StringValues? queryAccessToken)
    {
        if (!onHubPath) return null;

        // Nagłówek Authorization ma pierwszeństwo: gdy obecny, w ogóle nie używamy query.
        if (Present(authorizationHeader)) return null;

        var sv = queryAccessToken ?? StringValues.Empty;
        if (StringValues.IsNullOrEmpty(sv)) return null;
        if (sv.Count != 1) return null; // niejednoznaczne / puste / multi

        var arr = sv.ToArray<string>();
        var token = arr.Length > 0 ? arr[0] : null;
        return (token != null && token.Length > 0) ? token : null;
    }
}