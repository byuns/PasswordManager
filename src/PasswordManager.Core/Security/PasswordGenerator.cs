using System.Security.Cryptography;

namespace PasswordManager.Core.Security;

/// <summary>비밀번호 생성 옵션 (design 7.1). 기본값은 길이 16·전체 종류·혼동문자 제외.</summary>
public sealed record PasswordOptions(
    int Length = 16,
    bool IncludeUppercase = true,
    bool IncludeLowercase = true,
    bool IncludeDigits = true,
    bool IncludeSymbols = true,
    bool ExcludeAmbiguous = true);

/// <summary>
/// CSPRNG(<see cref="RandomNumberGenerator"/>)로 무작위 비밀번호를 만든다 (design 7.1).
/// 선택된 각 문자 종류에서 최소 1자를 보장하고 나머지는 전체 풀에서 뽑은 뒤 섞는다.
/// </summary>
public static class PasswordGenerator
{
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}:;,.?";
    private const string Ambiguous = "O0oIl1";

    public static string Generate(PasswordOptions options)
    {
        var classes = new List<string>();
        if (options.IncludeUppercase) classes.Add(Filter(Uppercase, options));
        if (options.IncludeLowercase) classes.Add(Filter(Lowercase, options));
        if (options.IncludeDigits) classes.Add(Filter(Digits, options));
        if (options.IncludeSymbols) classes.Add(Filter(Symbols, options));

        if (classes.Count == 0)
            throw new ArgumentException("최소 한 가지 문자 종류를 선택해야 합니다.", nameof(options));
        if (options.Length < classes.Count)
            throw new ArgumentException(
                $"길이({options.Length})가 선택한 문자 종류 수({classes.Count})보다 작아 각 종류를 포함할 수 없습니다.",
                nameof(options));

        var pool = string.Concat(classes);
        var chars = new char[options.Length];

        // 1) 각 선택 종류에서 최소 1자 확보.
        for (var i = 0; i < classes.Count; i++)
            chars[i] = Pick(classes[i]);

        // 2) 나머지는 전체 풀에서 채운다.
        for (var i = classes.Count; i < chars.Length; i++)
            chars[i] = Pick(pool);

        Shuffle(chars);
        return new string(chars);
    }

    private static string Filter(string set, PasswordOptions options)
    {
        if (!options.ExcludeAmbiguous)
            return set;
        return new string(set.Where(c => !Ambiguous.Contains(c)).ToArray());
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];

    /// <summary>Fisher–Yates 셔플(암호학적 난수)로 종류별 삽입 위치 편향을 제거한다.</summary>
    private static void Shuffle(char[] chars)
    {
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
    }
}
