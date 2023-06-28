using System;
using System.CommandLine;
using System.IO;
using System.Linq;

namespace Oleander.LicResComp.Tool.Options;

internal class CompListOption : Option<FileInfo[]>
{
    public CompListOption() : base(name: "--complist", description: "Specifies the name of a '*.licenses.txt' file that contains the list of licensed components.")
    {
        this.AddAlias("-c");
        this.AddValidator(result =>
        {
            try
            {
                var fileInfos = (result.GetValueOrDefault<FileInfo[]>() ?? Enumerable.Empty<FileInfo>()).ToList();

                foreach (var fileInfo in fileInfos)
                {
                    if (!fileInfo.FullName.Trim('\"').ToLower().EndsWith(".licenses.txt"))
                    {
                        result.ErrorMessage = $"File must have an '*.licenses.txt' extension: {fileInfo.FullName}";
                        return;
                    }

                    if (fileInfo.Exists) continue;
                    result.ErrorMessage = $"File does not exist: {fileInfo.FullName}";
                    return;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }
        });

        this.AddCompletions(ctx => TabCompletions.FileCompletions(ctx.WordToComplete, "*.licenses.txt"));
    }
}