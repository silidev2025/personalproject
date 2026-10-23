using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace RoseBuild
{
    public sealed class LicenseState
    {
        public bool Active;
        public string Plan = "-";
        public string Expires = "-";
        public string Message = "offline";
    }

    /// <summary>
    /// Talks to the PHP panel. Every call is best effort: a dead panel must never
    /// stop a build or freeze the UI.
    /// </summary>
    public static class Api
    {
        private const int TimeoutMs = 6000;

        public static void CheckLicenseAsync(string panelUrl, string key, string hwid, Action<LicenseState> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                LicenseState state = new LicenseState();
                try
                {
                    if (string.IsNullOrEmpty(panelUrl))
                    {
                        state.Message = "no panel configured";
                        done(state);
                        return;
                    }

                    string body = "{\"key\":" + Quote(key) + ",\"hwid\":" + Quote(hwid) + "}";
                    string response = Post(panelUrl.TrimEnd('/') + "/api/license.php", body);
                    state.Active = ExtractBool(response, "active");
                    state.Plan = ExtractString(response, "plan", "-");
                    state.Expires = ExtractString(response, "expires", "-");
                    state.Message = ExtractString(response, "message", state.Active ? "active" : "rejected");
                }
                catch (Exception ex)
                {
                    state.Message = "panel unreachable: " + ex.Message;
                }
                done(state);
            });
        }

        public static void LogBuildAsync(string panelUrl, BuildSpec spec, string hwid, string outputPath, long size)
        {
            if (string.IsNullOrEmpty(panelUrl)) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("{\"event\":\"build\",\"hwid\":").Append(Quote(hwid));
                    sb.Append(",\"name\":").Append(Quote(spec.Name));
                    sb.Append(",\"file\":").Append(Quote(Path.GetFileName(outputPath)));
                    sb.Append(",\"size\":").Append(size);
                    sb.Append(",\"version\":").Append(Quote(spec.FileVersion));
                    sb.Append("}");
                    Post(panelUrl.TrimEnd('/') + "/api/build.php", sb.ToString());
                }
                catch
                {
                }
            });
        }

        public static void NotifyAsync(string webhookUrl, string appName, string summary)
        {
            if (string.IsNullOrEmpty(webhookUrl)) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("{\"username\":").Append(Quote(appName));
                    sb.Append(",\"allowed_mentions\":{\"parse\":[]}");
                    sb.Append(",\"content\":").Append(Quote(summary));
                    sb.Append('}');
                    Post(webhookUrl, sb.ToString());
                }
                catch
                {
                }
            });
        }

        private static string Post(string url, string json)
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            catch { }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.UserAgent = "Rose/1.0";
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.AllowAutoRedirect = false;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            req.ContentLength = bytes.Length;
            using (Stream s = req.GetRequestStream())
                s.Write(bytes, 0, bytes.Length);

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream rs = resp.GetResponseStream())
            using (StreamReader reader = new StreamReader(rs, Encoding.UTF8))
            {
                char[] buffer = new char[4096];
                int read = reader.Read(buffer, 0, buffer.Length);
                return read > 0 ? new string(buffer, 0, read) : "";
            }
        }

        // ---- tiny JSON field readers (panel responses are flat) ----

        public static string ExtractString(string json, string key, string fallback)
        {
            if (json == null) return fallback;
            int idx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (idx < 0) return fallback;
            idx = json.IndexOf(':', idx);
            if (idx < 0) return fallback;
            idx = json.IndexOf('"', idx);
            if (idx < 0) return fallback;
            int end = json.IndexOf('"', idx + 1);
            if (end < 0) return fallback;
            return json.Substring(idx + 1, end - idx - 1);
        }

        public static bool ExtractBool(string json, string key)
        {
            if (json == null) return false;
            int idx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (idx < 0) return false;
            idx = json.IndexOf(':', idx);
            if (idx < 0) return false;
            string rest = json.Substring(idx + 1).TrimStart();
            return rest.StartsWith("true", StringComparison.OrdinalIgnoreCase);
        }

        public static string Quote(string s)
        {
            if (s == null) s = "";
            StringBuilder sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
