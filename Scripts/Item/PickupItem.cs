using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PickupHighlight))]
public class PickupItem : NetworkBehaviour
{
    [Header("Category")]
    [Tooltip("TrashCan 등 범용 시스템이 이 값만 보고 처리 방식을 결정함")]
    [SerializeField] private PickupCategory category = PickupCategory.Trash;
    public PickupCategory Category => category;

    [Header("Item Data")]
    [Tooltip("인벤토리 아이콘 등 아이템 공통 정보. 쓰레기는 TrashObject에 설정된 TrashData가 우선 사용됨")]
    [SerializeField] private ItemData itemData;
    private TrashObject _trashObject;
    /// <summary>쓰레기면 현재 종류의 TrashData, 아니면 프리팹에 연결된 ItemData를 반환</summary>
    public ItemData ItemData => (_trashObject != null && _trashObject.Data != null) ? _trashObject.Data : itemData;

    private Rigidbody _itemRigidbody;
    private Collider itemCollider;
    private PickupHighlight pickupHighlight;
    [Header("Carry Transform Settings")]
    // 던지기 등에 사용되는 기본 위치
    [SerializeField] private float carryDistance = 1f; public float CarryDistance => carryDistance; 
    [SerializeField] private float carryHeight = -0.3f; public float CarryHeight => carryHeight;
    [Header("Carry Anchor Offset (손 앵커 위치 오프셋)")]
    [Tooltip("체크하면 이 아이템을 들 때 RagdollPoser 기본 오프셋 대신 아래 값을 사용합니다.")]
    [SerializeField] private bool overrideCarryAnchorOffset = false;
    [Tooltip("CarryAnchor 기준 오프셋 (X:좌우, Y:위아래, Z:앞뒤)")]
    [SerializeField] private Vector3 carryAnchorOffset = new Vector3(0f, 0.1f, 0.3f);
    private PlayerInteraction _holder;
    public PlayerInteraction Holder => _holder;
    public PlayerInteraction LastHolder { get; private set; }

    // 들고있는 여부를 확인하기 위함
    private readonly NetworkVariable<bool> _isHeldNetworked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    public bool IsHeld => _isHeldNetworked.Value;

    // 캐싱용 -> 오브젝트 비활성화 대신 렌더랑 캔버스를 끄는 방식으로 처리하기 위한 변수
    private MeshRenderer[] _renderers;
    private Canvas[] _canvases;
    private IPickupListener _pickupListener;
    private Vector3 _originalLocalScale;
    private NetworkTransform _networkTransform;
    private RagdollPoser _holderRagdollPoser; // 현재 들고 있는 사람의 자세 제어기 (양손 들기 요청/해제용)
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isVisible.OnValueChanged += OnVisibilityChanged;
        UpdateActualVisibility(_isVisible.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isVisible.OnValueChanged -= OnVisibilityChanged;
        base.OnNetworkDespawn();
        gameObject.SetActive(false);
    }

    private readonly NetworkVariable<bool> _isVisible = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        if (_itemRigidbody == null) _itemRigidbody = GetComponent<Rigidbody>();
        if (itemCollider == null) itemCollider = GetComponent<Collider>();
        if (pickupHighlight == null) pickupHighlight = GetComponent<PickupHighlight>();

        if (_itemRigidbody != null)
        {
            _itemRigidbody.isKinematic = false;
            _itemRigidbody.useGravity = true;
        }
        GetComponent<NetworkObject>().AutoObjectParentSync = false; // 부모 동기화는 직접 RPC로 처리 (플레이어를 따라다니게 하기 위함)
        _originalLocalScale = transform.localScale;
        _renderers = GetComponentsInChildren<MeshRenderer>();
        _canvases = GetComponentsInChildren<Canvas>(true);
        TryGetComponent(out _pickupListener);
        TryGetComponent(out _networkTransform);
        TryGetComponent(out _trashObject);
    }

    public void SetHighlighted(bool highlighted)
    {
        if (pickupHighlight != null)
        {
            pickupHighlight.SetHighlighted(highlighted);
        }
    }

    public bool CanBePickedUpBy(PlayerInteraction playerInteraction)
    {
        if (!IsServer || playerInteraction == null || _holder != null)
        {
            return false;
        }

        float distance = Vector3.Distance(transform.position, playerInteraction.transform.position);
        return distance <= 5.5f;
    }

    public void Pickup(PlayerInteraction playerInteraction)
    {
        if (!IsServer || playerInteraction == null || _holder != null)
        {
            return;
        }
        LastHolder = playerInteraction;
        _pickupListener?.OnPickedUp();
        _holder = playerInteraction;
        _isHeldNetworked.Value = true;
        SetHighlighted(false);
        NetworkObject.ChangeOwnership(playerInteraction.OwnerClientId);

        // 홀더의 RagdollPoser를 찾아서 양손 들기 자세 요청, (SetCarryRequested는 IsServerAuthoritative 인스턴스에서만 실제로 팔을 구동하므로 서버에서 직접 호출해도 안전)
        _holderRagdollPoser = playerInteraction.playerRagDollPoser;
        _holderRagdollPoser?.SetCarryRequested(true);
        
        // 이 오브젝트의 그립을 홀더에 적용
        if (TryGetComponent<CarryGripPoints>(out var gripPoints))
        {
            _holderRagdollPoser?.SetCarryTarget(gripPoints);
        }

        AttachToHolderClientRpc(playerInteraction.NetworkObjectId);
    }

    public void Drop()
    {
        if (!IsServer || _holder == null)
        {
            return;
        }
        ulong throwerId = _holder.NetworkObjectId;
        SetVisibility(true);
        if (_holder.TryGetComponent<PlayerInventory>(out var inventory))
        {
            inventory.ClearItemFromSlots(this);
        }

        DropPos();

        DetachFromHolderClientRpc(throwerId, Vector3.zero, 0f, Vector3.zero);
    }

    [ClientRpc]
    private void AttachToHolderClientRpc(ulong holderNetId)
    {
        // 컴포넌트 초기화 타이밍을 확보하기 위해 코루틴으로 실행
        StartCoroutine(AttachToHolderCoroutine(holderNetId));
    }

    private IEnumerator AttachToHolderCoroutine(ulong holderNetId)
    {
        if (_networkTransform != null) _networkTransform.enabled = false;

        if (NetworkObject != null && !NetworkObject.IsSpawned)
        {
            yield return new WaitUntil(() => NetworkObject.IsSpawned);
        }
        yield return null;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(holderNetId, out var holderNetObj))
        {
            // 1. 물리 완전 비활성화
            if (_itemRigidbody != null)
            {
                _itemRigidbody.linearVelocity = Vector3.zero;
                _itemRigidbody.angularVelocity = Vector3.zero;
                _itemRigidbody.isKinematic = true;
                _itemRigidbody.useGravity = false;
            }

            if (itemCollider != null)
            {
                itemCollider.enabled = false;
            }

            Transform targetParent = holderNetObj.transform;
            RagdollPoser ragdollPoser = null;

            if (holderNetObj.TryGetComponent<RagdollNetworkSync>(out var ragdollSync))
            {
                ragdollPoser = ragdollSync.Poser;
            }

            bool attachedToHand = false;

            if (ragdollPoser != null && ragdollPoser.CarryAnchor != null)
            {
                // 래그돌 관절 뼈 속으로 직접 자식을 넣는 대신 CarryAnchor를 타겟으로 지정
                targetParent = ragdollPoser.CarryAnchor;
                attachedToHand = true;
                // 이 아이템 전용 오프셋이 있다면 적용
                if (overrideCarryAnchorOffset)
                {
                    ragdollPoser.SetCarryAnchorOffset(carryAnchorOffset);
                }
            }
            else if (holderNetObj.TryGetComponent<FirstPersonLook>(out var playerCamera) && playerCamera.PlayerCameraTransform != null)
            {
                targetParent = playerCamera.PlayerCameraTransform;
            }

            // 2. 부모 설정
            transform.SetParent(targetParent, false);

            // 3. 트랜스폼 초기화
            if (attachedToHand)
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
            else
            {
                transform.localPosition = new Vector3(0, carryHeight, carryDistance);
                transform.localRotation = Quaternion.identity;
            }

            // 스케일 보정
            Vector3 parentScale = targetParent.lossyScale;
            transform.localScale = new Vector3(
                _originalLocalScale.x / Mathf.Max(parentScale.x, 0.0001f),
                _originalLocalScale.y / Mathf.Max(parentScale.y, 0.0001f),
                _originalLocalScale.z / Mathf.Max(parentScale.z, 0.0001f)
            );
        }
    }

    [ClientRpc]
    private void DetachFromHolderClientRpc(ulong throwerNetId, Vector3 direction, float force, Vector3 torque)
    {
        StartCoroutine(DetachRoutine(throwerNetId, direction, force, torque));
    }

    private IEnumerator DetachRoutine(ulong throwerNetId, Vector3 direction, float force, Vector3 torque)
    {
        // 1. 부모를 끊기 전에 던진 사람의 위치를 기반으로 던지기 시작 월드 좌표를 먼저 계산해 두기
        Vector3 targetWorldPos = transform.position;
        Quaternion targetWorldRot = transform.rotation;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(throwerNetId, out var throwerNetObj))
        {
            Transform throwerTransform = throwerNetObj.transform;
            if (throwerNetObj.TryGetComponent<FirstPersonLook>(out var player) && player.PlayerCameraTransform != null)
            {
                throwerTransform = player.PlayerCameraTransform.parent != null ? player.PlayerCameraTransform.parent : player.PlayerCameraTransform;
            }

            // 던진 사람 손 앞의 정확한 월드 좌표 도출
            targetWorldPos = throwerTransform.TransformPoint(new Vector3(0, carryHeight, carryDistance));
            targetWorldRot = throwerTransform.rotation;
            // 손 앞에서 시작하는 이유: NetworkObject를 비활성화한 상태라 동기화 지연 때문에 순간이동하는 것처럼 보이기 때문

            // 오프셋을 기본값으로 복원 (다음 아이템이 기본 오프셋을 쓸 수 있도록)
            if (overrideCarryAnchorOffset && throwerNetObj.TryGetComponent<RagdollNetworkSync>(out var ragdollSync))
            {
                ragdollSync.Poser?.ResetCarryAnchorOffset();
            }
        }

        // 2. 부모를 해제하고 스케일을 복원
        transform.SetParent(null);
        transform.localScale = _originalLocalScale;

        // 3. 해제되면서 튄 좌표를 1에서 계산한 시작 지점으로 강제 고정
        transform.position = targetWorldPos;
        transform.rotation = targetWorldRot;

        // 4. 물리 및 콜라이더 계산을 먼저 재개하여 클라이언트가 즉시 날아갈 준비
        if (itemCollider != null)
        {
            itemCollider.enabled = true;
        }

        if (_itemRigidbody != null)
        {
            _itemRigidbody.isKinematic = false;
            _itemRigidbody.useGravity = true;
            _itemRigidbody.linearVelocity = Vector3.zero;
            _itemRigidbody.angularVelocity = Vector3.zero;

            // 클라이언트 화면에서 즉시 물리 힘을 주어 랙 없이 발사
            if (force > 0f)
            {
                Vector3 finalDirection = (direction + Vector3.up * 0.15f).normalized;
                _itemRigidbody.AddForce(finalDirection * force, ForceMode.Impulse);
                _itemRigidbody.AddTorque(torque, ForceMode.Impulse);
            }
        }

        // NetworkTransform이 켜지자마자 이전 위치 버퍼로 강제 회귀(순간이동)시키는 현상을 막기 위한 1프레임 대기
        yield return null;

        // 6. 물리 작동이 시작된 후 안전하게 NetworkTransform을 켜서 서버 패킷 동기화
        if (_networkTransform != null)
        {
            _networkTransform.enabled = true;

            // 권한이 있는 호스트/서버 측이라면 텔레포트 최종 확정
            if (_networkTransform.CanCommitToTransform)
            {
                _networkTransform.Teleport(transform.position, transform.rotation, transform.localScale);
            }
        }
    }

    private void DropPos()
    {
        _pickupListener?.OnDropped();
        _holder = null;
        _isHeldNetworked.Value = false;
        NetworkObject.RemoveOwnership();

        //들기 자세 해제
        _holderRagdollPoser?.SetCarryRequested(false);
        _holderRagdollPoser?.SetCarryTarget(null);
        _holderRagdollPoser = null;
    }

    public void SetVisibility(bool visible)
    {
        if (!IsServer) return;

        if (_isVisible.Value != visible)
        {
            _isVisible.Value = visible;
        }
    }

    private void OnVisibilityChanged(bool previousValue, bool newValue)
    {
        UpdateActualVisibility(newValue);
    }

    private void UpdateActualVisibility(bool visible)
    {
        if (_renderers != null)
        {
            foreach (var rdr in _renderers)
            {
                if (rdr != null) rdr.enabled = visible;
            }
        }

        if (_canvases != null)
        {
            foreach (var canvas in _canvases)
            {
                if (canvas != null) canvas.enabled = visible;
            }
        }

        if (itemCollider != null && _holder == null)
        {
            itemCollider.enabled = visible;
        }

        if (!visible)
        {
            SetHighlighted(false);
        }
    }

    /// <summary>
    /// 아이템 던지기
    /// </summary>
    public void ThrowFromServer(Vector3 direction, float force, Vector3 torque, ulong throwerNetId)
    {
        if (!IsServer) return;

        DropPos();

        // 1. 모든 클라이언트의 자식 관계를 끊음
        DetachFromHolderClientRpc(throwerNetId, direction, force, torque);

        // 2. 서버 및 호스트 클라이언트에서 즉시 물리 힘 전달
        if (_itemRigidbody != null)
        {
            // DetachFromHolderClientRpc가 불려도 서버에서는 동기화 순서 때문에 
            // 이 타이밍에 바로 힘을 주려면 물리 세팅을 한 번 더 확정해주는 게 안전함
            _itemRigidbody.isKinematic = false;
            _itemRigidbody.useGravity = true;
            itemCollider.enabled = true;    

            if(force > 0)
            {
                // 정면 방향에 위쪽 보정을 섞어 포물선으로 날아가게 한다
                Vector3 finalDirection = (direction + Vector3.up * 0.15f).normalized;
                _itemRigidbody.AddForce(finalDirection * force, ForceMode.Impulse);
                _itemRigidbody.AddTorque(torque, ForceMode.Impulse);
            }
            else
            {
                _itemRigidbody.linearVelocity = Vector3.zero;
                _itemRigidbody.angularVelocity = Vector3.zero;
            }
        }
    }
}