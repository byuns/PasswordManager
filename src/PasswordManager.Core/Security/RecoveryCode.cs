using System.Text;

namespace PasswordManager.Core.Security;

/// <summary>
/// 복구 키(바이트)를 사람이 옮겨 적기 쉬운 문자열로 인코딩/디코딩한다 (design 5.7, TD-010).
/// Crockford Base32를 사용해 헷갈리는 문자(I·L·O·U)를 배제하고, 4자씩 하이픈으로 묶어 표시한다.
/// 디코딩은 대소문자·구분자(하이픈/공백)를 무시하고 O→0·I/L→1 별칭을 허용한다.
/// </summary>
public static class RecoveryCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int GroupSize = 4;

    /// <summary>바이트를 Crockford Base32로 인코딩하고 4자씩 하이픈으로 묶는다.</summary>
    public static string Encode(byte[] data)
    {
        var symbols = new StringBuilder();
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                symbols.Append(Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }
        if (bitsLeft > 0)
            symbols.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);

        var grouped = new StringBuilder(symbols.Length + symbols.Length / GroupSize);
        for (var i = 0; i < symbols.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
                grouped.Append('-');
            grouped.Append(symbols[i]);
        }
        return grouped.ToString();
    }

    /// <summary>인코딩된 문자열을 바이트로 되돌린다. 구분자·대소문자·별칭을 허용한다.</summary>
    public static byte[] Decode(string code)
    {
        var bytes = new List<byte>();
        int buffer = 0, bitsLeft = 0;
        foreach (var raw in code)
        {
            if (raw is '-' or ' ')
                continue;

            var value = SymbolValue(raw);
            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                bytes.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }
        return bytes.ToArray();
    }

    private static int SymbolValue(char c)
    {
        var upper = char.ToUpperInvariant(c);
        upper = upper switch
        {
            'O' => '0',
            'I' or 'L' => '1',
            _ => upper,
        };
        var index = Alphabet.IndexOf(upper);
        if (index < 0)
            throw new FormatException($"복구 키에 사용할 수 없는 문자입니다: '{c}'");
        return index;
    }
}
