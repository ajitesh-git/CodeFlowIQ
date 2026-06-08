using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace CodeFlowIQ.Api;

public sealed record LocalRuntimeHostOptions(
    string ListenUrl,
    string RuntimeFilePath,
    bool WriteRuntimeFile);

public sealed record LocalRuntimeMetadata(
    string BaseUrl,
    int ProcessId,
    DateTimeOffset StartedAt,
    string Version);

public enum LocalRuntimePlatform
{
    Windows,
    MacOS,
    Linux
}

public static class LocalRuntimeHost
{
    private const string DefaultListenUrl = "http://127.0.0.1:0";

    public static LocalRuntimeHostOptions CreateOptions(IConfiguration configuration)
    {
        var listenUrl = FirstNonEmpty(
            configuration["CodeFlowIQ:ApiUrls"],
            Environment.GetEnvironmentVariable("CODEFLOWIQ_API_URLS"),
            configuration["urls"],
            DefaultListenUrl);

        var runtimeDirectory = FirstNonEmpty(
            configuration["CodeFlowIQ:RuntimeDirectory"],
            Environment.GetEnvironmentVariable("CODEFLOWIQ_RUNTIME_DIR"),
            GetDefaultRuntimeDirectory());

        var writeRuntimeFile = !bool.TryParse(configuration["CodeFlowIQ:DisableRuntimeFile"], out var disabled) || !disabled;

        return new LocalRuntimeHostOptions(
            listenUrl,
            Path.Combine(runtimeDirectory, "api.json"),
            writeRuntimeFile);
    }

    public static LocalRuntimeMetadata CreateMetadata(string baseUrl) =>
        new(
            baseUrl,
            Environment.ProcessId,
            DateTimeOffset.UtcNow,
            typeof(LocalRuntimeHost).Assembly.GetName().Version?.ToString() ?? "0.0.0");

    public static void TryWriteRuntimeFile(WebApplication app, LocalRuntimeHostOptions options)
    {
        if (!options.WriteRuntimeFile)
        {
            return;
        }

        var baseUrl = app.Services.GetService<IServer>()?
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = app.Urls.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return;
        }

        WriteRuntimeFile(options.RuntimeFilePath, CreateMetadata(baseUrl));
    }

    public static void WriteRuntimeFile(string runtimeFilePath, LocalRuntimeMetadata metadata)
    {
        var directory = Path.GetDirectoryName(runtimeFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        File.WriteAllText(runtimeFilePath, json);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? DefaultListenUrl;

    public static string GetDefaultRuntimeDirectory() =>
        GetDefaultRuntimeDirectory(DetectPlatform(), GetUserProfileDirectory());

    public static string GetDefaultRuntimeDirectory(LocalRuntimePlatform platform, string userProfileDirectory) =>
        platform switch
        {
            LocalRuntimePlatform.Windows => Path.Combine(GetWindowsLocalAppDataDirectory(userProfileDirectory), "CodeFlowIQ", "runtime"),
            LocalRuntimePlatform.MacOS => Path.Combine(userProfileDirectory, "Library", "Application Support", "CodeFlowIQ", "runtime"),
            _ => Path.Combine(userProfileDirectory, ".local", "share", "CodeFlowIQ", "runtime")
        };

    private static LocalRuntimePlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return LocalRuntimePlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return LocalRuntimePlatform.MacOS;
        }

        return LocalRuntimePlatform.Linux;
    }

    private static string GetUserProfileDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? Path.Combine(Path.GetTempPath(), "CodeFlowIQ")
            : userProfile;
    }

    private static string GetWindowsLocalAppDataDirectory(string fallbackUserProfileDirectory)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return localAppData;
        }

        return Path.Combine(fallbackUserProfileDirectory, "AppData", "Local");
    }
}
