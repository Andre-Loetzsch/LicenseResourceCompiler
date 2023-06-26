using System.CommandLine;
using System.CommandLine.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Oleander.LicResComp.Tool;

internal class ToolConsole : IConsole
{
    private readonly ILogger _logger;
    private readonly SystemConsole _systemConsole = new();
    private readonly StringBuilder _output = new();
    private bool _hasErrors;

    public ToolConsole(ILogger logger)
    {
        this._logger = logger;

        this.Out = new StreamWriterDelegate(msg =>
        {
            this._systemConsole.Write(msg);
            this._output.Append(msg);

        });
        this.Error = new StreamWriterDelegate(msg =>
        {
            this._hasErrors = true;

            this._systemConsole.Write(this._output.Length < 1 ? 
                MSBuildLogFormatter.CreateMSBuildErrorFormat("LRC1", msg, "Oleander.LicResComp.Tool") : msg);

            this._output.Append(msg);
        });
    }

    public IStandardStreamWriter Out { get; }
    public IStandardStreamWriter Error { get; }

    public bool IsOutputRedirected { get; } = false;

    public bool IsErrorRedirected { get; } = false;
    public bool IsInputRedirected { get; } = false;

    public void Flush()
    {
        if (this._output.Length == 0) return;

        if (this._hasErrors)
        {
            this._logger.LogError("{stream.error}", this._output.ToString());
        }
        else
        {
            this._logger.LogInformation("{stream.out}", this._output.ToString());
        }

        this._output.Clear();
    }
}