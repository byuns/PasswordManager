using PasswordManager.Core.Security;

namespace PasswordManager.Core.Tests.Security;

public class MemoryHygieneTests
{
    [Fact]
    public void Clear_zeroes_all_bytes()
    {
        var bytes = new byte[] { 1, 2, 3, 250, 99 };

        MemoryHygiene.Clear(bytes);

        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Clear_is_null_safe()
    {
        MemoryHygiene.Clear(null); // 예외 없이 무시
    }

    [Fact]
    public void Clear_handles_empty_array()
    {
        MemoryHygiene.Clear(Array.Empty<byte>()); // 예외 없음
    }
}
