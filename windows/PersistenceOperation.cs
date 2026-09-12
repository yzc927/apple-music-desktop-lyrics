using System.IO;
using System.Text.Json;

namespace AppleMusicDesktopLyrics;

internal static class PersistenceOperation
{
    internal static bool Try(Action action, out string error)
    {
        try { action(); error = ""; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            error = "未能保存更改，输入内容已保留。请检查文件是否被占用、写入权限和磁盘空间后重试。\n" + ex.Message;
            return false;
        }
    }
}
