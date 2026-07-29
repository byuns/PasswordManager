using System.Security.Cryptography;

namespace PasswordManager.Core.Security;

/// <summary>
/// 민감한 byte 배열(키·복호화 버퍼)을 사용 후 0으로 소거하는 위생 헬퍼(design 5.5). 문자열은 .NET에서
/// 불변이라 소거할 수 없어 best-effort 한계가 있다(design 13). JIT가 지우지 못하도록 <see
/// cref="CryptographicOperations.ZeroMemory"/>를 사용한다.
/// </summary>
public static class MemoryHygiene
{
    /// <summary>배열을 0으로 채운다. null·빈 배열은 무시한다.</summary>
    public static void Clear(byte[]? bytes)
    {
        if (bytes is { Length: > 0 })
            CryptographicOperations.ZeroMemory(bytes);
    }
}
