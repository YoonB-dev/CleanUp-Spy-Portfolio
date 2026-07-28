using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 펀치 입력(H키)의 좌우 교대와 타이밍을 관리 (서버 권위). <br/>
/// 실제 팔 자세는 서버 물리에서 RagdollPoser가 이 상태를 읽어 적용한다.
/// </summary>
public class PlayerPunch : NetworkBehaviour
{
    private const float WINDUP_DURATION = 0.32f;    // 팔을 크게 젖혀 감는 시간
    private const float STRIKE_DURATION = 0.16f;    // 휘두르는 시간. 무거운 팔이라 스냅되지 않는다
    private const float HOLD_DURATION = 0.06f;      // 다 휘두른 자세 유지(따라나감)
    private const float RECOVER_DURATION = 0.45f;   // 팔이 흐물흐물 돌아오는 시간
    private const float PUNCH_INTERVAL = 0.75f;     // 다음 펀치까지 최소 간격(홀드 연타 주기)

    private const float WINDUP_END = WINDUP_DURATION;
    private const float STRIKE_END = WINDUP_END + STRIKE_DURATION;
    private const float HOLD_END = STRIKE_END + HOLD_DURATION;
    private const float PUNCH_DURATION = HOLD_END + RECOVER_DURATION;

    private float _punchStartTime = float.NegativeInfinity;   // [서버] 현재 펀치 시작 시각
    private bool _isLeftHand;                                 // [서버] 이번에 휘두르는 손
    private bool _punchHeld;                                  // [Owner] 키 홀드 상태
    private float _nextPunchRequestTime;                      // [Owner] 다음 연타 요청 시각

    // 네트워크에 스폰되지 않았으면(오프라인 테스트) 이 인스턴스가 곧 오너이자 서버
    private bool IsOffline => !IsSpawned;

    private bool HasInputAuthority => IsOffline || IsOwner;

    /// <summary>
    /// 현재 펀치 자세 상태. 펀치 중이 아니면 false. [서버 전용]
    /// </summary>
    /// <param name="isLeftHand">휘두르는 손이 왼손인지</param>
    /// <param name="weight">펀치 자세 가중치 (0=평상 자세, 1=펀치 자세)</param>
    /// <param name="extension">팔 뻗음 정도 (0=당김, 1=최대로 뻗음)</param>
    public bool TryGetPunchPose(out bool isLeftHand, out float weight, out float extension)
    {
        isLeftHand = _isLeftHand;
        weight = 0f;
        extension = 0f;

        float elapsed = Time.time - _punchStartTime;
        if (elapsed < 0f || elapsed > PUNCH_DURATION)
        {
            return false;
        }

        if (elapsed < WINDUP_END)
        {
            // 당기면서 펀치 자세로 진입
            weight = elapsed / WINDUP_DURATION;
        }
        else if (elapsed < HOLD_END)
        {
            weight = 1f;
            extension = Mathf.Clamp01((elapsed - WINDUP_END) / STRIKE_DURATION);
        }
        else
        {
            // 뻗은 채로 가중치만 낮춰 평상 자세로 복귀
            weight = 1f - (elapsed - HOLD_END) / RECOVER_DURATION;
            extension = 1f;
        }

        return true;
    }

    /// <summary>펀치 입력(H키). 누르는 동안 양손을 번갈아 반복한다.</summary>
    public void OnPunch(InputAction.CallbackContext context)
    {
        if (!HasInputAuthority)
        {
            return;
        }

        if (context.started)
        {
            _punchHeld = true;
            _nextPunchRequestTime = 0f;   // 누른 즉시 첫 펀치
        }
        else if (context.canceled)
        {
            _punchHeld = false;
        }
    }

    private void Update()
    {
        if (!_punchHeld || !HasInputAuthority || Time.time < _nextPunchRequestTime)
        {
            return;
        }

        _nextPunchRequestTime = Time.time + PUNCH_INTERVAL;

        if (IsOffline)
        {
            ServerStartPunch();
        }
        else
        {
            RequestPunchServerRpc();
        }
    }

    [ServerRpc]
    private void RequestPunchServerRpc()
    {
        ServerStartPunch();
    }

    /// <summary>반대 손으로 새 펀치를 시작. 연타 주기는 서버에서도 검증. [서버 전용]</summary>
    private void ServerStartPunch()
    {
        if (Time.time - _punchStartTime < PUNCH_INTERVAL)
        {
            return;
        }

        _isLeftHand = !_isLeftHand;
        _punchStartTime = Time.time;
    }
}
