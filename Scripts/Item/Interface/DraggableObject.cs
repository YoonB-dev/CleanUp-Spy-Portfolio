using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
public class DraggableObject : NetworkBehaviour
{
    [Header("Drag Settings")]
    [SerializeField] private float followSpeed = 12f;
    [SerializeField] private float rotateSpeed = 10f;
    [SerializeField] private float followOffsetDistance = 1.5f; // 플레이어 정면으로부터의 거리
    [Tooltip("플레이어와 오브젝트(콜라이더 경계) 사이가 이 거리보다 벌어지면 서버가 잡기를 강제로 해제한다 (낭떠러지로 떨어짐 등)")]
    [SerializeField] private float releaseDistance = 3f;

    [Header("Ground Follow")]
    [Tooltip("바닥으로 인식할 레이어. 바닥/계단만 넣을 것 (플레이어나 아이템 레이어가 들어가면 그 위로 올라탄다). 비워두면 Floor")]
    [SerializeField] private LayerMask groundMask;
    [Tooltip("한 번에 올라갈 수 있는 최대 높이 (계단 한 칸). 이보다 높은 곳에는 올라타지 않는다")]
    [SerializeField] private float maxStepUp = 0.5f;
    [Tooltip("끄는 중 계단처럼 따라 내려가는 최대 깊이. 이보다 깊으면 낭떠러지로 보고 떨어진다")]
    [SerializeField] private float maxStepDown = 1.5f;
    [Tooltip("높이가 바뀔 때 오르내리는 속도 (m/s)")]
    [SerializeField] private float verticalSpeed = 4f;

    [Header("Falling")]
    [Tooltip("공중에서 놓거나 낭떠러지에서 떨어질 때의 낙하 가속도 (m/s²)")]
    [SerializeField] private float fallGravity = 20f;
    [Tooltip("놓은 뒤 이 거리만큼 떨어져도 바닥이 없으면 낙하를 멈춘다 (바닥 레이어 누락 시 끝없이 떨어지는 것 방지)")]
    [SerializeField] private float maxFallDistance = 30f;

    [Header("Grip Settings")]
    [Tooltip("플레이어 손이 잡아야 할 위치 (없으면 이 오브젝트의 Transform 사용)")]
    [SerializeField] private Transform handlePoint;
    public Transform HandlePoint => handlePoint != null ? handlePoint : transform;

    private Rigidbody _rb;
    private PickupHighlight _pickupHighlight;
    private NetworkVariable<ulong> _grabberPlayerId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsBeingDragged => _grabberPlayerId.Value != ulong.MaxValue;
    public ulong GrabberPlayerId => _grabberPlayerId.Value;

    private Transform _currentGrabberTransform;
    private RagdollPoser _currentGrabberRagdollPoser;

    // 바닥 감지용 (끌기 시작할 때 계산)
    private Collider[] _bodyColliders = System.Array.Empty<Collider>(); // 트리거 제외 콜라이더 (거리 판정에도 사용)
    private float _bottomOffset;  // 피벗에서 콜라이더 바닥면까지 높이
    private float _probeRadius;   // 발밑 바닥을 훑는 구 반지름 (회전해도 결과가 같도록 구 사용)
    private float _bodyHeight;    // 콜라이더 전체 높이. 감지 시작 높이로 사용
    private readonly RaycastHit[] _groundHits = new RaycastHit[8];

    // 낙하 (Kinematic이라 중력이 없으므로 직접 떨어뜨린다)
    private float _fallSpeed;
    private bool _isSettling;     // 놓은 뒤 바닥에 닿을 때까지 떨어지는 중
    private float _settleStartY;

    // 멀리서 잡으면 끌려오기 전부터 거리 초과로 놓이지 않도록, 한 번 가까이 온 뒤부터 거리 판정을 한다
    private bool _hasReachedGrabber;

    // 바닥면 크기 대비 감지 구 크기. AABB 기준이라 회전하면 실제보다 커지므로 여유를 둔다
    private const float PROBE_RADIUS_RATIO = 0.7f;
    private const float MIN_PROBE_RADIUS = 0.05f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _pickupHighlight = GetComponent<PickupHighlight>();

        if (groundMask == 0)
        {
            groundMask = LayerMask.GetMask("Floor");
        }
    }

    public override void OnNetworkSpawn()
    {
        _grabberPlayerId.OnValueChanged += OnGrabberChanged;
    }

    public override void OnNetworkDespawn()
    {
        _grabberPlayerId.OnValueChanged -= OnGrabberChanged;
    }

    public void SetHighlighted(bool highlighted)
    {
        // 잡혀있는 상태라면 강제로 아웃라인을 끔
        if (IsBeingDragged && highlighted)
        {
            highlighted = false;
        }

        if (_pickupHighlight != null)
        {
            _pickupHighlight.SetHighlighted(highlighted);
        }
    }

    private void OnGrabberChanged(ulong previous, ulong current)
    {
        if (current == ulong.MaxValue)
        {
            // 서버가 강제로 놓게 했을 수도 있으므로, 잡고 있던 플레이어 쪽 끌기 상태도 정리시킨다
            if (_currentGrabberTransform != null && _currentGrabberTransform.TryGetComponent<PlayerInteraction>(out var previousInteraction))
            {
                previousInteraction.OnDragReleased(this);
            }

            // 잡기 해제 시 래그돌 포즈 초기화
            if (_currentGrabberRagdollPoser != null)
            {
                _currentGrabberRagdollPoser.SetCarryRequested(false);
                _currentGrabberRagdollPoser.SetCarryTarget(null);
            }
            _currentGrabberTransform = null;
            _currentGrabberRagdollPoser = null;
        }
        else if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(current, out var netObj))
        {
            SetHighlighted(false);
            
            _currentGrabberTransform = netObj.transform;
            if (netObj.TryGetComponent<PlayerInteraction>(out var interaction))
            {
                _currentGrabberRagdollPoser = interaction.playerRagDollPoser;

                // [1] 손 뻗기 자세 유도 (RagdollPoser 활용)
                _currentGrabberRagdollPoser?.SetCarryRequested(true);
                if (TryGetComponent<CarryGripPoints>(out var gripPoints))
                {
                    _currentGrabberRagdollPoser?.SetCarryTarget(gripPoints);
                }
            }
        }
    }

    /// <summary>
    /// 실제 서버에서 끌기 시작을 처리하는 로직 (RPC가 아니므로 서버 내부에서 자유롭게 호출 가능).
    /// 호출하는 쪽(PlayerInteraction)이 자신이 소유한 오브젝트 위에서 ServerRpc를 받아 이 메서드를 호출해야 한다.
    /// </summary>
    public void StartDrag(ulong playerId)
    {
        if (!IsServer || IsBeingDragged) return;
        CacheFootprint();
        _hasReachedGrabber = false;
        _isSettling = false;
        _fallSpeed = 0f;
        _grabberPlayerId.Value = playerId;
    }

    /// <summary>
    /// 실제 서버에서 끌기 해제를 처리하는 로직 (RPC가 아니므로 서버 내부에서 자유롭게 호출 가능).
    /// </summary>
    public void StopDrag()
    {
        if (!IsServer) return;

        if (IsBeingDragged)
        {
            // 공중에서 놓았을 수 있으므로 바닥에 닿을 때까지 떨어뜨린다
            _isSettling = true;
            _fallSpeed = 0f;
            _settleStartY = _rb.position.y;
        }
        _grabberPlayerId.Value = ulong.MaxValue;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        if (!IsBeingDragged)
        {
            if (_isSettling) SettleStep();
            return;
        }

        if (_currentGrabberTransform == null) return;

        // 떨어지거나 끼어서 플레이어와 너무 멀어지면 서버가 잡기를 해제한다 (이후 착지 처리로 이어짐)
        if (!IsTooFarFromGrabber())
        {
            _hasReachedGrabber = true;
        }
        else if (_hasReachedGrabber)
        {
            StopDrag();
            return;
        }

        // [2] 플레이어 정면 방향으로 Target Position 계산 (회전 시 오브젝트가 호를 그리며 따라옴)
        Vector3 targetPosition = _currentGrabberTransform.position + (_currentGrabberTransform.forward * followOffsetDistance);
        targetPosition.y = _rb.position.y; // 높이는 플레이어가 아니라 발밑 바닥 기준으로 따로 계산 (점프해도 안 딸려오게)

        // 물리 위치 이동
        Vector3 newPos = Vector3.Lerp(_rb.position, targetPosition, Time.fixedDeltaTime * followSpeed);
        newPos.y = GetGroundFollowHeight(newPos);
        _rb.MovePosition(newPos);

        // [3] 캐릭터 회전값 그대로 적용 (플레이어 바라보는 방향 동기화)
        Quaternion targetRotation = Quaternion.LookRotation(_currentGrabberTransform.forward, Vector3.up);
        Quaternion newRot = Quaternion.Slerp(_rb.rotation, targetRotation, Time.fixedDeltaTime * rotateSpeed);
        _rb.MoveRotation(newRot);
    }

    /// <summary>
    /// 콜라이더 전체 범위로 바닥면 높이와 감지 구 크기를 계산한다. 끄는 동안은 Y축 회전만 하므로 시작할 때 한 번이면 충분
    /// </summary>
    private void CacheFootprint()
    {
        _bodyColliders = System.Array.FindAll(GetComponentsInChildren<Collider>(), col => !col.isTrigger && col.enabled);

        bool hasBounds = false;
        Bounds bounds = default;
        foreach (Collider col in _bodyColliders)
        {
            if (!hasBounds)
            {
                bounds = col.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        if (!hasBounds)
        {
            _bottomOffset = 0f;
            _bodyHeight = 0f;
            _probeRadius = MIN_PROBE_RADIUS;
            return;
        }

        _bottomOffset = _rb.position.y - bounds.min.y;
        _bodyHeight = bounds.size.y;
        _probeRadius = Mathf.Max(MIN_PROBE_RADIUS, Mathf.Min(bounds.extents.x, bounds.extents.z) * PROBE_RADIUS_RATIO);
    }

    /// <summary>
    /// 잡고 있는 플레이어와 오브젝트 사이가 releaseDistance보다 벌어졌는지. [서버 전용] <br/>
    /// 큰 오브젝트라 중심점이 아니라 콜라이더 경계 기준으로 잰다 (잡기 시작 거리 검증과 같은 방식)
    /// </summary>
    private bool IsTooFarFromGrabber()
    {
        Vector3 grabberPosition = _currentGrabberTransform.position;

        if (_bodyColliders.Length == 0)
        {
            return Vector3.Distance(grabberPosition, _rb.position) > releaseDistance;
        }

        foreach (Collider col in _bodyColliders)
        {
            if (col != null && Vector3.Distance(grabberPosition, col.bounds.ClosestPoint(grabberPosition)) <= releaseDistance)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 끄는 중 이동할 위치의 높이를 계산한다. [서버 전용] <br/>
    /// 계단 범위(maxStepDown) 안에 바닥이 있으면 부드럽게 따라가고, 없으면 낭떠러지로 보고 떨어진다
    /// </summary>
    private float GetGroundFollowHeight(Vector3 position)
    {
        float currentY = _rb.position.y;

        if (TryFindGroundHeight(position, maxStepDown, out float groundY))
        {
            _fallSpeed = 0f;
            return Mathf.MoveTowards(currentY, groundY, verticalSpeed * Time.fixedDeltaTime);
        }

        return currentY - NextFallStep();
    }

    /// <summary>
    /// 놓은 뒤 한 물리 프레임만큼 떨어뜨리고, 바닥에 닿으면 그 위에 세운 뒤 낙하를 끝낸다. [서버 전용]
    /// </summary>
    private void SettleStep()
    {
        Vector3 position = _rb.position;
        float fallStep = NextFallStep();
        bool landed = false;

        if (TryFindGroundHeight(position, fallStep, out float groundY))
        {
            _fallSpeed = 0f;
            // 이번 프레임 안에 닿는 바닥이면 바로 그 위에 세우고,
            // 계단을 오르던 중에 놓아서 바닥이 살짝 위에 있으면 부드럽게 올라간다
            position.y = groundY <= position.y
                ? groundY
                : Mathf.MoveTowards(position.y, groundY, verticalSpeed * Time.fixedDeltaTime);
            landed = Mathf.Approximately(position.y, groundY);
        }
        else
        {
            position.y -= fallStep;
        }

        _rb.MovePosition(position);

        if (landed || _settleStartY - position.y > maxFallDistance)
        {
            _isSettling = false;
            _fallSpeed = 0f;
        }
    }

    /// <summary>
    /// 낙하 속도를 중력만큼 키우고 이번 물리 프레임에 떨어질 거리를 돌려준다
    /// </summary>
    private float NextFallStep()
    {
        _fallSpeed += fallGravity * Time.fixedDeltaTime;
        return _fallSpeed * Time.fixedDeltaTime;
    }

    /// <summary>
    /// 해당 위치 발밑의 바닥을 찾아, 오브젝트가 그 위에 올라섰을 때의 피벗 높이를 구한다. <br/>
    /// SphereCast는 시작부터 겹친 콜라이더를 통째로 무시하므로, 계단(MeshCollider 하나)을 오르다
    /// 높이가 뒤처져 구가 계단에 파묻힌 채 시작하면 계단 전체를 놓친다. 그래서 오브젝트 높이만큼 위에서 시작하고,
    /// 맞은 바닥 중 현재 바닥면 + maxStepUp 이하에서 가장 높은 것을 고른다 (그보다 높은 벽/단상에는 올라타지 않음)
    /// </summary>
    private bool TryFindGroundHeight(Vector3 position, float downDistance, out float pivotY)
    {
        pivotY = 0f;
        float bottomY = _rb.position.y - _bottomOffset;
        float maxGroundY = bottomY + maxStepUp;

        float startHeight = Mathf.Max(maxStepUp, _bodyHeight);
        Vector3 origin = new Vector3(position.x, bottomY + startHeight + _probeRadius, position.z);
        float distance = startHeight + downDistance;

        int count = Physics.SphereCastNonAlloc(origin, _probeRadius, Vector3.down, _groundHits, distance, groundMask, QueryTriggerInteraction.Ignore);

        bool found = false;
        float bestGroundY = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _groundHits[i];

            // 시작부터 겹쳐 있던 콜라이더(거리 0, 위치 정보 없음)와 자기 자신은 제외
            if (hit.distance <= 0f || hit.rigidbody == _rb) continue;

            // 올라갈 수 있는 높이보다 높은 바닥(벽 위, 단상, 위층 바닥 등)은 제외
            if (hit.point.y > maxGroundY) continue;

            if (hit.point.y > bestGroundY)
            {
                bestGroundY = hit.point.y;
                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        pivotY = bestGroundY + _bottomOffset;
        return true;
    }
}