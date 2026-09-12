using System.Net;
using System.Net.Http;
using AppleMusicDesktopLyrics;

internal static class LyricsFailureTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    internal static async Task Run()
    {
        void Require(bool condition, string label) { if (!condition) throw new Exception(label); }
        async Task<LyricsSearchResult> Query(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send)
        {
            using var http = new HttpClient(new Handler(send)) { BaseAddress = new Uri("https://test.invalid/") };
            return await new LyricsClient(http).SearchAsync("Test Song", "Test Artist", "Test Album", TimeSpan.FromSeconds(180), default);
        }
        var empty = await Query((_,_) => Task.FromResult(Json("[]")));
        Require(empty.Candidates.Count == 0 && empty.Failure is null, "successful empty response is not network failure");
        var offline = await Query((_,_) => throw new HttpRequestException("simulated offline"));
        Require(offline.Failure?.Contains("无法连接") == true, "offline reported accurately");
        var timeout = await Query((_,_) => throw new TaskCanceledException("simulated timeout"));
        Require(timeout.Failure?.Contains("超时") == true, "timeout reported accurately");
        var unavailable = await Query((_,_) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        Require(unavailable.Failure?.Contains("503") == true, "HTTP status preserved");
        foreach (var body in new[] { "not json", "null", "[null]", "[{}]" })
            Require((await Query((_,_) => Task.FromResult(Json(body)))).Failure?.Contains("无效数据") == true, "invalid data is not no lyrics");
        const string match = "[{\"id\":123,\"trackName\":\"Test Song\",\"artistName\":\"Test Artist\",\"albumName\":\"Test Album\",\"duration\":180,\"syncedLyrics\":\"[00:01.00]Test line\"}]";
        var partial = await Query((req,_) => req.RequestUri!.Query.Contains("artist_name")
            ? Task.FromResult(Json(match)) : throw new HttpRequestException("broad search failed"));
        Require(partial.Candidates.Count == 1 && partial.Failure is not null && partial.Candidates[0].Match.Confidence == LyricsMatchConfidence.High,
            "partial failure preserves usable candidate");
        var partialEmpty = await Query((req,_) => req.RequestUri!.Query.Contains("artist_name")
            ? Task.FromResult(Json("[]")) : throw new HttpRequestException("broad search failed"));
        Require(partialEmpty.Failure is not null, "partial empty response must not claim absence");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        using var cancelledHttp = new HttpClient(new Handler((_,token) => Task.FromCanceled<HttpResponseMessage>(token))) { BaseAddress = new Uri("https://test.invalid/") };
        try { await new LyricsClient(cancelledHttp).SearchAsync("a", "b", "c", TimeSpan.Zero, cts.Token); throw new Exception("expected cancellation"); }
        catch (OperationCanceledException) { }
        Console.WriteLine("Lyrics network, service, empty response, partial success and cancellation tests passed.");
    }
}
