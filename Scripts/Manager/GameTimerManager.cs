using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public enum TimerState { None, Ready, Playing, Expired }

public class GameTimerManager : NetworkBehaviour
{
    public static GameTimerManager Instance { get; private set; }
    // 현재 타이머가 어떤 상태인지 구분하기 위한 Net변수임.
    public NetworkVariable<TimerState> CurrentState { get; private set; } = new(TimerState.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 오직 서버만 수정 가능한 동기화 시간 변수
    public NetworkVariable<int> RemainingTime { get; private set; } = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 시간이 종료되었을 때 InGameManager나 UI가 들을 수 있도록 이벤트 개방
    public event Action OnTimerExpired;
    private int actualPlayTimeSeconds;
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// 서버가 호출하여 타이머를 세팅하고 시작하는 함수
    /// </summary>
    public void StartTimer(int delaySeconds, int durationSeconds)
    {
        if (!IsServer) return;
        actualPlayTimeSeconds = durationSeconds;
        StartCoroutine(ServerTimerRoutine(delaySeconds));
    }

    private IEnumerator ServerTimerRoutine(int delaySeconds = 0)
    {
        // ------ 대기 타이머 ------
        CurrentState.Value = TimerState.Ready;
        RemainingTime.Value = delaySeconds;

        while (RemainingTime.Value > 0)
        {
            yield return new WaitForSeconds(1f);
            RemainingTime.Value--;
        }

        // ------ 실제 플레이 타이머 ------
        CurrentState.Value = TimerState.Playing;
        RemainingTime.Value = actualPlayTimeSeconds;

        while (RemainingTime.Value > 0)
        {
            yield return new WaitForSeconds(1f);
            RemainingTime.Value--;
        }


        // ------ 타이머 종료 ------
        CurrentState.Value = TimerState.Expired;
        Debug.LogWarning("[GameTimerManager] 본 게임 제한 시간이 종료되었습니다.");
        OnTimerExpired?.Invoke();
    }
}