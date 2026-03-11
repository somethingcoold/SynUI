using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using SynUI.Services;

namespace SynUI.API
{
    public class SynapseZAPI
    {
        private static string LatestErrorMsg = "";

        public static string GetLatestErrorMessage()
        {
            return LatestErrorMsg;
        }

        /// <summary>
        /// Execute a Lua script through SynapseZ.
        /// Returns: 0 = success, 1 = bin not found, 2 = scheduler not found, 3 = write error
        /// </summary>
        public static int Execute(string Script, int PID = 0)
        {
            string BinPath = AppPaths.SynapseZBinDir;

            if (!Directory.Exists(BinPath))
            {
                LatestErrorMsg = "Bin Folder not found";
                return 1;
            }

            string SchedulerPath = Path.Combine(BinPath, "scheduler");

            if (!Directory.Exists(SchedulerPath))
            {
                LatestErrorMsg = "Scheduler Folder not found";
                return 2;
            }

            string RandomFileName = RandomString(10) + ".lua";
            string FilePath = PID == 0
                ? Path.Combine(SchedulerPath, RandomFileName)
                : Path.Combine(SchedulerPath, "PID" + PID + "_" + RandomFileName);

            try
            {
                File.WriteAllText(FilePath, Script + "@@FileFullyWritten@@");
            }
            catch (Exception e)
            {
                LatestErrorMsg = e.Message;
                return 3;
            }

            return 0;
        }

        public static Nullable<DateTime> GetExpireDate()
        {
            String accKey = GetAccountKey();
            if (accKey == "")
            {
                LatestErrorMsg = "Could not find Account Key";
                return null;
            }

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "SYNZ-SERVICE");
            client.DefaultRequestHeaders.Add("key", accKey);

            HttpResponseMessage response = client.GetAsync("https://z-api.synapse.do/info").Result;

            if (((int)response.StatusCode) != 418)
            {
                LatestErrorMsg = "API Error: " + response.StatusCode.ToString();
                return null;
            }

            string responseBody = response.Content.ReadAsStringAsync().Result;
            int expireDate = int.Parse(responseBody);

            return DateTimeOffset.FromUnixTimeSeconds(expireDate).UtcDateTime;
        }

        public static int Redeem(String license)
        {
            String accKey = GetAccountKey();
            if (accKey == "")
            {
                LatestErrorMsg = "Could not find Account Key";
                return -1;
            }

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "SYNZ-SERVICE");
            client.DefaultRequestHeaders.Add("key", accKey);
            client.DefaultRequestHeaders.Add("license", license);

            HttpResponseMessage response = client.PostAsync("https://z-api.synapse.do/redeem", null).Result;

            if (((int)response.StatusCode) != 418)
            {
                if (((int)response.StatusCode) == 403)
                {
                    LatestErrorMsg = "Invalid License";
                    return -3;
                }
                LatestErrorMsg = "API Error: " + response.StatusCode.ToString();
                return -2;
            }

            string responseBody = response.Content.ReadAsStringAsync().Result;
            if (responseBody.StartsWith("Added"))
                return 0;

            LatestErrorMsg = "Invalid License";
            return -3;
        }

        public static int ResetHwid()
        {
            String accKey = GetAccountKey();
            if (accKey == "")
            {
                LatestErrorMsg = "Could not find Account Key";
                return -1;
            }

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "SYNZ-SERVICE");
            client.DefaultRequestHeaders.Add("key", accKey);

            HttpResponseMessage response = client.PostAsync("https://z-api.synapse.do/resethwid", null).Result;
            int statusCode = (int)response.StatusCode;

            switch (statusCode)
            {
                case 418: return 0;
                case 429:
                    LatestErrorMsg = "Cooldown";
                    return -3;
                case 403:
                    LatestErrorMsg = "Blacklisted";
                    return -4;
                default:
                    LatestErrorMsg = "API Error: " + response.StatusCode.ToString();
                    return -2;
            }
        }

        public static Process[] GetRobloxProcesses()
        {
            return Process.GetProcessesByName("RobloxPlayerBeta");
        }

        public static List<Process> GetSynzRobloxInstances()
        {
            Process[] processes = GetRobloxProcesses();
            List<Process> injectedProcesses = new List<Process>();
            for (int i = 0; i < processes.Length; i++)
            {
                if (IsSynz(processes[i].Id))
                    injectedProcesses.Add(processes[i]);
            }
            return injectedProcesses;
        }

        public static bool IsSynz(int PID = 0)
        {
            try
            {
                Process process = Process.GetProcessById(PID);
                string? path = process.MainModule?.FileName;
                if (path == null) return false;

                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] array = new byte[0x600];
                int bytesRead = stream.Read(array, 0, 0x600);
                string fileContent = System.Text.Encoding.Default.GetString(array, 0, bytesRead);
                return fileContent.Contains(".grh");
            }
            catch
            {
                return false;
            }
        }

        public static bool AreAllInstancesSynz()
        {
            Process[] processes = GetRobloxProcesses();
            if (processes.Length == 0) return false;
            return GetSynzRobloxInstances().Count == processes.Length;
        }

        public static string GetAccountKey()
        {
            string path = Path.Combine(AppPaths.SynapseZRoot, "auth_v2.syn");
            if (!File.Exists(path))
                return "";
            return File.ReadAllText(path);
        }

        private static Random random = new Random();
        private static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}
