using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RoseBuild
{
    /// <summary>Admin-side settings, stored as simple key=value pairs.</summary>
    public sealed class AppConfig
    {
        public string PanelUrl = "";
        public string WebhookUrl = "";
        public string LicenseKey = "";
        public string Hwid = "";
        public string OutDir = "";
        public string LastName = "test";
        public string LastIcon = "";

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rose"); }
        }

        public static string Path_ { get { return Path.Combine(Dir, "config.ini"); } }

        public static AppConfig Load()
        {
            AppConfig cfg = new AppConfig();
            cfg.OutDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "builds");
            cfg.Hwid = ComputeHwid();

            try
            {
                if (!File.Exists(Path_))
                {
                    cfg.Save();
                    return cfg;
                }

                foreach (string line in File.ReadAllLines(Path_))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line.StartsWith("#"))
                        continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "panel": cfg.PanelUrl = value; break;
                        case "webhook": cfg.WebhookUrl = value; break;
                        case "license": cfg.LicenseKey = value; break;
                        case "hwid": if (value.Length > 0) cfg.Hwid = value; break;
                        case "outdir": if (value.Length > 0) cfg.OutDir = value; break;
                        case "lastname": cfg.LastName = value; break;
                        case "lasticon": cfg.LastIcon = value; break;
                    }
                }
            }
            catch
            {
                // defaults already in place
            }

            return cfg;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Rose builder settings");
                sb.AppendLine("panel=" + PanelUrl);
                sb.AppendLine("webhook=" + WebhookUrl);
                sb.AppendLine("license=" + LicenseKey);
                sb.AppendLine("hwid=" + Hwid);
                sb.AppendLine("outdir=" + OutDir);
                sb.AppendLine("lastname=" + LastName);
                sb.AppendLine("lasticon=" + LastIcon);
                File.WriteAllText(Path_, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        /// <summary>Stable per-machine identifier, sent to the panel as the device fingerprint.</summary>
        public static string ComputeHwid()
        {
            try
            {
                string material = "";
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                           @"SOFTWARE\Microsoft\Cryptography"))
                {
                    if (key != null)
                    {
                        object v = key.GetValue("MachineGuid");
                        if (v != null) material += v.ToString();
                    }
                }

                material += "|" + Environment.MachineName;
                material += "|" + Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture);

                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
                    StringBuilder sb = new StringBuilder(20);
                    for (int i = 0; i < 8; i++)
                        sb.Append(hash[i].ToString("X2"));
                    return sb.ToString();
                }
            }
            catch
            {
                return "UNKNOWN";
            }
        }
    }

    public sealed class BuildSpec
    {
        public string Name = "build";
        public string Extension = "exe";
        public string IconPath = "";
        public string FileDescription = "";
        public string ProductName = "";
        public string Company = "";
        public string FileVersion = "1.0.0.0";
        public string ProductVersion = "1.0.0.0";
        public string Copyright = "";
        public string DropName = "download.txt";
        public string PanelUrl = "";
        public string WebhookUrl = "";
        public string Tag = "";

        public string FileName
        {
            get
            {
                string ext = (Extension ?? "exe").Trim().TrimStart('.');
                if (ext.Length == 0) ext = "exe";
                return Name + "." + ext;
            }
        }

        public List<string> Validate()
        {
            List<string> errors = new List<string>();

            string name = (Name ?? "").Trim();
            if (name.Length == 0)
                errors.Add("Executable name is required.");
            else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                errors.Add("Executable name contains invalid characters.");
            else if (name.Length > 64)
                errors.Add("Executable name is too long.");

            string ext = (Extension ?? "").Trim().TrimStart('.');
            if (ext.Length == 0 || ext.Length > 8 || ext.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                errors.Add("Extension must be 1-8 characters, for example exe.");

            if (!string.IsNullOrEmpty(IconPath) && (!File.Exists(IconPath) ||
                !IconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)))
                errors.Add("Icon must be an existing .ico file.");

            if (!VersionUtil.IsValid(FileVersion))
                errors.Add("File version must look like 1.0.0.0.");
            if (!VersionUtil.IsValid(ProductVersion))
                errors.Add("Product version must look like 1.0.0.0.");

            if (!string.IsNullOrEmpty(PanelUrl))
            {
                Uri parsed;
                if (!Uri.TryCreate(PanelUrl, UriKind.Absolute, out parsed))
                    errors.Add("Panel URL must be a full URL.");
                else if (parsed.Scheme != Uri.UriSchemeHttps && !IsLoopback(parsed))
                    errors.Add("Panel URL must be https, unless it points at this machine.");
            }

            if (!string.IsNullOrEmpty(WebhookUrl))
            {
                Uri parsed;
                if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out parsed) || parsed.Scheme != Uri.UriSchemeHttps)
                    errors.Add("Webhook URL must be absolute https.");
            }

            return errors;
        }

        /// <summary>Plain http is allowed only for a local test panel.</summary>
        private static bool IsLoopback(Uri uri)
        {
            if (uri.Scheme != Uri.UriSchemeHttp)
                return false;

            return uri.IsLoopback ||
                   string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host == "127.0.0.1" ||
                   uri.Host == "::1";
        }
    }

    public static class VersionUtil
    {
        public static bool IsValid(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return false;

            string[] parts = version.Split('.');
            if (parts.Length < 2 || parts.Length > 4)
                return false;

            foreach (string part in parts)
            {
                int value;
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    return false;
                if (value < 0 || value > 65535)
                    return false;
            }

            return true;
        }

        public static long ToMsLs(string version, bool high)
        {
            int[] parts = new int[4];
            string[] split = (version ?? "1.0.0.0").Split('.');
            for (int i = 0; i < 4; i++)
            {
                int value = 0;
                if (i < split.Length)
                    int.TryParse(split[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                parts[i] = value;
            }

            if (high)
                return ((long)parts[0] << 16) | (uint)parts[1];
            return ((long)parts[2] << 16) | (uint)parts[3];
        }
    }
}
