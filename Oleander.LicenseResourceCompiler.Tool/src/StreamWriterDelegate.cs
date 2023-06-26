using System;
using System.CommandLine.IO;

namespace Oleander.LicResComp.Tool;

internal class StreamWriterDelegate : IStandardStreamWriter
{
    private readonly Action<string> _writeAction;

    public StreamWriterDelegate(Action<string> writeAction)
    {
        this._writeAction = writeAction;
    }

    public void Write(string? value)
    {
        if (value == null) return;

        this._writeAction(value);
    }
}