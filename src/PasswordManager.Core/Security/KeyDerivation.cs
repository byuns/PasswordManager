using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace PasswordManager.Core.Security;

/// <summary>Argon2id KDF 파라미터. 볼트 헤더에 저장되어 로그인 시 재파생·자동 상향의 기준이 된다(design 5.3, TD-015).</summary>
public sealed record KdfParams(int MemoryKiB, int Iterations, int Parallelism)
{
    /// <summary>초기 권장치: 메모리 64 MiB, 반복 3, 병렬성 4 (TD-015).</summary>
    public static KdfParams Recommended { get; } = new(MemoryKiB: 65536, Iterations: 3, Parallelism: 4);
}

/// <summary>
/// 마스터 비밀번호 + salt를 Argon2id로 늘려 마스터키(KEK)를 파생한다 (design 5.2, TD-015).
/// memory-hard 특성으로 파일 탈취 후 오프라인 크래킹 비용을 크게 높인다.
/// </summary>
public static class KeyDerivation
{
    /// <summary>salt 길이 (128-bit).</summary>
    public const int SaltSizeBytes = 16;

    /// <summary>암호학적 난수로 새 salt를 만든다.</summary>
    public static byte[] NewSalt()
        => RandomNumberGenerator.GetBytes(SaltSizeBytes);

    /// <summary>비밀번호+salt를 Argon2id로 파생해 키를 반환한다. 같은 입력이면 항상 같은 키.</summary>
    public static byte[] DeriveKey(string password, byte[] salt, KdfParams parameters, int outputBytes = VaultCrypto.KeySizeBytes)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = parameters.MemoryKiB,
                Iterations = parameters.Iterations,
                DegreeOfParallelism = parameters.Parallelism,
            };
            return argon2.GetBytes(outputBytes);
        }
        finally
        {
            // 위생: 평문 비밀번호 바이트를 즉시 소거(design 5.5).
            Array.Clear(passwordBytes);
        }
    }
}
