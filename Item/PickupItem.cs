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

    private Rigidbody _itemRigidbody;
    private Collider itemCollider;
    private PickupHighlight pickupHighlight;
    [Header("Carry Transform Settings")]
    // 던지기나 그런거에 사용되는 기본 위치
    [SerializeField] private float carryDistance = 1f; public float CarryDistance => carryDistance; 
    [SerializeField] private float carryHeight = -0.3f; public float CarryHeight => carryHeight;
    [Header("Carry Anchor Offset (손 앵커 위치 오프셋)")]
    [Tooltip("체크하면 이 아이템을 들 때 RagdollPoser 기본 오프셋 대신 아래 값을 사용합니다.")]
    [SerializeField] private bool overrideCarryAnchorOffset = false;
    [Tooltip("CarryAnchor 기준 오프셋 (X:좌우, Y:위아래, Z:앞뒤)")]
    [SerializeField] private Vector3 carryAnchorOffset = new Vector3(0f, 0.1f, 0.3f);
    private PlayerInteraction _holder;
    public PlayerInteraction Holder => _holder;

    // 외부(예: PolaroidCamera)에서 홀더가 들고 있는지 편하게 확인하기 위한 프로퍼티
    public bool IsHeld => _holder != null;

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
        GetComponent<NetworkObject>().AutoObjectParentSync = false; // 부모 동기화는 직접 RPC로 처리 -> 이걸로 플레이어 프리펩에 따라다니게 하려고 하기 위함
        _originalLocalScale = transform.localScale;
        _renderers = GetComponentsInChildren<MeshRenderer>();
        _canvases = GetComponentsInChildren<Canvas>(true);
        TryGetComponent(out _pickupListener);
        TryGetComponent(out _networkTransform);
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
        _pickupListener?.OnPickedUp();
        _holder = playerInteraction;
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

        // RPC를 통해 모든 클라이언트(특히 소유자 로격 클라이언트)에서 물리적 자식화를 수행합니다.
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

        // RPC를 통해 모든 클라이언트에서 자식 관계를 해제하고 월드로 내보냅니다.
        DetachFromHolderClientRpc(throwerId, Vector3.zero, 0f, Vector3.zero);
    }

    [ClientRpc]
    private void AttachToHolderClientRpc(ulong holderNetId)
    {
        // 컴포넌트 초기화 타이밍을 확보하기 위해 코루틴 실행으로 수정.
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
        // 1. 부모를 끊기 전에 던진 사람의 위치를 기반으로 '가장 정확한 던지기 시작 월드 좌표'를 먼저 계산해 두기
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
            // 참고로 사람 손 앞으로 가는 이유는 동기화 지연 때문에 순간이동 하는 느낌이 들어서 그럼. networkobject를 비활성화 했기 때문임.ㅇㅇ

            // 오프셋을 기본값으로 복원 (다음 아이템이 기본 오프셋을 쓸 수 있도록)
            if (overrideCarryAnchorOffset && throwerNetObj.TryGetComponent<RagdollNetworkSync>(out var ragdollSync))
            {
                ragdollSync.Poser?.ResetCarryAnchorOffset();
            }
        }

        // 2. 이제 안전하게 부모를 해제하고 스케일을 복원
        transform.SetParent(null);
        transform.localScale = _originalLocalScale;

        // 3. 해제되면서 튄 좌표를 우리가 계산한 정확한 시작 지점으로 강제 고정
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

        // 이렇게 하면 NetworkTransform이 켜지자마자 이전 위치 버퍼로 강제 회귀(순간이동)시키는 현상을 완벽히 막기 위한 1프레임 대기
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

        // 1. 모든 클라이언트의 자식 관계를 끊음 (기존 RPC 재활용)
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
                // 정면 방향으로 살짝 위쪽(Vector3.up * 0.1f) 보정을 섞어주면 더 이쁘게 날아감 포물선을 그리면서!
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