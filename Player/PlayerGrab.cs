using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 다른 플레이어를 붙잡는 로직/네트워크(조준, 검증, 상태 동기화). 서버 권위 방식. <br/>
/// 팔 뻗는 연출은 PlayerArmReach가 이 컴포넌트 상태를 읽어 처리
/// </summary>
public class PlayerGrab : NetworkBehaviour
{
    [Header("조준")]
    [SerializeField] private Camera playerCamera;

    private const float GRAB_REACH_RANGE = 1.8f;        // 손이 닿는 최대 거리
    private const float GRAB_SPHERE_RADIUS = 0.35f;     // 조준 판정 여유 반경
    private const float GRAB_VALIDATE_RANGE = 3.0f;     // 서버가 붙잡기를 승인하는 최대 거리
    private const float GRAB_STRETCH_LIMIT = 1.6f;      // 잡은 대상을 놓치는 거리
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
    private readonly NetworkVariable<float> _grabInitialDistanceNet = new(                 // 붙잡은 순간 간격(늘어남/놓기 기준)
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private PlayerGrab _serverGrabTarget;   // [서버] 내가 붙잡은 대상
    private float _reachEndTime;            // [서버] 손 뻗기 자동 종료 시각
    private Vector3 _holdOffset;            // [서버] 붙잡은 순간의 상대 위치(나 기준 오프셋). 내가 이동하면 오프셋을 유지하며 끌려옴.

    private FirstPersonLook _firstPersonLook;
    private bool _yawLimitApplied;

    // [Owner] 붙잡기 키 홀드 상태 + 재시도 타이머
    private bool _grabHeld;
    private float _nextReachRefreshTime;
    private float _nextGrabTryTime;

    /// <summary>다른 플레이어에게 붙잡혀 있는지</summary>
    public bool IsGrabbed => _grabbedByRef.Value.TryGet(out _);

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
        if (!_grabbedTargetRef.Value.TryGet(out NetworkObject grabbedObject))
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

        _firstPersonLook = GetComponent<FirstPersonLook>();
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
            UpdateLookLockOwner();
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

        // 잡은 대상과 한계 이상으로 벌어지면(팔이 최대로 늘어남) 놓아줌
        float distance = Vector3.Distance(transform.position, _serverGrabTarget.transform.position);
        if (distance > _grabInitialDistanceNet.Value + GRAB_STRETCH_LIMIT)
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

        if (Time.time >= _nextReachRefreshTime)
        {
            _nextReachRefreshTime = Time.time + REACH_REFRESH_INTERVAL;
            RequestReachServerRpc();
        }

        // 손이 대상에 닿는 순간 잡히도록 주기적으로 시도
        if (!IsGrabbing && Time.time >= _nextGrabTryTime)
        {
            _nextGrabTryTime = Time.time + GRAB_TRY_INTERVAL;
            if (TryFindGrabTarget(out NetworkObjectReference targetRef, out Vector3 localGrabPoint))
            {
                RequestGrabServerRpc(targetRef, localGrabPoint);
            }
        }
    }

    // 붙잡는 동안 좌우 시점 제한 on/off (상하는 자유)
    private void UpdateLookLockOwner()
    {
        if (_firstPersonLook == null)
        {
            return;
        }

        bool shouldLimit = IsGrabbing;
        if (shouldLimit == _yawLimitApplied)
        {
            return;
        }

        _yawLimitApplied = shouldLimit;
        _firstPersonLook.SetLookYawLimited(shouldLimit);
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

    /// <summary>붙잡기 입력(G키). 누르면 홀드 시작, 떼면 놓기</summary>
    public void OnGrab(InputAction.CallbackContext context)
    {
        if (!IsOwner)
        {
            return;
        }

        if (context.started)
        {
            _grabHeld = true;
            // 다음 프레임 즉시 시도되도록 타이머 리셋
            _nextReachRefreshTime = 0f;
            _nextGrabTryTime = 0f;
        }
        else if (context.canceled)
        {
            _grabHeld = false;
            RequestStopServerRpc();
        }
    }

    // 카메라 정면으로 스피어캐스트해 가장 가까운 플레이어와 손이 닿은 지점(대상 로컬)을 찾는다.
    private bool TryFindGrabTarget(out NetworkObjectReference targetRef, out Vector3 localGrabPoint)
    {
        targetRef = default;
        localGrabPoint = Vector3.zero;
        if (playerCamera == null)
        {
            return false;
        }

        Ray ray = new(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit[] hits = Physics.SphereCastAll(ray, GRAB_SPHERE_RADIUS, GRAB_REACH_RANGE);

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

        targetRef = new NetworkObjectReference(nearestTarget.NetworkObject);
        return true;
    }

    [ServerRpc]
    private void RequestReachServerRpc()
    {
        _isReachingNet.Value = true;
        _reachEndTime = Time.time + REACH_DURATION;
    }

    [ServerRpc]
    private void RequestGrabServerRpc(NetworkObjectReference targetRef, Vector3 localGrabPoint)
    {
        if (_serverGrabTarget != null)
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

        _serverGrabTarget = target;
        target._grabbedByRef.Value = new NetworkObjectReference(NetworkObject);
        _grabbedTargetRef.Value = new NetworkObjectReference(target.NetworkObject);
        _grabLocalPointNet.Value = localGrabPoint;
        _isGrabbingNet.Value = true;

        // 붙잡은 순간 상대 위치를 오프셋으로 고정(잡는 순간 안 움직이고, 이동 시 끌려옴).
        _holdOffset = target.transform.position - transform.position;
        _grabInitialDistanceNet.Value = _holdOffset.magnitude;
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
    private void ServerReleaseGrab()
    {
        if (!IsServer)
        {
            return;
        }

        if (_serverGrabTarget != null)
        {
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

        holdPoint = grabber.transform.position + grabber._holdOffset;
        return true;
    }
}
