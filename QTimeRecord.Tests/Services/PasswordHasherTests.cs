using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.Services;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void 正しいPINで検証が通る()
    {
        var hashed = _hasher.Hash("12345678");

        Assert.True(_hasher.Verify("12345678", hashed.Hash, hashed.Salt, hashed.Iterations));
    }

    [Fact]
    public void 誤ったPINでは検証が通らない()
    {
        var hashed = _hasher.Hash("12345678");

        Assert.False(_hasher.Verify("12345679", hashed.Hash, hashed.Salt, hashed.Iterations));
        Assert.False(_hasher.Verify("1234567", hashed.Hash, hashed.Salt, hashed.Iterations));
        Assert.False(_hasher.Verify(string.Empty, hashed.Hash, hashed.Salt, hashed.Iterations));
    }

    [Fact]
    public void 同じPINでもソルトが毎回変わる()
    {
        var first = _hasher.Hash("12345678");
        var second = _hasher.Hash("12345678");

        // ソルトが固定だと、同じPINの店舗が同じハッシュになり総当たりを共有されてしまう。
        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);

        Assert.True(_hasher.Verify("12345678", first.Hash, first.Salt, first.Iterations));
        Assert.True(_hasher.Verify("12345678", second.Hash, second.Salt, second.Iterations));
    }

    [Fact]
    public void 反復回数が保存され検証時に使われる()
    {
        var hashed = _hasher.Hash("12345678");

        Assert.Equal(PasswordHasher.DefaultIterations, hashed.Iterations);

        // 反復回数が違えば別の鍵になる。行ごとに保存していないと、
        // 将来この値を引き上げた時点で既存のPINが通らなくなる。
        Assert.False(_hasher.Verify("12345678", hashed.Hash, hashed.Salt, hashed.Iterations - 1));
    }

    [Fact]
    public void 壊れた保存値では例外にせず失敗させる()
    {
        // DB の値が壊れていても、認証を通さず落ちもしないこと。
        Assert.False(_hasher.Verify("12345678", "not-base64!", "also-not-base64!", 1000));
    }
}
