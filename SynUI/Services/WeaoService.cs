using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SynUI.Services
{
    public class WeaoStatus
    {
        public bool IsUpdated { get; set; }
        public bool IsDetected { get; set; }
        public string RobloxVersion { get; set; } = "Unknown";
        public string ExploitVersion { get; set; } = "Unknown";
        public bool Success { get; set; }
    }

    public static class WeaoService
    {
        private static readonly HttpClient _client;

        static WeaoService()
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("User-Agent", "WEAO-3PService");
            _client.Timeout = TimeSpan.FromSeconds(8);
        }

        public static async Task<WeaoStatus> GetSynapseZStatusAsync()
        {
            try
            {
                var response = await _client.GetAsync("https://weao.xyz/api/status/exploits");

                if (!response.IsSuccessStatusCode)
                    return new WeaoStatus { Success = false };

                string json = await response.Content.ReadAsStringAsync();
                var exploits = JArray.Parse(json);

                foreach (var exploit in exploits)
                {
                    string? title = exploit["title"]?.ToString();
                    if (title != null && title.Equals("Synapse Z", StringComparison.OrdinalIgnoreCase))
                    {
                        return new WeaoStatus
                        {
                            Success = true,
                            IsUpdated = exploit["updateStatus"]?.Value<bool>() ?? false,
                            IsDetected = exploit["detected"]?.Value<bool>() ?? false,
                            RobloxVersion = exploit["rbxversion"]?.ToString() ?? "Unknown",
                            ExploitVersion = exploit["version"]?.ToString() ?? "Unknown"
                        };
                    }
                }

                return new WeaoStatus { Success = false };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WEAO] Error: {ex.Message}");
                return new WeaoStatus { Success = false };
            }
        }
    }
}
