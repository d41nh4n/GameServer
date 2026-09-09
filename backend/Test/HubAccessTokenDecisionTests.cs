using GamePanel.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

/// <summary>
/// Testy jednostkowe HubAccessTokenDecision — czysta, testowalna logika wyboru
/// query-tokena dla /hubs/server (bez HttpRequest/serwera): path-boundary,
/// precedencja nagłówka Authorization, jednoznaczność tokenu (fail-closed).
/// </summary>
public class HubAccessTokenDecisionTests
{
    private static StringValues? SV(params string[] v) => v.Length == 0 ? StringValues.Empty : (StringValues)v;
    private static StringValues? NULL_SV() => null;

    [Fact]
    public void NonHubPath_ReturnsNull()
    {
        Assert.Null(HubAccessTokenDecision.Resolve(false, NULL_SV(), SV("tok")));
    }

    [Fact]
    public void AuthorizationHeader_TakesPrecedence_OverQuery()
    {
        // Header obecny + query token → query NIE nadpisuje (return null).
        Assert.Null(HubAccessTokenDecision.Resolve(true, SV("Bearer token-valid"), SV("query-tok")));
    }

    [Fact]
    public void NoHeader_SingleQueryToken_IsAccepted()
    {
        Assert.Equal("tok", HubAccessTokenDecision.Resolve(true, NULL_SV(), SV("tok")));
    }

    [Fact]
    public void MissingQueryToken_ReturnsNull()
    {
        Assert.Null(HubAccessTokenDecision.Resolve(true, NULL_SV(), NULL_SV()));
    }

    [Fact]
    public void EmptyQueryToken_ReturnsNull()
    {
        Assert.Null(HubAccessTokenDecision.Resolve(true, NULL_SV(), SV("")));
    }

    [Fact]
    public void MultipleQueryTokens_AreRejected()
    {
        Assert.Null(HubAccessTokenDecision.Resolve(true, NULL_SV(), SV("a", "b")));
    }

    [Fact]
    public void InvalidQueryToken_CannotOverwriteValidAuthorizationHeader()
    {
        // Nawet zły query token nie wpływa, gdy jest ważny header.
        Assert.Null(HubAccessTokenDecision.Resolve(true, SV("Bearer good"), SV("evil")));
    }

    [Fact]
    public void HubPath_RejectsEvilPrefix()
    {
        // Granica ścieżki segmentowej (spójne z AccessTokenExtractionTests).
        var ok = PathString.FromUriComponent("/hubs/server").StartsWithSegments(
            PathString.FromUriComponent("/hubs/server"));
        var evil = PathString.FromUriComponent("/hubs/server-evil").StartsWithSegments(
            PathString.FromUriComponent("/hubs/server"));
        Assert.True(ok);
        Assert.False(evil);
    }
}