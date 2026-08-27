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

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _pickupHighlight = GetComponent<PickupHighlight>();
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

    [ServerRpc]
    public void RequestStartDragServerRpc(ulong playerId)
    {
        if (!IsServer || IsBeingDragged) return;
        _grabberPlayerId.Value = playerId;
    }

    [ServerRpc]
    public void RequestStopDragServerRpc()
    {
        if (!IsServer) return;
        _grabberPlayerId.Value = ulong.MaxValue;
    }

    private void FixedUpdate()
    {
        if (!IsServer || !IsBeingDragged || _currentGrabberTransform == null) return;

        // [2] 플레이어 정면 방향으로 Target Position 계산 (회전 시 오브젝트가 호를 그리며 따라옴)
        Vector3 targetPosition = _currentGrabberTransform.position + (_currentGrabberTransform.forward * followOffsetDistance);
        targetPosition.y = transform.position.y; // 바닥 유지

        // 물리 위치 이동
        Vector3 newPos = Vector3.Lerp(_rb.position, targetPosition, Time.fixedDeltaTime * followSpeed);
        _rb.MovePosition(newPos);

        // [3] 캐릭터 회전값 그대로 적용 (플레이어 바라보는 방향 동기화)
        Quaternion targetRotation = Quaternion.LookRotation(_currentGrabberTransform.forward, Vector3.up);
        Quaternion newRot = Quaternion.Slerp(_rb.rotation, targetRotation, Time.fixedDeltaTime * rotateSpeed);
        _rb.MoveRotation(newRot);
    }
}