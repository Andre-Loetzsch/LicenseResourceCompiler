using System;
using System.ComponentModel;
using System.Diagnostics;

namespace Oleander.LicResComp.Tool.ExternalProcesses;

public class ExternalProcess
{
    private readonly string _fileName;
    private readonly string _arguments;
    
    public ExternalProcess(string exeName, string arguments)
    {
        this._fileName = exeName;
        this._arguments = arguments;
    }

    public ExternalProcessResult Start()
    {
        var epr = new ExternalProcessResult(this._fileName, this._arguments);

        var p = new Process
        {
            StartInfo =
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                FileName = this._fileName,
                Arguments = this._arguments,
                CreateNoWindow = true,
                ErrorDialog = false
            }
        };

        try
        {
            if (!p.Start())
            {
                epr.ExitCode = -1;
                epr.StandardErrorOutput = $"Process '{p.MainWindowTitle}' did not start!";
                return epr;
            }
        }
        catch (Win32Exception ex)
        {
            epr.Win32ExitCode = (Win32ExitCodes)ex.NativeErrorCode;
            epr.ExitCode = ex.NativeErrorCode;
            epr.StandardErrorOutput = $"An error occurred while starting the process! ({ex.Message})";
            return epr;
        }
        catch (Exception ex)
        {
            epr.ExitCode = -1;
            epr.StandardErrorOutput = $"An error occurred while starting the process! ({ex.Message})";
            return epr;
        }

        if (!p.WaitForExit(30000))
        {
            try
            {
                epr.ExitCode = -2;
                epr.StandardErrorOutput = $"Try to kill the process {p.Id} because there is no response!";
                p.Kill();
            }
            catch (Exception ex)
            {
                epr.ExitCode = -1;
                epr.StandardErrorOutput = $"An error occurred while killing the process! ({ex.Message})";
                return epr;
            }
        }

        epr.StandardOutput = p.StandardOutput.ReadToEnd();
        epr.StandardErrorOutput = p.StandardError.ReadToEnd();

        epr.ExitCode = p.ExitCode;
        p.Close();
        p.Dispose();

        if (epr.ExitCode == 0)
        {
            return epr;
        }

        if (string.IsNullOrWhiteSpace(epr.StandardErrorOutput)) epr.StandardErrorOutput = epr.StandardOutput;

        return epr;
    }
}