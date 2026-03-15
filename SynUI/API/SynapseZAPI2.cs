using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SynUI.API
{
    /// <summary>
    /// Receives Roblox console output via a local HTTP server.
    /// Roblox scripts use syn.request / request to POST to http://localhost:1338/api/console.
    /// Body format: "TYPE:MESSAGE"  where TYPE is 0=print, 1=info, 2=warn, 3=error.
    /// </summary>
    public sealed class SynapseZAPI2 : IDisposable
    {
        public static readonly SynapseZAPI2 Instance = new();

        public const int Port = 1338;

        /// <summary>Fired on background thread — use Dispatcher.Invoke for UI updates.</summary>
        public event Action<int, string>? ConsoleOutput;

        /// <summary>Only messages bearing this token are forwarded. Rotated on each Hook injection.</summary>
        private volatile string _activeToken = "";

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        /// <summary>Generates a fresh token that invalidates all previously injected hooks.</summary>
        public string NewHookToken()
        {
            _activeToken = Guid.NewGuid().ToString("N").Substring(0, 8);
            return _activeToken;
        }

        // ── Lua hook script that Roblox executes to start forwarding console ──────
        // Token is embedded in every POST body so the server can reject stale hooks.
        public static string GetHookScript(int port, string token) => $@"
if _G._SynUIHook then pcall(function() _G._SynUIHook:Disconnect() end) _G._SynUIHook = nil end
local ls = game:GetService(""LogService"")
local _tk = ""{token}""
local _t = {{
    [Enum.MessageType.MessageOutput]  = 0,
    [Enum.MessageType.MessageInfo]    = 1,
    [Enum.MessageType.MessageWarning] = 2,
    [Enum.MessageType.MessageError]   = 3,
}}
_G._SynUIHook = ls.MessageOut:Connect(function(msg, mt)
    pcall(function()
        local req = (type(syn)==""table"" and type(syn.request)==""function"" and syn.request)
                 or (type(request)==""function"" and request)
        if req then
            req({{
                Url    = ""http://localhost:{port}/api/console"",
                Method = ""POST"",
                Headers = {{ [""Content-Type""] = ""text/plain"" }},
                Body   = _tk .. "":"" .. (_t[mt] or 0) .. "":"" .. tostring(msg),
            }})
        end
    end)
end)
print(""[SynUI] Console hooked!"")
";

        // ── Start / Stop ──────────────────────────────────────────────────────────

        public void Start()
        {
            if (_listener != null) return;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Start();

                Task.Run(() => ListenLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SynapseZAPI2] Failed to start HttpListener: {ex.Message}");
                _listener = null;
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        // ── HTTP Loop ─────────────────────────────────────────────────────────────

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener?.IsListening == true)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => HandleRequest(ctx), ct);
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");

                if (ctx.Request.HttpMethod == "POST" &&
                    ctx.Request.Url?.AbsolutePath == "/api/console")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                    var body = reader.ReadToEnd();
                    ParseAndFire(body);
                }

                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SynapseZAPI2] Request error: {ex.Message}");
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }

        private void ParseAndFire(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return;

            // Expected format: "TOKEN:TYPE:MESSAGE"
            // If token is empty (server started but hook never clicked) accept any.
            int first = body.IndexOf(':');
            if (first < 0) return;

            string incomingToken = body[..first];
            string rest = body[(first + 1)..];

            // Validate token — drop messages from stale hooks
            if (_activeToken.Length > 0 && incomingToken != _activeToken) return;

            int second = rest.IndexOf(':');
            int type   = 0;
            string text;

            if (second >= 0 && int.TryParse(rest.AsSpan(0, second), out int t) && t is >= 0 and <= 3)
            {
                type = t;
                text = rest[(second + 1)..].TrimEnd('\n', '\r');
            }
            else
            {
                text = rest.TrimEnd('\n', '\r');
            }

            ConsoleOutput?.Invoke(type, text);
        }

        // ── IDisposable ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
