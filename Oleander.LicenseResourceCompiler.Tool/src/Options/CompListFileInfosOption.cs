using System;
using System.CommandLine;
using System.IO;
using System.Linq;

namespace Oleander.LicResComp.Tool.Options;

internal class CompListFileInfosOption : Option<FileInfo[]>
{
    public CompListFileInfosOption() : base(name: "--complist", description: "Specifies the name of a '*.licenses.txt' file that contains the list of licensed components.")
    {
        this.AddAlias("-c");
        this.AddValidator(result =>
        {
            try
            {
                var fileInfos = (result.GetValueOrDefault<FileInfo[]>() ?? Enumerable.Empty<FileInfo>()).ToList();

                foreach (var fileInfo in fileInfos)
                {
                    if (!string.Equals(Path.GetExtension(fileInfo.FullName).Trim('\"'), ".licenses.txt"))
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