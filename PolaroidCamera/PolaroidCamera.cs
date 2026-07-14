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
public class PolaroidCamera : NetworkBehaviour, ICameraTool, ICustomCarryTransform
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
    private float carryDistanceAim = 0.8f;
    private float carryHeightAim = 0.0f;

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

    private PickupItem _pickupItem;
    private readonly NetworkVariable<bool> _isAiming = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server // 수정은 서버만 가능
    );

    // 이제 변수가 아니라 NetworkVariable의 Value를 반환하도록 수정
    public bool IsAiming => _isAiming.Value && _isEquipped.Value;
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

    // ===== ICustomCarryTransform 구현 - PickupItem이 매 프레임 이걸 먼저 물어봄 =====

    public bool TryGetCarryTransform(Transform cameraTransform, out Vector3 position, out Quaternion rotation)
    {
        if (!IsAiming)
        {
            // 조준 중이 아니면 "나는 특수 위치를 원하지 않는다" -> PickupItem이 기본 로직 사용
            position = default;
            rotation = default;
            return false;
        }

        position = cameraTransform.position + cameraTransform.forward * carryDistanceAim + cameraTransform.up * carryHeightAim;
        rotation = cameraTransform.rotation;
        return true;
    }

    // ===== ICameraTool 구현 - holdItem이 든 아이템에서 이 인터페이스를 찾아 직접 호출 =====

    public void Aim(bool isAiming)
    {
        if (!IsOwner || !_isEquipped.Value) return;

        SetAimingStateServerRpc(isAiming);

        if (viewfinderCamera != null)
        {
            viewfinderCamera.enabled = isAiming;
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

    [ServerRpc]
    private void SetAimingStateServerRpc(bool isAiming)
    {
        // 서버에서 값을 변경하면, 전 세계 모든 클라이언트의 _isAiming.Value가 동기화됩니다.
        _isAiming.Value = isAiming;
    }
}