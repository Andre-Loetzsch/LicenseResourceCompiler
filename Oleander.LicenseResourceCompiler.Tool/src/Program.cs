using System;
using System.Collections;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.ComponentModel.Design;
using System.ComponentModel;
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
using System.Globalization;
using System.Numerics;

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




        rootCommand.SetHandler();

        






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

    private int CreateLicense(ILogger logger,  IEnumerable<FileInfo> compList, IEnumerable<DirectoryInfo> moduleDirectories, FileInfo target)
    {
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

        var typeDictionary = new Dictionary<string, Type>();

        foreach (var compFile in compList)
        {
            var designTimeLicenseContext = new DesigntimeLicenseContext();

            foreach (var line in File.ReadAllLines(compFile.FullName))
            {
                if (line.Length <= 0 || line.StartsWith("#") || typeDictionary.ContainsKey(line)) continue;

                var type = Type.GetType(line);

                if (type == null)
                {
                    logger.CreateMSBuildError("LRC2", $"Unable to resolve type '{line}'", "Oleander.LicResComp.Tool");
                    return -1;
                }

                typeDictionary[line] = type;
                logger.LogInformation("Resolved type '{type}'", type);

                try
                {
                    LicenseManager.CreateWithContext(type, designTimeLicenseContext);
                }
                catch (Exception ex)
                {
                    logger.CreateMSBuildError("LRC3", $"Exception occurred creating type '{type}'! {ex}", "Oleander.LicResComp.Tool");
                    return -1;
                }
            }

            var path = compFile.FullName.Replace(".txt", ".licenses");
            logger.LogInformation("Creating Licenses file '{path}'.", path);

            using var stream = File.Create(path);
            DesigntimeLicenseContextSerializer.Serialize(stream, target.FullName.ToUpper(CultureInfo.InvariantCulture), designTimeLicenseContext);
        }

        AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;

        return 0;
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs e)
    {
        return null;
    }
}