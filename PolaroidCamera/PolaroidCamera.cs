using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 조준(Aim) + 캡처(Capture) 가능한 도구가 구현하는 계약.
/// 플레이어 쪽 holdItem 스크립트는 이 인터페이스만 알면 되고,
/// PolaroidCamera 같은 구체 클래스 이름을 직접 알 필요가 없다.
/// </summary>
public interface ICameraTool
{
    void Aim(bool isAiming);
    void Capture();
}

/// <summary>
/// 폴라로이드 카메라 픽업 아이템에 부착하는 보조 컴포넌트.
/// 위치/자세 관련 로직은 전혀 다루지 않는다 (그건 별도로 처리한다고 하셨으므로 제외).
/// 이 스크립트가 하는 일은 딱 두 가지:
///   1) 조준 중일 때만 뷰파인더 카메라를 켜서 실시간 렌더링
///   2) 캡처 시점의 화면을 찍어서 Photo 프리팹으로 스폰
/// </summary>
[RequireComponent(typeof(PickupItem))]
public class PolaroidCamera : NetworkBehaviour, ICameraTool, IPickupListener
{
    [Header("References")]
    [Tooltip("뷰파인더 화면에 실시간으로 그려주는 자식 카메라 (구멍 뒤 스크린용)")]
    [SerializeField] private Camera viewfinderCamera;
    [Tooltip("viewfinderCamera가 렌더링할 RenderTexture. 구멍 뒤 스크린 머테리얼에도 동일한 텍스처를 연결해둘 것")]
    [SerializeField] private RenderTexture viewfinderRT;
    [Tooltip("스폰할 사진 프리팹")]
    [SerializeField] private GameObject photoPrefab;
    [Tooltip("사진이 튀어나오는 위치/방향")]
    [SerializeField] private Transform photoEjectPoint;

    // 남은 수 카운트
    [SerializeField] private TextMeshProUGUI remainingPhotosText;

    // ===== 설정값 =====
    private int captureResolution = 512;
    [Range(1, 100)]
    private int jpgQuality = 60;
    private float ejectForce = 1.5f;

    // 카메라 에임 시 위치 조정
    [SerializeField] private float carryDistanceAim = 1f;
    [SerializeField] private float carryHeightAim = -0.1f;
    [SerializeField] private float carryRightOffsetAim = 0.1f;

    private const int MAX_JPG_BYTE_SIZE = 512 * 1024;

    // PickupItem.cs를 건드리지 않기 위해, 서버가 Holder를 폴링해서 여기 미러링한다.
    private readonly NetworkVariable<bool> _isEquipped = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // 남은 사진 수 카운팅
    private readonly NetworkVariable<int> _remainingPhotos = new NetworkVariable<int>(
        3,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<bool> _isAiming = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server // 수정은 서버만 가능
    );

    private bool _isReparented = false;

    // 이제 변수가 아니라 NetworkVariable의 Value를 반환하도록 수정
    public bool IsAiming => _isAiming.Value && _isEquipped.Value;


    // 에임 시 로컬 하이어러키 위치
    private Transform _originalParent;
    private PickupItem _pickupItem;

    private void Awake()
    {
        _pickupItem = GetComponent<PickupItem>();

        if (viewfinderCamera != null)
        {
            viewfinderCamera.targetTexture = viewfinderRT;
            viewfinderCamera.enabled = false; // 조준 중에만 렌더링 (성능 절약)
        }
        _remainingPhotos.OnValueChanged += OnRemainingPhotosChanged;
        UpdateRemainingPhotosUI();
        _isAiming.OnValueChanged += OnAimingChanged;
    }

    public override void OnNetworkDespawn()
    {
        _remainingPhotos.OnValueChanged -= OnRemainingPhotosChanged;
        _isAiming.OnValueChanged -= OnAimingChanged;
    }

    private void Update()
    {
        // PickupItem.cs 수정 없이 held 상태를 얻기 위한 서버 측 폴링.
        // Holder는 서버 인스턴스에서만 정확하므로, 여기서 읽어 NetworkVariable로 미러링한다.
        if (IsServer)
        {
            bool actuallyHeld = _pickupItem.Holder != null;
            if (_isEquipped.Value != actuallyHeld)
            {
                _isEquipped.Value = actuallyHeld;
            }
        }
    }

    // IPickupListener 구현 - PickupItem이 집히거나 내려놓을 때 호출됨
    public void OnPickedUp()
    {
        // 없음
    }

    public void OnDropped()
    {
        if (IsOwner)
        {
            DetachFromCameraAnchor();
        }
        if (!IsServer) return;

        // Drop 시 조준 상태를 서버 권위로 강제 리셋.
        // 이 한 줄만으로 모든 클라이언트의 _isAiming.OnValueChanged가 트리거되어
        // 뷰파인더 카메라 off + 텍스처 클리어까지 자동으로 동기화됩니다.
        if (_isAiming.Value)
        {
            _isAiming.Value = false;
        }
    }

    private void DetachFromCameraAnchor()
    {
        // 조준 중(카메라 부모 상태)에 버려졌거나, 부모가 임시 변경된 상태일 때 복구
        if (!_isReparented && transform.parent != _originalParent)
        {
            // 백업된 _originalParent가 존재한다면 복구
            if (_originalParent != null)
            {
                transform.SetParent(_originalParent);
            }
        }

        if (TryGetComponent(out Rigidbody rb))
        {
            // 내려놓은 상태이므로 kinematic 해제 및 물리 활성화
            rb.isKinematic = !_pickupItem.Holder;
        }
        _isReparented = false;
        _originalParent = null;
    }
    // ===== ICameraTool 구현 - holdItem이 든 아이템에서 이 인터페이스를 찾아 직접 호출 =====

    public void Aim(bool isAiming)
    {
        if (!_isEquipped.Value) return;

        SetAimingStateServerRpc(isAiming); // 여기서 위치는 건드리지 않음, 상태 요청만

        if (viewfinderCamera != null)
        {
            viewfinderCamera.enabled = isAiming;
            if (!isAiming) ClearViewfinderTexture();
        }
    }
    public void Capture()
    {
        if (!IsOwner || !_isEquipped.Value || !_isAiming.Value) return;

        if (_remainingPhotos.Value <= 0)
        {
            Debug.Log("PolaroidCamera: 남은 사진이 없습니다.");
            return;
        }

        TakePhoto();
    }

    private void TakePhoto()
    {
        if (viewfinderCamera == null || viewfinderRT == null)
        {
            Debug.LogWarning("PolaroidCamera: viewfinderCamera 또는 viewfinderRT가 비어있습니다.");
            return;
        }

        // 클릭한 그 순간의 화면을 확실히 얻기 위해 강제로 한 프레임 즉시 렌더링
        viewfinderCamera.Render();

        byte[] jpgBytes = CaptureRenderTextureToJpg(viewfinderRT, captureResolution, jpgQuality);

        Vector3 spawnPos = photoEjectPoint != null ? photoEjectPoint.position : transform.position;
        Quaternion spawnRot = photoEjectPoint != null ? photoEjectPoint.rotation : transform.rotation;
        Vector3 ejectVelocity = (photoEjectPoint != null ? photoEjectPoint.forward : transform.forward) * ejectForce
                                 + Vector3.up * 0.5f;

        RequestSpawnPhotoServerRpc(spawnPos, spawnRot, ejectVelocity, jpgBytes);
    }

    /// <summary>
    /// RenderTexture를 읽어서 지정된 해상도로 리사이즈 후 JPG 바이트로 인코딩한다.
    /// </summary>
    private static byte[] CaptureRenderTextureToJpg(RenderTexture source, int targetSize, int quality)
    {
        RenderTexture prevActive = RenderTexture.active;

        RenderTexture resized = RenderTexture.GetTemporary(targetSize, targetSize, 0, source.format);
        // RenderTexture를 리사이즈. Graphics.Blit(source, resized)로 하면 source의 크기와 상관없이 resized에 맞게 스케일링됨.
        Graphics.Blit(source, resized);

        RenderTexture.active = resized;
        var tex = new Texture2D(targetSize, targetSize, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, targetSize, targetSize), 0, 0);
        tex.Apply();

        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(resized);

        byte[] jpgBytes = tex.EncodeToJPG(quality);
        Destroy(tex);

        return jpgBytes;
    }

    [ServerRpc]
    private void RequestSpawnPhotoServerRpc(Vector3 position, Quaternion rotation, Vector3 velocity, byte[] jpgBytes)
    {
        if (_remainingPhotos.Value <= 0) return;

        if (jpgBytes == null || jpgBytes.Length > MAX_JPG_BYTE_SIZE)
        {
            Debug.LogWarning($"PolaroidCamera: JPG 바이트 크기가 최대 허용 크기({MAX_JPG_BYTE_SIZE})를 초과했습니다. 사진 스폰을 건너뜁니다.");
            return;
        }

        if (photoPrefab == null)
        {
            Debug.LogError("PolaroidCamera: photoPrefab이 할당되지 않았습니다.");
            return;
        }
        _remainingPhotos.Value--;

        GameObject photoObj = Instantiate(photoPrefab, position, rotation);
        NetworkObject photoNetObj = photoObj.GetComponent<NetworkObject>();
        photoNetObj.Spawn(true);

        if (photoObj.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = velocity;
        }

        Photo photo = photoObj.GetComponent<Photo>();
        photo.ApplyPhotoTextureClientRpc(jpgBytes);
    }

    

    [ServerRpc]
    private void SetAimingStateServerRpc(bool isAiming)
    {
        // 서버에서 값을 변경하면, 전 세계 모든 클라이언트의 _isAiming.Value가 동기화됩니다.
        _isAiming.Value = isAiming;
    }
    private void OnAimingChanged(bool previousValue, bool newValue)
    {
        if (viewfinderCamera != null)
        {
            viewfinderCamera.enabled = newValue;
            if (!newValue) ClearViewfinderTexture();
        }

        // 1. 부모 전환 및 위치 적용
        UpdateParentAndTransform(newValue);

        // 2. 카메라 Near Clip Plane 조정
        ApplyCameraClip(newValue);
    }

    private void UpdateParentAndTransform(bool isAiming)
    {
        if (_pickupItem == null || _pickupItem.Holder == null) return;

        var holderNetObj = _pickupItem.Holder.NetworkObject;
        if (holderNetObj == null) return;

        if (isAiming)
        {
            // [조준 시] 부모를 1인칭 카메라로 변경하여 회전/이동을 즉시 완벽 추종
            if (holderNetObj.TryGetComponent<FirstPersonLook>(out var playerCamera) &&
                playerCamera.PlayerCameraTransform != null)
            {
                if (!_isReparented)
                {
                    _originalParent = transform.parent;
                    _isReparented = true; // 부모 변경 플래그 켬
                }

                transform.SetParent(playerCamera.PlayerCameraTransform, false);
                transform.localPosition = new Vector3(carryRightOffsetAim, carryHeightAim, carryDistanceAim);
                transform.localRotation = Quaternion.identity;
            }
        }
        else
        {
            // [조준 해제 시] 다시 래그돌 CarryAnchor 자식으로 되돌림
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
                // 앵커 기준 (0,0,0) 위치 및 회전 강제 초기화
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }

            _isReparented = false;
        }
    }

    private void UpdateRemainingPhotosUI()
    {
        if (remainingPhotosText != null)
        {
            remainingPhotosText.text = _remainingPhotos.Value.ToString();
        }
    }

    private void OnRemainingPhotosChanged(int previousValue, int newValue)
    {
        UpdateRemainingPhotosUI();
    }

    private void ApplyCameraClip(bool isAiming)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

        var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayerObj == null || !localPlayerObj.TryGetComponent<FirstPersonLook>(out var look)) return;

        if (isAiming) look.SetClipNear();
        else look.SetClipOrigin();
    }


    /// <summary>
    /// 뷰파인더 RenderTexture를 검은 화면으로 초기화합니다.
    /// 카메라가 꺼져도 RenderTexture 자체는 마지막 프레임을 계속 들고 있기 때문에 명시적으로 지워줘야 함.
    /// </summary>
    private void ClearViewfinderTexture()
    {
        if (viewfinderRT == null) return;

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture.active = viewfinderRT;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = previousActive;
    }

}