using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oleander.Extensions.Configuration;
using Oleander.Extensions.Logging;
using Oleander.Extensions.Logging.Abstractions;
using Oleander.Extensions.Logging.Providers;
using Oleander.LicResComp.Tool.Options;
using System.Linq;
using Oleander.LicResComp.Tool.ExternalProcesses;
using System.Collections;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;

namespace Oleander.LicResComp.Tool;

internal class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddJsonFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appConfiguration.json"), true, true);

        var serviceProvider = builder.Services.BuildServiceProvider();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        builder.Services
            .AddSingleton((IConfigurationRoot)configuration)
            .Configure<ConfiguredTypes>(configuration.GetSection("types"))
            .TryAddSingleton(typeof(IConfiguredTypesOptionsMonitor<>), typeof(ConfiguredTypesOptionsMonitor<>));

        builder.Logging
            .ClearProviders()
            .AddConfiguration(builder.Configuration.GetSection("Logging"))
            .Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, LoggerSinkProvider>());

        var host = builder.Build();

        host.Services.InitLoggerFactory();

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
        var console = new ToolConsole(logger);

        var rootCommand = new RootCommand("License resource compiler tool");
        var commandLine = new CommandLineBuilder(rootCommand)
            .UseDefaults() // automatically configures dotnet-suggest
            .Build();

        TabCompletions.Logger = logger;

        var compListOption = new CompListOption().ExistingOnly();
        var targetOption = new TargetOption().ExistingOnly();

        rootCommand.AddOption(compListOption);
        rootCommand.AddOption(targetOption);
        rootCommand.SetHandler((compList, targetFile) =>
                Task.FromResult(CreateLicense(logger, compList, targetFile)),
                compListOption, targetOption);

        var exitCode = await commandLine.InvokeAsync(args, console);

        console.Flush();

        const string logMsg = "LicResComp '{args}' exit with exit code {exitCode}";

        var arguments = string.Join(" ", args);

        if (exitCode == 0)
        {
            logger.LogInformation(logMsg, arguments, exitCode);

            if (!arguments.StartsWith("[suggest:"))
            {
                MSBuildLogFormatter.CreateMSBuildMessage("LRC0", $"LicResComp {exitCode}", "Main");
            }
        }
        else
        {
            logger.LogError(logMsg, arguments, exitCode);
        }

        await host.WaitForLoggingAsync(TimeSpan.FromSeconds(5));
        return exitCode;
    }

    private static int CreateLicense(ILogger logger, IEnumerable<FileInfo> compList, FileInfo target)
    {
        var loadedAssemblies = new List<Assembly>();
        var assemblyLoadEventHandler = new AssemblyLoadEventHandler((_, args) => { loadedAssemblies.Add(args.LoadedAssembly); });
        var resolveEventHandler = new ResolveEventHandler((_, args) => OnAssemblyResolve(logger, args, target.Directory));
        var typeCache = new Dictionary<string, Type>();
        var targetFileExtension = target.FullName.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase) ? ".exe" : ".dll";
        var projectFileInfos = new List<FileInfo>();

        AppDomain.CurrentDomain.AssemblyLoad += assemblyLoadEventHandler;
        AppDomain.CurrentDomain.AssemblyResolve += resolveEventHandler;

        foreach (var compFile in compList)
        {
            var outDir = compFile.Directory;

            if (VSProject.TryFindProjectFile(compFile.Directory, out var projectFileInfo))
            {
                outDir = projectFileInfo!.Directory;

                if (!projectFileInfos.Any(x => string.Equals(x.FullName, projectFileInfo.FullName, StringComparison.InvariantCultureIgnoreCase)))
                {
                    projectFileInfos.Add(projectFileInfo);
                }
            }

            if (outDir == null) continue;

            foreach (var line in File.ReadAllLines(compFile.FullName).Select(x => x.Trim()))
            {
                if (line.Length <= 0 || line.StartsWith("#") || typeCache.ContainsKey(line)) continue;

                var type = Type.GetType(line);

                if (type == null)
                {
                    logger.CreateMSBuildError("LRC2", $"Unable to resolve type '{line}'", "Oleander.LicResComp.Tool");
                    return -1;
                }

                typeCache[line] = type;
                logger.LogInformation("Resolved type '{type}'", type);
            }

            //var lcTarget = compFile.Name.Replace(".licenses.txt", targetFileExtension);
            var lcTarget = target.Name;
            var lcCompList = new[] { compFile.FullName };
            var assemblies = loadedAssemblies.Where(x => typeCache.Values.Any(x1 => x1.Assembly.FullName == x.FullName)).Select(x => x.Location);
            var licensesFilePath = Path.Combine(outDir!.FullName, $"{lcTarget}.licenses");


            try
            {
                var lcResult = new LicenseCompiler(lcTarget, lcCompList, assemblies, outDir!.FullName).Start();

                if (lcResult.ExitCode != 0)
                {
                    logger.CreateMSBuildError("LRC4", lcResult.Win32ExitCode == Win32ExitCodes.ERROR_SUCCESS ?
                        $"Start external process LC.exe failed! ERROR{lcResult.ExitCode} {lcResult.StandardErrorOutput}" :
                        $"Start external process LC.exe failed! {lcResult.Win32ExitCode} {lcResult.StandardErrorOutput}", "Oleander.LicResComp.Tool");

                    return -1;
                }

                if (lcResult.StandardOutput == null || !lcResult.StandardOutput.Contains(licensesFilePath, StringComparison.InvariantCultureIgnoreCase))
                {
                    logger.CreateMSBuildError("LRC5", $"No licenses file was created!  {lcResult}", "Oleander.LicResComp.Tool");

                    return -1;
                }

                logger.LogInformation("LC {lcResult}", lcResult);

                if (!File.Exists(licensesFilePath))
                {
                    logger.CreateMSBuildError("LRC6", $"Licenses file not found! '{licensesFilePath}'", "Oleander.LicResComp.Tool");
                    return -1;
                }
               
                var licensesFilePath1 = Path.Combine(outDir!.FullName, $"{compFile.Name.Replace(".licenses.txt", targetFileExtension)}.licenses");
                File.Move(licensesFilePath, licensesFilePath1, true);
            }
            catch (Exception ex)
            {
                logger.CreateMSBuildError("LRC7", $"Start external process LC.exe failed! {ex}", "Oleander.LicResComp.Tool");
                return -1;
            }
        }

        AppDomain.CurrentDomain.AssemblyLoad += assemblyLoadEventHandler;
        AppDomain.CurrentDomain.AssemblyResolve += resolveEventHandler;

        foreach (var projectFileInfo in projectFileInfos)
        {
            if (projectFileInfo.Directory == null) continue;
            var result = MergeLicensesFiles(logger, projectFileInfo.Directory, targetFileExtension);
            if (result != 0) return result;

            result = AddItemToProject(logger, projectFileInfo, targetFileExtension);
            if (result != 0) return result;
        }

        return 0;
    }

    private static Assembly? OnAssemblyResolve(ILogger logger, ResolveEventArgs e, DirectoryInfo? targetDir)
    {
        if (targetDir is not { Exists: true }) return null;

        var splitAssemblyToResolve = e.Name.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var assemblyNameToResolve = splitAssemblyToResolve.First();
        var assemblyVersionToResolve = splitAssemblyToResolve.FirstOrDefault(x => x.StartsWith("Version="));
        var assemblyCultureToResolve = splitAssemblyToResolve.FirstOrDefault(x => x.StartsWith("Culture="));
        var assemblyPublicKeyTokenToResolve = splitAssemblyToResolve.FirstOrDefault(x => x.StartsWith("PublicKeyToken="));

        if (assemblyCultureToResolve == "Culture=neutral") assemblyCultureToResolve = null;
        if (assemblyPublicKeyTokenToResolve == "PublicKeyToken=null") assemblyPublicKeyTokenToResolve = null;

        foreach (var fileExt in new[] { ".dll", ".exe" })
        {
            foreach (var fileInfo in targetDir.GetFiles($"{assemblyNameToResolve}{fileExt}", SearchOption.AllDirectories))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(fileInfo.FullName);
                    if (assembly.FullName == null) continue;

                    var splitLoadedAssembly = assembly.FullName.Split(", ", StringSplitOptions.RemoveEmptyEntries);
                    var loadedAssemblyCulture = splitLoadedAssembly.FirstOrDefault(x => x.StartsWith("Culture="));
                    var loadedAssemblyPublicKeyToken = splitLoadedAssembly.FirstOrDefault(x => x.StartsWith("PublicKeyToken="));
                    var loadedAssemblyVersion = splitLoadedAssembly.FirstOrDefault(x => x.StartsWith("Version="));

                    assemblyVersionToResolve ??= loadedAssemblyVersion;
                    assemblyCultureToResolve ??= loadedAssemblyCulture;
                    assemblyPublicKeyTokenToResolve ??= loadedAssemblyPublicKeyToken;

                    var assemblyFullName = $"{assemblyNameToResolve}, {assemblyVersionToResolve}, {assemblyCultureToResolve}, {assemblyPublicKeyTokenToResolve}";
                    if (assembly.FullName == assemblyFullName) return assembly;

                }
                catch (Exception ex)
                {
                    logger.CreateMSBuildWarning("LRC4", $"Exception occurred creating type '{assemblyNameToResolve}'! {ex}", "Oleander.LicResComp.Tool");
                }
            }
        }

        return null;
    }


    private static int MergeLicensesFiles(ILogger logger, DirectoryInfo projectDirInfo, string targetFileExtension)
    {
        string? cryptoKey = null;
        var hashtable = new Hashtable();
        IFormatter binaryFormatter = new BinaryFormatter();

        var files = projectDirInfo.GetFiles($"*{targetFileExtension}.licenses", SearchOption.TopDirectoryOnly );
        logger.LogInformation("Merge files from directory: {projectDir}", projectDirInfo.FullName);

        if (!files.Any()) return 0;

        foreach (var file in from file in files
                             let f = file.Name
                             where f != null && f.ToUpper() != "DLL.LICENSES" && f.ToUpper() != "EXE.LICENSES"
                             select file)
        {
            logger.LogInformation("Processing file: {file}", file);

            using var stream = file.OpenRead(); 
#pragma warning disable SYSLIB0011
            if (binaryFormatter.Deserialize(stream) is not object[] { Length: 2 } objects)
#pragma warning restore SYSLIB0011
            {
                logger.LogInformation("The file '{file}' is not a valid license file!", file);
                continue;
            }

            var cryptoKeyTemp = (string)objects[0];

            if (!string.IsNullOrWhiteSpace(cryptoKey) && cryptoKey != cryptoKeyTemp)
            {
                logger.CreateMSBuildError("LRC8", $"Crypto key is different! {cryptoKey} - {cryptoKeyTemp}", "Oleander.LicResComp.Tool");
                return -2;
            }

            cryptoKey = cryptoKeyTemp;

            var tempTable = (Hashtable)objects[1];

            foreach (var dictEntry in tempTable.Cast<DictionaryEntry>()
                         .Where(dictEntry => !hashtable.ContainsKey(dictEntry.Key)))
            {
                hashtable.Add(dictEntry.Key, dictEntry.Value);
            }
        }

        if (string.IsNullOrWhiteSpace(cryptoKey))
        {
            logger.CreateMSBuildError("LRC9", $"Crypto key is null!", "Oleander.LicResComp.Tool");
            return -2;
        }

        if (targetFileExtension.StartsWith(".")) targetFileExtension = targetFileExtension[1..];

        logger.LogInformation("Create {targetFileExtension}.licenses", targetFileExtension);

        var licensesFile = Path.Combine(projectDirInfo.FullName, $"{targetFileExtension}.licenses");

        if (File.Exists(licensesFile))
        {
            logger.LogInformation("Remove FileAttributes.ReadOnly from file {licensesFile}.", licensesFile);
            var attributes = File.GetAttributes(licensesFile);
            attributes = attributes & ~FileAttributes.ReadOnly;
            File.SetAttributes(licensesFile, attributes);
        }

        using (var stream = File.Create(licensesFile))
        {
            var objArray = new object[] { cryptoKey, hashtable };
#pragma warning disable SYSLIB0011
            binaryFormatter.Serialize(stream, objArray);
#pragma warning restore SYSLIB0011
        }

        return 0;
    }

    private static int AddItemToProject(ILogger logger, FileInfo projectFileInfo, string targetFileExtension)
    {
        var vsProject = new VSProject(projectFileInfo.FullName);
        var itemGroup = vsProject.FindOrCreateProjectItemGroupElement("None", $"{targetFileExtension}.licenses");

        foreach (var file in projectFileInfo.Directory!.GetFiles($"*{targetFileExtension}.licenses"))
        {
            vsProject.UpdateOrCreateItemElement(itemGroup, "None", file.FullName);
            logger.LogInformation("Add licenses files '{file}' to project '{projectFile}'.", file.FullName, projectFileInfo.FullName);
        }

        if (targetFileExtension.StartsWith(".")) targetFileExtension = targetFileExtension[1..];

        var embeddedResourceFileName = Path.Combine(projectFileInfo.Directory.FullName, $"{targetFileExtension}.licenses");
        if (!File.Exists(embeddedResourceFileName)) return 0;

        vsProject.UpdateOrCreateItemElement(itemGroup, "EmbeddedResource", embeddedResourceFileName);
        logger.LogInformation("Add licenses files '{file}' as 'EmbeddedResource' to project '{projectFile}'.", embeddedResourceFileName, projectFileInfo.FullName);

        vsProject.SaveChanges();
        return 0;
    }
}