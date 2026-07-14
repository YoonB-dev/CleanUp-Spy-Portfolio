using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PickupHighlight))]
public class PickupItem : NetworkBehaviour
{
    private Rigidbody _itemRigidbody;
    private Collider itemCollider;
    private PickupHighlight pickupHighlight;
    private float carryDistance = 1.6f;
    private float carryHeight = -0.5f;

    // 카메라 에임 시 위치 조정 -> 이건 나중에 코드 분리할 여지가 있음
    private float carryDistanceAim = 0.8f; 
    private float carryHeightAim = 0.0f;

    private PlayerInteraction _holder; public PlayerInteraction Holder => _holder;

    private MeshRenderer[] _renderers; // 자식 오브젝트들까지 포함해서 다 끄기 위함
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // 네트워크 변수의 값이 변경될 때 실행될 콜백 함수 등록
        _isVisible.OnValueChanged += OnVisibilityChanged;
        UpdateActualVisibility(_isVisible.Value);
    }
    public override void OnNetworkDespawn()
    {
        _isVisible.OnValueChanged -= OnVisibilityChanged;
        base.OnNetworkDespawn();
        // 서버에서 연결이 끊기면 모든 클라이언트에서 아이템을 비활성화 -> 바로 삭제되는 방식은 문제 발생할 수 있다고 해서 약간 방어적으로 작성함.
        gameObject.SetActive(false);
    }
    // 아이템이 보이거나 안보이는 상태를 제어함.
    private readonly NetworkVariable<bool> _isVisible = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private void Awake()
    {
        if (_itemRigidbody == null)
        {
            _itemRigidbody = GetComponent<Rigidbody>();
        }

        if (itemCollider == null)
        {
            itemCollider = GetComponent<Collider>();
        }

        if (pickupHighlight == null)
        {
            pickupHighlight = GetComponent<PickupHighlight>();
        }

        if (_itemRigidbody != null)
        {
            _itemRigidbody.isKinematic = false;
            _itemRigidbody.useGravity = true;
        }

        _renderers = GetComponentsInChildren<MeshRenderer>();
    }

    public void SetHighlighted(bool highlighted)
    {
        if (pickupHighlight != null)
        {
            pickupHighlight.SetHighlighted(highlighted);
        }
    }

    private void Update()
    {
        if (_holder == null || (!IsServer && !_holder.IsOwner) || !_isVisible.Value)
        {
            return;
        }

        // 돌진중이면 스킵
        if (_holder.TryGetComponent<MafiaDashSkill>(out var dashSkill) && dashSkill.isDashing)
        {
            return;
        }

        

        if (_holder.TryGetComponent<FirstPersonLook>(out var player))
        {
            Transform camTransform = player.PlayerCameraTransform;

            if (camTransform != null)
            {
                // 카메라 사진 찍는거면 carryDistanceAim, carryHeightAim 적용 -> 이 부분도 나중에 코드 분리하긴 해야할듯
                if (TryGetComponent<PolaroidCamera>(out var polaroidCamera) && polaroidCamera.IsAiming)
                {
                    Vector3 followPositionAim = camTransform.position + camTransform.forward * carryDistanceAim + camTransform.up * carryHeightAim;
                    transform.position = followPositionAim;
                    transform.rotation = camTransform.rotation;
                    return;
                }
                // 2. 카메라의 정면(forward) 방향으로 carryDistance만큼 띄우고, 
                // 카메라 기준의 정중앙에 위치시키기 위해 약간 아래나 위로 조절하고 싶다면 camTransform.up을 활용.
                // (가운데 딱 맞추려면 Vector3.up * carryHeight 대신 살짝만 내리거나 0으로 두면 됨ㅇㅇ)
                Vector3 followPosition = camTransform.position + camTransform.forward * carryDistance + camTransform.up * carryHeight;

                transform.position = followPosition;

                // 3. 아이템의 회전도 카메라가 바라보는 회전과 일치시킨다.
                transform.rotation = camTransform.rotation;
            }
        }
        else
        {
            // 만약 플레이어 스크립트를 못 찾았다면 기존 백업 로직 수행
            Transform holderTransform = _holder.transform;
            Vector3 followPosition = holderTransform.position + holderTransform.forward * carryDistance + Vector3.up * carryHeight;
            transform.position = followPosition;
            transform.rotation = holderTransform.rotation;
        }
    }

    public bool CanBePickedUpBy(PlayerInteraction playerInteraction)
    {
        if (!IsServer || playerInteraction == null || _holder != null)
        {
            return false;
        }

        float distance = Vector3.Distance(transform.position, playerInteraction.transform.position);
        return distance <= 3.5f;
    }

    public void Pickup(PlayerInteraction playerInteraction)
    {
        if (!IsServer || playerInteraction == null || _holder != null)
        {
            return;
        }

        _holder = playerInteraction;
        SetHighlighted(false);
        NetworkObject.ChangeOwnership(playerInteraction.OwnerClientId);

        if (TryGetComponent<PlaceableBox>(out var placeableBox))
        {
            placeableBox.OnPickedUp(); // 이 안에서 Demolish()가 돌며 아랫장 연쇄 물리 연산 시동
        }

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
    }

    public void Drop()
    {
        if (!IsServer || _holder == null)
        {
            return;
        }

        _holder = null;
        NetworkObject.RemoveOwnership();

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
        }
    }

    public void SetVisibility(bool visible)
    {
        if (!IsServer) return;

        // 상태가 다를 때만 갱신하여 네트워크 패킷 낭비 방지
        if (_isVisible.Value != visible)
        {
            _isVisible.Value = visible;
        }
    }

    // 안보이게 처리
    private void OnVisibilityChanged(bool previousValue, bool newValue)
    {
        UpdateActualVisibility(newValue);
    }

    // 실제로 안보이게 처리하는 핵심 메서드
    private void UpdateActualVisibility(bool visible)
    {
        // 1. 모든 메쉬 렌더러 켜고 끄기
        if (_renderers != null)
        {
            foreach (var rdr in _renderers)
            {
                if (rdr != null) rdr.enabled = visible;
            }
        }

        // 2. 콜라이더 켜고 끄기 (들고 있을 때는 이미 꺼져있을 테니 Drop 상태에서 유용)
        if (itemCollider != null && _holder == null)
        {
            itemCollider.enabled = visible;
        }

        // 3. 하이라이트 기능도 꺼버리기
        if (!visible)
        {
            SetHighlighted(false);
        }
    }
}