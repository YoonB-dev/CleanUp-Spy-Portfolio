using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 다른 플레이어를 붙잡는 로직/네트워크(조준, 검증, 상태 동기화). 서버 권위 방식. <br/>
/// </summary>
public class PlayerGrab : NetworkBehaviour
{
    [Header("조준")]
    [SerializeField] private Camera playerCamera;

    [Header("디버그")]
    [Tooltip("켜면 토글(누를 때마다 잡기 - 놓기), 끄면 홀드(누르는 동안만)")]
    [SerializeField] private bool toggleGrab = false;

    private const float GRAB_REACH_RANGE = 1.8f;        // 손이 닿는 최대 거리
    private const float GRAB_SPHERE_RADIUS = 0.35f;     // 조준 판정 여유 반경
    private const float GRAB_VALIDATE_RANGE = 3.0f;     // 서버가 붙잡기를 승인하는 최대 거리
    private const float GRAB_STRETCH_LIMIT = 1.6f;      // 상대가 목표 지점에서 이만큼 벌어지면(막힘) 놓침
    private const float GRAB_SETTLE_TIME = 0.4f;        // 당겨오는 초반 유예(아직 멀어서 오판 방지)
    private const float REACH_DURATION = 0.45f;         // 갱신 끊긴 뒤 팔을 내리기까지의 시간
    private const float REACH_REFRESH_INTERVAL = 0.2f;  // 홀드 중 reach 갱신 주기(REACH_DURATION보다 짧아야 함)
    private const float GRAB_TRY_INTERVAL = 0.1f;       // 홀드 중 붙잡기 재시도 주기
    private const float GRAB_TARGET_HEIGHT = 1.1f;      // 잡은 지점을 못 구했을 때의 대체 높이

    private readonly NetworkVariable<NetworkObjectReference> _grabbedByRef = new(          // 나를 붙잡은 상대
        new NetworkObjectReference(), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _isGrabbingNet = new(                           // 내가 누군가를 붙잡고 있는지
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _isReachingNet = new(                           // 손을 뻗고 있는지(붙잡기와 무관)
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _lookElevationNet = new(                       // 상하 시선 각도(도, +위). Owner가 기록
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<NetworkObjectReference> _grabbedTargetRef = new(      // 내가 붙잡은 대상
        new NetworkObjectReference(), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector3> _grabLocalPointNet = new(                    // 붙잡은 지점(대상 로컬 좌표)
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private PlayerGrab _serverGrabTarget;   // [서버] 내가 붙잡은 대상
    private float _reachEndTime;            // [서버] 손 뻗기 자동 종료 시각
    private float _grabStartTime;           // [서버] 붙잡은 시각(당겨오기 유예 판정용)
    private Vector3 _holdOffset;            // [서버] 붙잡은 순간의 상대 위치(나 기준 오프셋). 내가 이동하면 오프셋을 유지하며 끌려옴.

    private PlayerMovement _playerMovement;
    private PlayerActionGate _gate;

    // 붙잡기 충돌 무시 계산용 임시 버퍼
    private readonly List<Collider> _selfColliders = new();
    private readonly List<Collider> _otherColliders = new();

    // [Owner] 붙잡기 키 홀드 상태 + 재시도 타이머
    private bool _grabHeld;
    private float _nextReachRefreshTime;
    private float _nextGrabTryTime;

    /// <summary>다른 플레이어에게 붙잡혀 있는지</summary>
    public bool IsGrabbed => NetworkManager.Singleton != null && _grabbedByRef.Value.TryGet(out _);

    /// <summary>내가 누군가를 붙잡고 있는지</summary>
    public bool IsGrabbing => _isGrabbingNet.Value;

    /// <summary>내가 손을 뻗고 있는지</summary>
    public bool IsReaching => _isReachingNet.Value;

    /// <summary>상하 시선 각도</summary>
    public float LookElevation => _lookElevationNet.Value;

    /// <summary>붙잡은 지점의 현재 월드 위치(대상 이동/회전 반영)</summary>
    public bool TryGetGrabWorldPoint(out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        if (NetworkManager.Singleton == null || !_grabbedTargetRef.Value.TryGet(out NetworkObject grabbedObject))
        {
            return false;
        }

        worldPoint = grabbedObject.transform.TransformPoint(_grabLocalPointNet.Value);
        return true;
    }

    private void Awake()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }

        _playerMovement = GetComponent<PlayerMovement>();
        _gate = PlayerActionGate.GetOrAdd(gameObject);
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            return;
        }

        ServerReleaseGrab();

        // 나를 붙잡던 상대가 있으면 그쪽 상태도 해제
        if (_grabbedByRef.Value.TryGet(out NetworkObject grabberObject)
            && grabberObject.TryGetComponent<PlayerGrab>(out PlayerGrab grabber))
        {
            grabber.ServerReleaseGrab();
        }
    }

    private void Update()
    {
        if (IsOwner)
        {
            UpdateGrabHeldOwner();
            UpdateLookElevationOwner();
        }

        if (!IsServer)
        {
            return;
        }

        // 갱신이 끊기면 팔 내리기
        if (_isReachingNet.Value && Time.time > _reachEndTime)
        {
            _isReachingNet.Value = false;
        }

        if (_serverGrabTarget == null)
        {
            return;
        }

        if (_holdOffset.y > 0f)
        {
            _holdOffset.y = 0f;
        }

        // 당겨오는 초반엔 아직 멀어서 오판하므로 유예
        if (Time.time - _grabStartTime < GRAB_SETTLE_TIME)
        {
            return;
        }

        // 상대가 목표 지점(당겨올 위치)에서 한계 이상 못 따라오면(막힘) 놓아줌
        Vector3 holdPoint = transform.position + transform.rotation * _holdOffset;
        float victimLag = Vector3.Distance(_serverGrabTarget.transform.position, holdPoint);
        if (victimLag > GRAB_STRETCH_LIMIT)
        {
            ServerReleaseGrab();
        }
    }

    // 키를 누르는 동안 reach를 갱신(홀드 유지)하고, 아직 못 잡았으면 재시도
    private void UpdateGrabHeldOwner()
    {
        if (!_grabHeld)
        {
            return;
        }

        if (!IsGrabbing && !_gate.CanDo(PlayerAction.Grab))
        {
            SetGrabActive(false);
            return;
        }

        if (Time.time >= _nextReachRefreshTime)
        {
            _nextReachRefreshTime = Time.time + REACH_REFRESH_INTERVAL;
            RequestReachServerRpc();
        }

        // 손이 대상에 닿는 순간 잡히도록 주기적으로 시도
        if (!IsGrabbing && Time.time >= _nextGrabTryTime)
        {
            _nextGrabTryTime = Time.time + GRAB_TRY_INTERVAL;
            if (TryFindGrabTarget(GRAB_REACH_RANGE, null, out PlayerGrab target, out _))
            {
                RequestGrabServerRpc(new NetworkObjectReference(target.NetworkObject));
            }
        }
    }

    // 상하 시선 각도를 동기화(변할 때만)
    private void UpdateLookElevationOwner()
    {
        if (playerCamera == null || (!IsReaching && !IsGrabbing))
        {
            return;
        }

        float elevation = Mathf.Asin(Mathf.Clamp(playerCamera.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        if (Mathf.Abs(elevation - _lookElevationNet.Value) > 1f)
        {
            _lookElevationNet.Value = elevation;
        }
    }

    /// <summary>붙잡기 입력(G키). 홀드 모드면 누르는 동안, 토글 모드면 누를 때마다 잡기↔놓기.</summary>
    public void OnGrab(InputAction.CallbackContext context)
    {
        if (!IsOwner)
        {
            return;
        }

        if (toggleGrab)
        {
            // 토글: 누를 때마다 켜고 끔(떼는 건 무시)
            if (context.started)
            {
                SetGrabActive(!_grabHeld);
            }
        }
        else
        {
            // 홀드: 누르면 시작, 떼면 종료
            if (context.started)
            {
                SetGrabActive(true);
            }
            else if (context.canceled)
            {
                SetGrabActive(false);
            }
        }
    }

    private void SetGrabActive(bool active)
    {
        if (active && !_gate.CanDo(PlayerAction.Grab))
        {
            return;
        }

        _grabHeld = active;
        if (active)
        {
            // 다음 프레임 즉시 시도되도록 타이머 리셋
            _nextReachRefreshTime = 0f;
            _nextGrabTryTime = 0f;
        }
        else
        {
            RequestStopServerRpc();
        }
    }

    /// <summary>
    /// 카메라 정면으로 스피어캐스트해 플레이어와 손이 닿은 지점(대상 로컬)을 찾는다. <br/>
    /// Owner의 조준(requiredTarget = null, 가장 가까운 대상)과 서버의 조준 재검증(requiredTarget 지정) 양쪽에서 사용.
    /// </summary>
    private bool TryFindGrabTarget(float range, PlayerGrab requiredTarget, out PlayerGrab target, out Vector3 localGrabPoint)
    {
        target = null;
        localGrabPoint = Vector3.zero;
        if (playerCamera == null)
        {
            return false;
        }

        Ray ray = new(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit[] hits = Physics.SphereCastAll(ray, GRAB_SPHERE_RADIUS, range);

        PlayerGrab nearestTarget = null;
        float nearestDistance = float.MaxValue;
        Vector3 nearestHitPoint = Vector3.zero;
        bool nearestHasHitPoint = false;

        foreach (RaycastHit hit in hits)
        {
            PlayerGrab candidate = hit.collider.GetComponentInParent<PlayerGrab>();
            if (candidate == null || candidate == this)   // 자기 캡슐도 맞으므로 자신은 건너뜀
            {
                continue;
            }

            if (requiredTarget != null && candidate != requiredTarget)
            {
                continue;
            }

            float candidateDistance = Vector3.Distance(playerCamera.transform.position, candidate.transform.position);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                nearestTarget = candidate;
                nearestHasHitPoint = hit.distance > 0f;   // 시작 겹침(distance 0)이면 hit.point 신뢰 불가
                nearestHitPoint = hit.point;
            }
        }

        if (nearestTarget == null)
        {
            return false;
        }

        // 실제 히트점(없으면 몸통 높이)을 대상 로컬로 변환해 저장
        Vector3 worldGrabPoint = nearestHasHitPoint
            ? nearestHitPoint
            : nearestTarget.transform.position + Vector3.up * GRAB_TARGET_HEIGHT;
        localGrabPoint = nearestTarget.transform.InverseTransformPoint(worldGrabPoint);

        target = nearestTarget;
        return true;
    }

    [ServerRpc]
    private void RequestReachServerRpc()
    {
        _isReachingNet.Value = true;
        _reachEndTime = Time.time + REACH_DURATION;
    }

    [ServerRpc]
    private void RequestGrabServerRpc(NetworkObjectReference targetRef)
    {
        if (_serverGrabTarget != null)
        {
            return;
        }

        if (!_gate.CanDo(PlayerAction.Grab))
        {
            return;
        }

        if (!targetRef.TryGet(out NetworkObject targetObject))
        {
            return;
        }

        if (!targetObject.TryGetComponent<PlayerGrab>(out PlayerGrab target) || target == this)
        {
            return;
        }

        if (target.IsGrabbed)
        {
            return;
        }

        // 서버에서 거리 재검증(치트 방어).
        float distance = Vector3.Distance(transform.position, target.transform.position);
        if (distance > GRAB_VALIDATE_RANGE)
        {
            return;
        }

        // 서버에서 조준 재검증(치트 방어). 대상이 실제로 시선 정면에 있는지 서버 물리로 다시 확인하고,
        // 붙잡은 지점도 클라이언트 값을 믿지 않고 서버가 직접 계산한다.
        // 지연 보정을 위해 조준 사거리(GRAB_REACH_RANGE)가 아닌 검증 사거리(GRAB_VALIDATE_RANGE)로 판정.
        if (!TryFindGrabTarget(GRAB_VALIDATE_RANGE, target, out _, out Vector3 localGrabPoint))
        {
            return;
        }

        _serverGrabTarget = target;
        target._grabbedByRef.Value = new NetworkObjectReference(NetworkObject);
        _grabbedTargetRef.Value = new NetworkObjectReference(target.NetworkObject);
        _grabLocalPointNet.Value = localGrabPoint;
        _isGrabbingNet.Value = true;

        // 붙잡은 순간 상대 위치를 내 정면 기준 로컬 오프셋으로 고정.
        // 로컬이라 내가 돌아도 상대가 계속 정면에 유지되며 밀려감(월드 고정이면 옆으로 샘).
        _holdOffset =
            Quaternion.Inverse(transform.rotation) *
            (target.transform.position - transform.position);
        _grabStartTime = Time.time;

        // 서로 밀치지 않도록 잡은 사람↔붙잡힌 사람 충돌만 무시(월드 충돌은 유지)
        SetGrabCollisionIgnored(target, true);
    }

    // 잡은 사람과 붙잡힌 사람의 몸 콜라이더(캡슐+래그돌)끼리 충돌을 켜고 끔. [서버 전용]
    private void SetGrabCollisionIgnored(PlayerGrab other, bool ignore)
    {
        if (_playerMovement == null || other == null)
        {
            return;
        }

        PlayerMovement otherMovement = other.GetComponent<PlayerMovement>();
        if (otherMovement == null)
        {
            return;
        }

        _playerMovement.CollectBodyColliders(_selfColliders);
        otherMovement.CollectBodyColliders(_otherColliders);

        foreach (Collider self in _selfColliders)
        {
            foreach (Collider otherCollider in _otherColliders)
            {
                if (self != null && otherCollider != null)
                {
                    Physics.IgnoreCollision(self, otherCollider, ignore);
                }
            }
        }
    }

    // 키 뗌: 놓기 + 팔 즉시 내림
    [ServerRpc]
    private void RequestStopServerRpc()
    {
        ServerReleaseGrab();
        _isReachingNet.Value = false;
        _reachEndTime = 0f;
    }

    /// <summary>붙잡은 대상을 놓고 상태 초기화. [서버 전용]</summary>
    public void ServerReleaseGrab()
    {
        if (!IsServer)
        {
            return;
        }

        if (_serverGrabTarget != null)
        {
            SetGrabCollisionIgnored(_serverGrabTarget, false);
            _serverGrabTarget._grabbedByRef.Value = new NetworkObjectReference();
            _serverGrabTarget = null;
        }

        _grabbedTargetRef.Value = new NetworkObjectReference();
        _isGrabbingNet.Value = false;
    }

    /// <summary>나를 붙잡은 상대가 나를 유지하려는 지점(고정 오프셋 기준). 드래그 계산(PlayerMovement)용.</summary>
    public bool TryGetHoldPoint(out Vector3 holdPoint)
    {
        holdPoint = Vector3.zero;
        if (!_grabbedByRef.Value.TryGet(out NetworkObject grabberObject))
        {
            return false;
        }

        if (!grabberObject.TryGetComponent<PlayerGrab>(out PlayerGrab grabber))
        {
            return false;
        }

        holdPoint = grabber.transform.position + grabber.transform.rotation * grabber._holdOffset;
        return true;
    }
}
