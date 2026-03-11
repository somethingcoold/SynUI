using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using StreamJsonRpc;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SynUI.Models;

namespace SynUI.Services
{
    public class LspManager : IDisposable
    {
        private static LspManager? _instance;
        public static LspManager Instance => _instance ??= new LspManager();

        private Process? _lspProcess;
        private JsonRpc? _rpc;
        private HashSet<string> _openedDocuments = new HashSet<string>();

        private static string LspDir => AppPaths.LspDir;
        private static string LspExePath => AppPaths.LspExePath;
        private static readonly string DownloadUrl = "https://github.com/JohnnyMorganz/luau-lsp/releases/latest/download/luau-lsp-win64.zip";

        public event Action<string>? OnStatusChanged;
        public bool IsReady { get; private set; }

        public async Task StartAsync()
        {
            if (IsReady) return;

            try
            {
                OnStatusChanged?.Invoke("Checking Luau LSP binary...");
                if (!File.Exists(LspExePath))
                {
                    await DownloadAndExtractLspAsync();
                }

                OnStatusChanged?.Invoke("Booting Luau LSP...");
                _lspProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = LspExePath,
                        Arguments = "lsp",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = LspDir
                    }
                };

                _lspProcess.Start();

                var formatter = new StreamJsonRpc.JsonMessageFormatter();
                formatter.JsonSerializer.NullValueHandling = NullValueHandling.Ignore;
                formatter.JsonSerializer.ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();

                // Order is (sendingStream, receivingStream, formatter)
                _rpc = new JsonRpc(new HeaderDelimitedMessageHandler(_lspProcess.StandardInput.BaseStream, _lspProcess.StandardOutput.BaseStream, formatter));
                _rpc.StartListening();

                OnStatusChanged?.Invoke("Initializing LSP...");

                // 1. Initialize Request
                var initParams = new
                {
                    processId = Process.GetCurrentProcess().Id,
                    rootUri = (string?)null,
                    capabilities = new { }
                };

                var initResult = await _rpc.InvokeWithParameterObjectAsync<JObject>("initialize", initParams);

                // 2. Initialized Notification
                await _rpc.NotifyAsync("initialized");

                IsReady = true;
                OnStatusChanged?.Invoke("Luau LSP Ready");
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"LSP Error: {ex.Message}");
                Debug.WriteLine($"[LSP] Start Error: {ex}");
            }
        }

        private async Task DownloadAndExtractLspAsync()
        {
            Directory.CreateDirectory(LspDir);
            string zipPath = Path.Combine(LspDir, "luau-lsp.zip");

            OnStatusChanged?.Invoke("Downloading Luau LSP...");
            
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            var response = await client.GetAsync(DownloadUrl);
            response.EnsureSuccessStatusCode();

            using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }

            OnStatusChanged?.Invoke("Extracting Luau LSP...");
            if (File.Exists(LspExePath)) File.Delete(LspExePath);
            
            ZipFile.ExtractToDirectory(zipPath, LspDir, true);
            File.Delete(zipPath);
        }

        public async Task OpenDocumentAsync(string uri, string text)
        {
            if (!IsReady || _rpc == null) return;
            if (_openedDocuments.Contains(uri)) return;

            var didOpenParams = new
            {
                textDocument = new
                {
                    uri = uri,
                    languageId = "luau",
                    version = 1,
                    text = text
                }
            };

            await _rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", didOpenParams);
            _openedDocuments.Add(uri);
        }

        public async Task UpdateDocumentAsync(string uri, string newText, int version)
        {
            if (!IsReady || _rpc == null) return;

            if (!_openedDocuments.Contains(uri))
            {
                await OpenDocumentAsync(uri, newText);
                return;
            }

            var didChangeParams = new
            {
                textDocument = new
                {
                    uri = uri,
                    version = version
                },
                contentChanges = new[]
                {
                    new { text = newText }
                }
            };

            await _rpc.NotifyWithParameterObjectAsync("textDocument/didChange", didChangeParams);
        }

        public async Task<List<LspCompletionItem>> GetCompletionsAsync(string uri, int line, int character, string fallbackText = "")
        {
            if (!IsReady || _rpc == null) return new List<LspCompletionItem>();

            if (!_openedDocuments.Contains(uri))
            {
                await OpenDocumentAsync(uri, fallbackText);
            }

            var completionParams = new
            {
                textDocument = new { uri = uri },
                position = new { line = line, character = character }
            };

            try
            {
                var result = await _rpc.InvokeWithParameterObjectAsync<JToken>("textDocument/completion", completionParams);
                var items = new List<LspCompletionItem>();

                if (result == null) return items;

                JArray? itemsArray = null;
                if (result.Type == JTokenType.Array)
                {
                    itemsArray = result as JArray;
                }
                else if (result.Type == JTokenType.Object)
                {
                    itemsArray = result["items"] as JArray;
                }

                if (itemsArray == null) return items;

                foreach (var item in itemsArray)
                {
                    items.Add(new LspCompletionItem
                    {
                        Label = item["label"]?.ToString() ?? "",
                        Detail = item["detail"]?.ToString(),
                        Kind = item["kind"]?.ToObject<int>() ?? 0,
                        InsertText = item["insertText"]?.ToString() ?? item["label"]?.ToString() ?? ""
                    });
                }

                return items;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LSP] Completion Error: {ex}");
                return new List<LspCompletionItem>();
            }
        }

        public void Dispose()
        {
            _rpc?.Dispose();
            if (_lspProcess != null && !_lspProcess.HasExited)
            {
                _lspProcess.Kill();
                _lspProcess.Dispose();
            }
        }
    }
}
