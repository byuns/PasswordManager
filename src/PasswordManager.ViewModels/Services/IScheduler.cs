namespace PasswordManager.ViewModels.Services;

/// <summary>지연 실행 추상화. 자동 삭제·자동 잠금 등 "일정 시간 뒤 한 번" 동작에 쓴다(테스트에서는 수동 실행).</summary>
public interface IScheduler
{
    /// <summary>delay 후 action을 한 번 실행하도록 예약한다.</summary>
    void Schedule(TimeSpan delay, Action action);
}
