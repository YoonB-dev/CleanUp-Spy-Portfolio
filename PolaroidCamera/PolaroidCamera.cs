using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 폴라로이드 카메라 픽업 아이템에 부착하는 보조 컴포넌트.
/// 위치/자세 관련 로직은 전혀 다루지 않는다 (그건 별도로 처리한다고 하셨으므로 제외).
/// 이 스크립트가 하는 일은 딱 두 가지:
///   1) 조준 중일 때만 뷰파인더 카메라를 켜서 실시간 렌더링
///   2) 캡처 시점의 화면을 찍어서 Photo 프리팹으로 스폰
/// </summary>
[RequireComponent(typeof(PickupItem))]
[RequireComponent(typeof(ZoomableItem))]
public class PolaroidCamera : NetworkBehaviour, ICaptureTool, IPickupListener
{
    [Header("References")]
    [Tooltip("뷰파인더 화면에 실시간으로 그려주는 자식 카메라 (구멍 뒤 스크린용)")]
    [SerializeField] private Camera viewfinderCamera;
    [Tooltip("뷰파인더 RenderTexture 원본. 런타임엔 이 설정을 복사한 카메라 전용 RT를 만들어 쓴다")]
    [SerializeField] private RenderTexture viewfinderRT;
    [Tooltip("뷰파인더 스크린 렌더러. 인스턴스마다 전용 RT를 꽂아준다")]
    [SerializeField] private Renderer viewfinderRenderer;
    [SerializeField] private string viewfinderTextureProperty = "_BaseMap";
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
    private ZoomableItem _zoomableItem;
    private Material _viewfinderMaterial;
    private void Awake()
    {
        _pickupItem = GetComponent<PickupItem>();
        _zoomableItem = GetComponent<ZoomableItem>();

        // RT 에셋은 모든 카메라가 공유하므로, 설정만 복사해 이 카메라 전용 RT를 만든다
        if (viewfinderRT != null)
        {
            viewfinderRT = new RenderTexture(viewfinderRT.descriptor) { name = $"{name}_Viewfinder" };
            viewfinderRT.Create();
        }

        if (viewfinderRenderer != null)
        {
            // .material은 이 렌더러 전용 복사본을 만든다
            _viewfinderMaterial = viewfinderRenderer.material;
            _viewfinderMaterial.SetTexture(viewfinderTextureProperty, viewfinderRT);
        }

        if (viewfinderCamera != null)
        {
            viewfinderCamera.targetTexture = viewfinderRT;
            viewfinderCamera.enabled = false; // 조준 중에만 렌더링 (성능 절약)
        }
        ClearViewfinderTexture(); // 새 RT는 내용이 비정의 상태라 검게 초기화
        _remainingPhotos.OnValueChanged += OnRemainingPhotosChanged;
        UpdateRemainingPhotosUI();

        if (_zoomableItem != null) {_zoomableItem.OnAimingStateChanged += OnZoomAimingChanged; }
    }

    public override void OnNetworkDespawn()
    {
        _remainingPhotos.OnValueChanged -= OnRemainingPhotosChanged;
    }

    public override void OnDestroy()
    {
        // 런타임에 만든 RT와 머테리얼 복사본은 자동으로 해제되지 않는다
        if (viewfinderCamera != null) viewfinderCamera.targetTexture = null;
        if (viewfinderRT != null)
        {
            viewfinderRT.Release();
            Destroy(viewfinderRT);
        }
        if (_viewfinderMaterial != null) Destroy(_viewfinderMaterial);

        base.OnDestroy();
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

    private void OnZoomAimingChanged(bool isAiming)
    {
        if (viewfinderCamera != null)
        {
            viewfinderCamera.enabled = isAiming;
            if (!isAiming) ClearViewfinderTexture();
        }
    }

    // IPickupListener 구현 - PickupItem이 집히거나 내려놓을 때 호출됨
    public void OnPickedUp()
    {
        // 없음
    }

    public void OnDropped()
    {
        if (viewfinderCamera != null)
        {
            viewfinderCamera.enabled = false;
            ClearViewfinderTexture();
        }
    }
    // ===== ICameraTool 구현 - holdItem이 든 아이템에서 이 인터페이스를 찾아 직접 호출 =====

    public void Capture()
    {
        if (!IsOwner || _zoomableItem == null || !_zoomableItem.IsAiming) return;

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