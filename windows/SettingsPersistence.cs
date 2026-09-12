using System.IO;
using System.Text.Json;

namespace AppleMusicDesktopLyrics;

/// <summary>Atomic same-directory writes and validated last-good recovery.</summary>
internal static class SettingsPersistence
{
    private static readonly object Gate = new();
    internal static string Status { get; private set; } = "";

    private static T Read<T>(string path, Func<T, bool>? validate) where T : class
    {
        var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        if (value is null || (validate is not null && !validate(value)))
            throw new JsonException("设置内容不完整或无效");
        return value;
    }

    internal static T Load<T>(string path, Func<T> defaults, Func<T, bool>? validate = null) where T : class
    {
        lock (Gate)
        {
            try { return Read(path, validate); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                try
                {
                    var recovered = Read(path + ".bak", validate);
                    Status = $"{Path.GetFileName(path)}：已从备份恢复设置。";
                    return recovered;
                }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
                {
                    if (File.Exists(path) || File.Exists(path + ".bak") ||
                        ex is not (FileNotFoundException or DirectoryNotFoundException))
                        Status = $"{Path.GetFileName(path)}：设置无法读取，暂用默认值；原文件保留。";
                    return defaults();
                }
            }
        }
    }

    internal static void Save<T>(string path, T value, Func<T, bool>? validate = null) where T : class
    {
        lock (Gate)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (validate is not null && !validate(value)) throw new JsonException("设置内容不完整或无效");
                var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(path))
                {
                    var validPrevious = false;
                    try { Read(path, validate); validPrevious = true; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
                    if (!validPrevious)
                        File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                    // Never replace a good backup with a corrupt primary file.
                    File.Replace(temporary, path, validPrevious ? path + ".bak" : null);
                }
                else File.Move(temporary, path);
                if (Status.StartsWith($"{Path.GetFileName(path)}：保存失败", StringComparison.Ordinal))
                    Status = $"{Path.GetFileName(path)}：设置已成功保存。";
            }
            catch (Exception ex)
            {
                Status = $"{Path.GetFileName(path)}：保存失败，原文件未替换。{ex.Message}";
                throw;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
