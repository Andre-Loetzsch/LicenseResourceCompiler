using System;
using System.Collections.Generic;
using System.IO;

namespace Oleander.LicResComp.Tool.ExternalProcesses;

public class LicenseCompiler : ExternalProcess
{
    public LicenseCompiler(string target, IEnumerable<string> compList, IEnumerable<string> assemblies, string outDir) 
        : base( Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LC.exe"), $"/target:{target} /complist:{string.Join(" /complist:", compList)} /i:{string.Join(" /i:", assemblies)} /outdir:{outDir}")
    {
    }
}