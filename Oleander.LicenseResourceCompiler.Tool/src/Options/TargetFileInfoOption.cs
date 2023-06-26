using System;
using System.CommandLine;
using System.IO;

namespace Oleander.LicResComp.Tool.Options;

internal class TargetFileInfoOption : Option<FileInfo>
{
    public TargetFileInfoOption() : base(name: "--target", description: "Specifies the executable for which the .licenses file is being generated.")
    {
        this.AddAlias("-t");
        this.AddValidator(result =>
        {
            try
            {
                var fileInfo = result.GetValueOrDefault<FileInfo>();

                if (fileInfo == null)
                {
                    result.ErrorMessage = $"Parameter --target (-t) is missing!";
                    return;
                }

                if (!fileInfo.Exists) result.ErrorMessage = $"File does not exist: {fileInfo.FullName}";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }
        });

        this.AddCompletions(ctx => TabCompletions.FileCompletions(ctx.WordToComplete, "*.dll"));
        this.AddCompletions(ctx => TabCompletions.FileCompletions(ctx.WordToComplete, "*.exe"));
    }
}