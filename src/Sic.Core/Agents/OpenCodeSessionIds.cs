using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Sic.Core.Agents;

/// <summary>
/// OpenCode session IDs, as OpenCode 2.0 makes them: "ses_", twelve hex digits, then fourteen random letters and digits. The
/// hex digits are the creation time in milliseconds times 4096 plus a per-millisecond counter, bitwise inverted and cut to
/// 48 bits, so a newer session sorts first. OpenCode creates a session under any ID beginning "ses" that it is given; SIC
/// makes its IDs the way OpenCode does, so its sessions sort among OpenCode's own.
/// </summary>
public static class OpenCodeSessionIds
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static bool IsValid(string id) => Regex.IsMatch(id, @"\Ases_[0-9a-f]{12}[0-9A-Za-z]{14}\z");

    public static string New(DateTimeOffset now) =>
        "ses_" + (~checked(now.ToUnixTimeMilliseconds() * 4096) & 0xFFFF_FFFF_FFFFL).ToString("x12") + RandomNumberGenerator.GetString(Alphabet, 14);
}
