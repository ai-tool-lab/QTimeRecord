using System.Collections.Frozen;

namespace QTimeRecord.Core.Domain;

/// <summary>
/// 列挙型と DB 保存値（<c>CLOCK_IN</c> のような大文字スネークケース）を相互変換する。
///
/// 数値ではなく文字列で保存するのは、DB を直接開いたときに読めるようにするため。
/// 対応表を手で書かず C# の名前から機械的に導くのは、書き間違いを構造的に防ぐため。
/// </summary>
public static class EnumDbValue
{
    private static class Cache<TEnum> where TEnum : struct, Enum
    {
        public static readonly FrozenDictionary<TEnum, string> ToDb =
            Enum.GetValues<TEnum>().ToFrozenDictionary(v => v, ToDbValueCore);

        public static readonly FrozenDictionary<string, TEnum> FromDb =
            Enum.GetValues<TEnum>().ToFrozenDictionary(ToDbValueCore, v => v);

        private static string ToDbValueCore(TEnum value)
            => NamingConventions.ToSnakeCase(value.ToString()!).ToUpperInvariant();
    }

    /// <summary>DB に保存する文字列を返す。<c>ClockIn</c> → <c>CLOCK_IN</c>。</summary>
    public static string ToDbValue<TEnum>(TEnum value) where TEnum : struct, Enum
        => Cache<TEnum>.ToDb.TryGetValue(value, out var s)
            ? s
            : throw new ArgumentOutOfRangeException(
                nameof(value), value, $"{typeof(TEnum).Name} に定義されていない値です。");

    /// <summary>DB の文字列から列挙値へ戻す。</summary>
    public static TEnum Parse<TEnum>(string dbValue) where TEnum : struct, Enum
        => Cache<TEnum>.FromDb.TryGetValue(dbValue, out var v)
            ? v
            : throw new ArgumentOutOfRangeException(
                nameof(dbValue), dbValue, $"{typeof(TEnum).Name} として解釈できない値です。");
}
