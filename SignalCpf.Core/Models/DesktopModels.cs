namespace SignalCpf.Core.Models;

public sealed record ContactInfo(
    string ServiceId,
    string? Number,
    string? ProfileName,
    string? About);

public sealed record ClientSettings(
    string ApiBaseUrl,
    string DataDirectory,
    string DeviceName,
    bool AllowInsecureTls,
    bool EnablePqKeys,
    bool NotificationsEnabled,
    bool ReadReceiptsEnabled,
    bool UsesNativeLibSignal,
    string ServerProfile = "SelfHosted",
    string? CdnUrl = null,
    string? StorageUrl = null);

public sealed record AttachmentInfo(
    string Id,
    string MessageId,
    string? FileName,
    string? ContentType,
    long Size,
    string? LocalPath);
