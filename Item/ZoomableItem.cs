using NUnit.Framework.Interfaces;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PickupItem))]
public class ZoomableItem : NetworkBehaviour, IZoomTool, IPickupListener
{
    [Header("Aim Local Transform (조준 시 위치/회전)")]
    [Tooltip("카메라 전방 거리")]
    [SerializeField] private float carryDistanceAim = 0.4f;
    [Tooltip("카메라 높이 오프셋")]
    [SerializeField] private float carryHeightAim = -0.05f;
    [Tooltip("카메라 좌우 오프셋")]
    [SerializeField] private float carryRightOffsetAim = 0.0f;
    [Tooltip("조준 시 회전 오프셋 (Inspector에서 각도 조정)")]
    [SerializeField] private Vector3 aimRotationOffset = Vector3.zero;

    private readonly NetworkVariable<bool> _isEquipped = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isAiming = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsAiming => _isAiming.Value && _isEquipped.Value;

    private PickupItem _pickupItem;

    public event System.Action<bool> OnAimingStateChanged; // 줌 했을 때 이벤트

    private void Awake()
    {
        _pickupItem = GetComponent<PickupItem>();
        _isAiming.OnValueChanged += OnAimingChanged;
    }

    public override void OnNetworkDespawn()
    {
        _isAiming.OnValueChanged -= OnAimingChanged;
    }

    private void Update()
    {
        // 들고 있는지 상태를 서버에서 폴링
        if (IsServer)
        {
            bool actuallyHeld = _pickupItem.Holder != null;
            if (_isEquipped.Value != actuallyHeld)
            {
                _isEquipped.Value = actuallyHeld;
            }
        }
    }

    // IPickupListener 구현
    public void OnPickedUp() { }

    public void OnDropped()
    {
        if (!IsServer) return;

        // 버릴 때 조준 상태 강제 리셋
        if (_isAiming.Value)
        {
            _isAiming.Value = false;
        }
    }

    // ICameraTool 구현 (플레이어의 Hold/Input 스크립트에서 우클릭 시 호출)
    public void Aim(bool isAiming)
    {
        if (!_isEquipped.Value) return;

        SetAimingStateServerRpc(isAiming);
    }

    [ServerRpc]
    private void SetAimingStateServerRpc(bool isAiming)
    {
        _isAiming.Value = isAiming;
    }

    private void OnAimingChanged(bool previousValue, bool newValue)
    {
        OnAimingStateChanged?.Invoke(newValue); // 추가

        if (!IsOwner) return;

        // 1. 부모 전환 및 위치/회전 적용
        UpdateParentAndTransform(newValue);

        // 2. 카메라 Clip Plane 조정 (카메라에 너무 가까워 파묻히는 현상 방지)
        ApplyCameraClip(newValue);
    }

    private void UpdateParentAndTransform(bool isAiming)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

        NetworkObject holderNetObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (holderNetObj == null) return;

        if (isAiming)
        {
            // [조준 시] 1인칭 카메라 자식으로 변경 및 정면 위치/회전 배치
            if (holderNetObj.TryGetComponent<FirstPersonLook>(out var playerCamera) &&
                playerCamera.PlayerCameraTransform != null)
            {
                transform.SetParent(playerCamera.PlayerCameraTransform, false);
                transform.localPosition = new Vector3(carryRightOffsetAim, carryHeightAim, carryDistanceAim);
                transform.localRotation = Quaternion.Euler(aimRotationOffset);
            }
        }
        else
        {
            // [조준 해제 시] 래그돌 CarryAnchor 자식으로 복귀 및 (0,0,0) 초기화
            Transform targetParent = null;

            if (holderNetObj.TryGetComponent<RagdollNetworkSync>(out var ragdollSync) &&
                ragdollSync.Poser != null && ragdollSync.Poser.CarryAnchor != null)
            {
                targetParent = ragdollSync.Poser.CarryAnchor;
            }
            else if (holderNetObj.TryGetComponent<FirstPersonLook>(out var playerCam) &&
                     playerCam.PlayerCameraTransform != null)
            {
                targetParent = playerCam.PlayerCameraTransform;
            }

            if (targetParent != null)
            {
                transform.SetParent(targetParent, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
        }
    }

    private void ApplyCameraClip(bool isAiming)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

        var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayerObj == null || !localPlayerObj.TryGetComponent<FirstPersonLook>(out var look)) return;

        if (isAiming) look.SetClipNear();
        else look.SetClipOrigin();
    }
}