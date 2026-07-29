using PasswordManager.Core.Security;

namespace PasswordManager.Core.Tests.Security;

public class KeyDerivationTests
{
    // 테스트 속도를 위한 가벼운 파라미터(결정성 검증엔 값 자체가 중요치 않음).
    private static readonly KdfParams Light = new(MemoryKiB: 8192, Iterations: 2, Parallelism: 1);

    [Fact]
    public void DeriveKey_is_deterministic_for_same_inputs()
    {
        var salt = KeyDerivation.NewSalt();

        var a = KeyDerivation.DeriveKey("correct horse battery staple", salt, Light);
        var b = KeyDerivation.DeriveKey("correct horse battery staple", salt, Light);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_salt_produces_different_key()
    {
        var a = KeyDerivation.DeriveKey("same-password", KeyDerivation.NewSalt(), Light);
        var b = KeyDerivation.DeriveKey("same-password", KeyDerivation.NewSalt(), Light);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_password_produces_different_key()
    {
        var salt = KeyDerivation.NewSalt();

        var a = KeyDerivation.DeriveKey("password-one", salt, Light);
        var b = KeyDerivation.DeriveKey("password-two", salt, Light);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DeriveKey_returns_32_bytes_by_default()
    {
        var key = KeyDerivation.DeriveKey("pw", KeyDerivation.NewSalt(), Light);

        Assert.Equal(VaultCrypto.KeySizeBytes, key.Length);
    }

    [Fact]
    public void NewSalt_returns_expected_size_and_is_random()
    {
        var a = KeyDerivation.NewSalt();
        var b = KeyDerivation.NewSalt();

        Assert.Equal(KeyDerivation.SaltSizeBytes, a.Length);
        Assert.NotEqual(a, b);
    }

    // --- KDF 자동 상향 (M5, design 7.5) ---

    [Fact]
    public void RaisedTo_takes_per_field_maximum()
    {
        var weak = new KdfParams(MemoryKiB: 8192, Iterations: 2, Parallelism: 1);
        var floor = new KdfParams(MemoryKiB: 65536, Iterations: 3, Parallelism: 4);

        var raised = weak.RaisedTo(floor);

        Assert.Equal(65536, raised.MemoryKiB);
        Assert.Equal(3, raised.Iterations);
        Assert.Equal(4, raised.Parallelism);
    }

    [Fact]
    public void RaisedTo_never_downgrades_stronger_fields()
    {
        var strongMemory = new KdfParams(MemoryKiB: 131072, Iterations: 2, Parallelism: 4);
        var floor = new KdfParams(MemoryKiB: 65536, Iterations: 3, Parallelism: 4);

        var raised = strongMemory.RaisedTo(floor);

        Assert.Equal(131072, raised.MemoryKiB); // 더 강한 메모리는 유지
        Assert.Equal(3, raised.Iterations);     // 약한 반복만 상향
    }

    [Fact]
    public void NeedsUpgradeTo_true_when_any_field_below_floor()
    {
        var weak = new KdfParams(MemoryKiB: 8192, Iterations: 3, Parallelism: 4);
        var floor = new KdfParams(MemoryKiB: 65536, Iterations: 3, Parallelism: 4);

        Assert.True(weak.NeedsUpgradeTo(floor));
    }

    [Fact]
    public void NeedsUpgradeTo_false_when_at_or_above_floor()
    {
        var floor = new KdfParams(MemoryKiB: 65536, Iterations: 3, Parallelism: 4);
        var atFloor = floor;
        var above = new KdfParams(MemoryKiB: 131072, Iterations: 4, Parallelism: 4);

        Assert.False(atFloor.NeedsUpgradeTo(floor));
        Assert.False(above.NeedsUpgradeTo(floor));
    }
}
