using System.Security.Cryptography;
using PasswordManager.Core.Security;

namespace PasswordManager.Core.Tests.Security;

public class RecoveryCodeTests
{
    [Fact]
    public void Encode_groups_into_blocks_of_four_with_hyphens()
    {
        var data = new byte[VaultServiceRecoveryKeySize];

        var code = RecoveryCode.Encode(data);

        var groups = code.Split('-');
        Assert.Equal(13, groups.Length);              // 32바이트 → 52자 → 4자 13그룹
        Assert.All(groups, g => Assert.Equal(4, g.Length));
    }

    [Fact]
    public void Encode_uses_only_crockford_alphabet()
    {
        var data = RandomNumberGenerator.GetBytes(VaultServiceRecoveryKeySize);

        var code = RecoveryCode.Encode(data).Replace("-", "");

        const string crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        Assert.All(code, c => Assert.Contains(c, crockford));
        Assert.DoesNotContain('I', code);
        Assert.DoesNotContain('L', code);
        Assert.DoesNotContain('O', code);
        Assert.DoesNotContain('U', code);
    }

    [Fact]
    public void Decode_roundtrips_encoded_value()
    {
        var data = RandomNumberGenerator.GetBytes(VaultServiceRecoveryKeySize);

        var roundtripped = RecoveryCode.Decode(RecoveryCode.Encode(data));

        Assert.Equal(data, roundtripped);
    }

    [Fact]
    public void Decode_is_case_insensitive_and_ignores_separators()
    {
        var data = RandomNumberGenerator.GetBytes(VaultServiceRecoveryKeySize);
        var code = RecoveryCode.Encode(data);

        var messy = code.ToLowerInvariant().Replace("-", " ");
        Assert.Equal(data, RecoveryCode.Decode(messy));
    }

    [Fact]
    public void Decode_maps_ambiguous_characters()
    {
        // Crockford 별칭: O→0, I/L→1. 사용자가 헷갈려 입력해도 같은 값으로 해석.
        var canonical = RecoveryCode.Decode("00001111");
        var withAliases = RecoveryCode.Decode("OOOOILIL");

        Assert.Equal(canonical, withAliases);
    }

    [Fact]
    public void Decode_rejects_invalid_characters()
    {
        Assert.Throws<FormatException>(() => RecoveryCode.Decode("!!!!"));
    }

    // VaultService.RecoveryKeySizeBytes와 동일(32). 여기 상수로 두어 참조 결합을 피함.
    private const int VaultServiceRecoveryKeySize = 32;
}
