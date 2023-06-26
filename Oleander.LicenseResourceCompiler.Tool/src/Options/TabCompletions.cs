using System;
using System.Collections.Generic;
using System.CommandLine.Completions;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Oleander.LicResComp.Tool.Options;

internal static class TabCompletions
{

    public static ILogger? Logger;

    public static IEnumerable<CompletionItem> FileCompletions(string wordToComplete, string? filePattern = null)
    {
        var fileItems = new List<CompletionItem>();
        var strResFiles = new List<string>();

        if (string.IsNullOrEmpty(wordToComplete)) return fileItems;

        var toReplace = new Dictionary<string, string>();
        var directorySeparatorChar = wordToComplete.Contains('\\') ? '\\' : '/';
        var wordToCompleteList = wordToComplete.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        var lastWordToComplete = wordToCompleteList.Last();

        if (wordToCompleteList.Count > 0)
        {
            wordToCompleteList.RemoveAt(wordToCompleteList.Count - 1);
        }

        if (wordToCompleteList.Count == 0 && wordToComplete.StartsWith(directorySeparatorChar))
        {
            wordToCompleteList.Add(lastWordToComplete);
            lastWordToComplete = string.Empty;
        }

        var currentDirectory = Directory.GetCurrentDirectory();

        Logger?.LogDebug("currentDirectory={currentDirectory}, filePattern={filePattern}", currentDirectory, filePattern);

        var pathTest = currentDirectory;
        var isRelativePath = true;

        if (wordToCompleteList.Count > 0)
        {
            isRelativePath = false;
            pathTest = string.Join(Path.DirectorySeparatorChar, wordToCompleteList);

            if (!new DirectoryInfo(pathTest).Exists)
            {
                var driveInfoNames = DriveInfo.GetDrives().Select(x => x.Name.ToLower()).ToList();
                var driveInfo = driveInfoNames.FirstOrDefault(x => x.StartsWith(wordToCompleteList[0].ToLower()));

                if (driveInfo == null) return fileItems;

                wordToCompleteList[0] = driveInfo;
                var newPathTest = Path.GetFullPath(string.Join(Path.DirectorySeparatorChar, wordToCompleteList));

                toReplace[newPathTest] = string.Concat(pathTest, directorySeparatorChar);
                pathTest = newPathTest;
            }
        }

        var dirInfoTest = new DirectoryInfo(pathTest);
        if (!dirInfoTest.Exists) return fileItems;

        isRelativePath = isRelativePath || pathTest != dirInfoTest.FullName;

        strResFiles.Add(string.Concat(dirInfoTest.FullName, directorySeparatorChar));

        if (!string.IsNullOrEmpty(filePattern))
        {
            var searchPattern = $"{lastWordToComplete}{filePattern}";

            if (lastWordToComplete.Contains("."))
            {
                var filePatternExtension = filePattern.Split('.').Last();
                var lastWordToCompleteExtension = lastWordToComplete.Split('.').Last();

                if (filePatternExtension.StartsWith(lastWordToCompleteExtension))
                {
                    // SR.str
                    // *.strings
                    
                    searchPattern = $"{lastWordToComplete
                        .Substring(0, lastWordToComplete.Length - lastWordToCompleteExtension.Length)}{filePattern
                        .Substring(filePattern.Length - filePatternExtension.Length)}";
                }

            }

            strResFiles.AddRange(dirInfoTest.GetFiles(searchPattern, SearchOption.TopDirectoryOnly).Select(x => x.FullName));
        }


        AddSingleDirectory(strResFiles, dirInfoTest.GetDirectories("*", SearchOption.TopDirectoryOnly)
            .Where(x => x.Name.StartsWith(lastWordToComplete) && x.Name != lastWordToComplete), directorySeparatorChar, filePattern);

        // Test directory with last word
        pathTest = Path.Combine(pathTest, lastWordToComplete);
        dirInfoTest = new DirectoryInfo(pathTest);

        if (dirInfoTest.Exists && dirInfoTest.FullName.EndsWith(lastWordToComplete))
        {
            strResFiles.Add(string.Concat(dirInfoTest.FullName, directorySeparatorChar));

            if (!string.IsNullOrEmpty(filePattern))
            {
                strResFiles.AddRange(dirInfoTest.GetFiles(filePattern,
                    SearchOption.TopDirectoryOnly).Select(x => x.FullName));
            }

            AddSingleDirectory(strResFiles, dirInfoTest.GetDirectories("*", SearchOption.TopDirectoryOnly), directorySeparatorChar, filePattern);
        }

        var firstChar = wordToComplete.StartsWith(directorySeparatorChar) ? directorySeparatorChar.ToString() : string.Empty;
        var pathToRemove = (isRelativePath ? currentDirectory : string.Empty)
            .Replace('\\', directorySeparatorChar)
            .Replace('/', directorySeparatorChar);

        strResFiles = strResFiles.Distinct().ToList();

        for (var i = 0; i < strResFiles.Count; i++)
        {
            var label = $"{firstChar}{strResFiles[i]}";

            foreach (var (key, value) in toReplace)
            {
                label = label.Replace(key, value);
            }

            label = label
                .Replace("\\\\", directorySeparatorChar.ToString())
                .Replace('\\', directorySeparatorChar)
                .Replace("//", directorySeparatorChar.ToString())
                .Replace('/', directorySeparatorChar);

            if (isRelativePath && label.StartsWith(pathToRemove))
            {
                label = label[pathToRemove.Length..];
            }

            var indexOf = label.IndexOf(wordToComplete, StringComparison.InvariantCulture);
            if (indexOf >= 0) label = label[indexOf..];
            if (!label.StartsWith(wordToComplete) || label == wordToComplete) continue;

            fileItems.Add(new(label: label, sortText: $"{i:000}"));
        }

        Logger?.LogDebug("Labels{Labels}", string.Join(Environment.NewLine, fileItems.Select(x => x.Label)));
        return fileItems;
    }


    private static void AddSingleDirectory(List<string> strResFiles, IEnumerable<DirectoryInfo> dirInfos, char directorySeparatorChar, string? filePattern)
    {
        var dirInfoList = dirInfos.ToList();

        foreach (var dirInfo in dirInfoList)
        {
            try
            {
                var subDirs = dirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly);

                if (subDirs.Any())
                {
                    strResFiles.Add(string.Concat(dirInfo.FullName, directorySeparatorChar));
                }

                if (!string.IsNullOrEmpty(filePattern))
                {
                    strResFiles.AddRange(dirInfo.GetFiles(filePattern, SearchOption.TopDirectoryOnly)
                        .Select(fileInfo => fileInfo.FullName));
                }

                if (dirInfoList.Count == 1)
                {
                    AddSingleDirectory(strResFiles, subDirs, directorySeparatorChar, filePattern);
                }
            }
            catch (UnauthorizedAccessException)
            {

            }
            catch(Exception ex)
            {
                Logger?.LogDebug("Exc={exception}", ex);
            }
        }
    }
}