using System;
using System.CommandLine;
using System.IO;
using System.Linq;

namespace Oleander.LicResComp.Tool.Options;

internal class ModuleDirectoryInfosOption : Option<DirectoryInfo[]>
{
    public ModuleDirectoryInfosOption() : base(name: "--module", description: "Specifies the path to the modules that contain the components listed in the --complist file.")
    {
        this.AddAlias("-m");
        this.AddValidator(result =>
        {
            try
            {
                var dirInfos = (result.GetValueOrDefault<DirectoryInfo[]>() ?? Enumerable.Empty<DirectoryInfo>()).ToList();

                foreach (var dirInfo in dirInfos.Where(dirInfo => !dirInfo.Exists))
                {
                    result.ErrorMessage = $"Directory does not exist: {dirInfo.FullName}";
                    return;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }
        });
    }
}