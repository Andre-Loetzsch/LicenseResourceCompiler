using System.IO;
using System.Text;

namespace Oleander.LicResComp.Tool.ExternalProcesses
{
    public class ExternalProcessResult
    {
        public ExternalProcessResult(string exeFileName, string arguments)
        {
            this.ExeFileName = Path.GetFileName(exeFileName);
            this.CommandLine = string.Concat(exeFileName, " ", arguments);
        }

        public string ExeFileName { get; }

        public string CommandLine { get; }

        public Win32ExitCodes Win32ExitCode { get; internal set; } = Win32ExitCodes.ERROR_SUCCESS;
        public int ExitCode { get; internal set; }
        public string? StandardOutput { get; internal set; }
        public string? StandardErrorOutput { get; internal set; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.Append("Cmd:".PadRight(20)).AppendLine(this.CommandLine);
            sb.Append("ExeFileName:".PadRight(20)).AppendLine(this.ExeFileName);
            sb.Append("ExitCode:".PadRight(20)).AppendLine(this.ExitCode.ToString());
            sb.Append("Win32ExitCode:".PadRight(20)).AppendLine(this.Win32ExitCode.ToString());
            
            if (!string.IsNullOrEmpty(this.StandardOutput)) sb.AppendLine(this.StandardOutput);
            if (!string.IsNullOrEmpty(this.StandardErrorOutput)) sb.AppendLine(this.StandardErrorOutput);

            return sb.ToString();
        }
    }
}