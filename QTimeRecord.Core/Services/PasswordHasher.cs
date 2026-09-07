using System.Security.Cryptography;

namespace QTimeRecord.Core.Services;

public interface IPasswordHasher
{
    HashedPassword Hash(string pin);

    bool Verify(string pin, string hash, string salt, int iterations);
}

public sealed record HashedPassword(string Hash, string Salt, int Iterations);

/// <summary>
/// 管理者PIN のハッシュ化。PBKDF2-HMAC-SHA256。
///
/// bcrypt / Argon2 のほうが強いが外部パッケージが要る。
/// .NET 標準だけで完結させ、反復回数で強度を確保する。
///
/// <b>この方式は「端末を触れる人の誤操作を防ぐ」もので、盗まれた DB を守るものではない。</b>
/// 8桁数字は1億通りしかなく、ハッシュが流出すればオフラインでの総当たりは現実的に可能。
/// 総当たりへの実質的な備えは、認証失敗5回でのロック（→ plan.md Q10）と端末の物理管理。
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>反復回数。将来引き上げても、既存のハッシュは行に保存した値で検証できる。</summary>
    public const int DefaultIterations = 210_000;

    private const int SaltBytes = 16;
    private const int KeyBytes = 32;

    public HashedPassword Hash(string pin)
    {
        ArgumentException.ThrowIfNullOrEmpty(pin);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Derive(pin, salt, DefaultIterations);

        return new HashedPassword(
            Convert.ToBase64String(key),
            Convert.ToBase64String(salt),
            DefaultIterations);
    }

    public bool Verify(string pin, string hash, string salt, int iterations)
    {
        if (string.IsNullOrEmpty(pin) || string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
        {
            return false;
        }

        byte[] expected;
        byte[] saltBytes;

        try
        {
            expected = Convert.FromBase64String(hash);
            saltBytes = Convert.FromBase64String(salt);
        }
        catch (FormatException)
        {
            // DB の値が壊れている。認証を通さずに失敗として扱う。
            return false;
        }

        var actual = Derive(pin, saltBytes, iterations);

        // 先頭から順に比較すると、一致した桁数が処理時間に出てしまう。
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string pin, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, KeyBytes);
}
