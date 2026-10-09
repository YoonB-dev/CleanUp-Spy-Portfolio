using UnityEngine;

/// <summary>
/// 페인트가 묻을 수 있는 표면(벽, 바닥 등)에 부착하는 컴포넌트.
/// 원본 렌더러/머테리얼은 절대 건드리지 않는다.
/// 대신 같은 메시를 쓰는 투명 오버레이 자식 오브젝트를 런타임에 자동 생성해서
/// 그 위에 페인트 마스크를 덧그린다 (원본 셰이더/텍스처/노멀맵 등 그대로 유지).
/// 브러시는 레이가 맞은 월드 좌표 기준으로 그려지므로(PaintSurfaceManager.DrawAt) MeshCollider의 Convex 여부와 무관하다.
/// 단, 메시에 겹치지 않는 UV0가 있어야 칠한 자리가 다른 면에 같이 묻지 않는다.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
public class PaintableSurface : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("에디터 버튼을 통해 저장되는 유일 ID입니다.")]
    [SerializeField] private int surfaceId = -1; // [SerializeField] 필수!

    public int SurfaceId => surfaceId;

    [Header("Canvas Settings")]
    [SerializeField] private int textureSize = 1024;

    [Header("Overlay Settings")]
    [Tooltip("Custom/PaintOverlayURP 셰이더. 원본 머테리얼 위에 덧그려지는 투명 레이어용.")]
    [SerializeField] private Shader overlayShader;
    [SerializeField] private Color paintColor = new Color(0.8f, 0.05f, 0.05f, 1f);
    [Tooltip("원본 표면과 겹칠 때 Z-fighting을 막기 위한 노멀 방향 오프셋")]
    [SerializeField] private float normalOffset = 0.001f;
    public RenderTexture Canvas { get; private set; }

    private const string MaskPropertyName = "_PaintMask";

    public float ContaminationPercent { get; set; } = 0f;

    /// <summary>칠해진 실제 면적(m²). 서버가 페인트 마스크를 읽을 때 갱신. 페인트 점수는 이 값의 합으로 계산한다</summary>
    public float PaintedArea { get; set; } = 0f;

    /// <summary>페인트 마스크 1픽셀이 덮는 평균 월드 면적(m²). 등록 시 GPU로 한 번 측정하며, 0이면 아직 측정 전</summary>
    public float AreaPerPixel { get; set; } = 0f;
    private void Awake()
    {
        int dynamicSize = CalculateDynamicResolution();

        Canvas = new RenderTexture(dynamicSize, dynamicSize, 0, RenderTextureFormat.R8)
        {
            name = $"PaintCanvas_{SurfaceId}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
            autoGenerateMips = false
        };
        Canvas.Create();

        // 시작 시 완전히 깨끗한 상태(0)로 초기화
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = Canvas;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prev;

        CreateOverlayRenderer();
    }

    /// <summary>
    /// 메쉬의 실측 월드 크기를 기반으로 적절한 RenderTexture 해상도를 반환합니다.
    /// (Texel Density를 맞춰 바닥 깨짐 및 메모리 낭비 방지)
    /// </summary>
    private int CalculateDynamicResolution()
    {
        var filter = GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            return textureSize; // 예외 시 Inspector 기본값 사용

        // lossyScale의 음수 방지 (절대값)
        Vector3 lossy = transform.lossyScale;
        Vector3 absScale = new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z));

        // 메쉬의 실제 3D 월드 바운딩 박스 크기
        Vector3 boundsSize = filter.sharedMesh.bounds.size;
        Vector3 realWorldSize = Vector3.Scale(boundsSize, absScale);

        // 가장 긴 축의 실측 거리 (미터 단위)
        float maxWorldLength = Mathf.Max(realWorldSize.x, Mathf.Max(realWorldSize.y, realWorldSize.z));

        // 3D 크기 조건에 따른 동적 해상도 할당
        if (maxWorldLength > 25f) return 4096;      // 초대형 맵/거대 바닥
        if (maxWorldLength > 10f) return 2048;      // 대형 벽면, 넓은 바닥
        if (maxWorldLength > 3f) return 1024;      // 기둥, 일반 오브젝트
        return 512;                                 // 소형 상자, 작은 소품
    }

    /// <summary>
    /// 원본 메시와 동일한 자식 오브젝트를 만들어 투명 페인트 레이어로 사용한다.
    /// 원본 Renderer의 머테리얼은 전혀 건드리지 않는다.
    /// </summary>
    private void CreateOverlayRenderer()
    {
        if (overlayShader == null)
        {
            Debug.LogError($"{name}: overlayShader(Custom/PaintOverlayURP)가 할당되지 않았습니다.", this);
            return;
        }

        var overlayObj = new GameObject($"PaintOverlay_{SurfaceId}");
        overlayObj.transform.SetParent(transform, false);
        overlayObj.transform.localPosition = Vector3.zero;
        overlayObj.transform.localRotation = Quaternion.identity;
        overlayObj.transform.localScale = Vector3.one;

        Mesh sourceMesh = GetComponent<MeshFilter>().sharedMesh;

        var overlayFilter = overlayObj.AddComponent<MeshFilter>();
        overlayFilter.sharedMesh = sourceMesh;

        var overlayRenderer = overlayObj.AddComponent<MeshRenderer>();
        overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = false;

        var overlayMat = new Material(overlayShader);
        overlayMat.SetTexture(MaskPropertyName, Canvas);
        overlayMat.SetColor("_PaintColor", paintColor);
        overlayMat.SetFloat("_NormalOffset", normalOffset);

        // 원본 메시가 서브메시(멀티 머테리얼 슬롯)로 나뉘어 있는 경우를 대비해
        // 서브메시 개수만큼 같은 오버레이 머테리얼을 채워서 전체 표면이 빠짐없이 덮이도록 함
        int subMeshCount = Mathf.Max(1, sourceMesh.subMeshCount);
        var materials = new Material[subMeshCount];
        for (int i = 0; i < subMeshCount; i++)
        {
            materials[i] = overlayMat;
        }
        overlayRenderer.sharedMaterials = materials;
    }

    private void Start()
    {
        // Manager가 먼저 초기화되어 있어야 하므로 Start에서 등록
        if (PaintSurfaceManager.Instance == null)
        {
            Debug.LogError("PaintSurfaceManager가 씬에 없습니다. 등록 실패.");
            return;
        }
        PaintSurfaceManager.Instance.Register(SurfaceId, this);
    }

    private void OnDestroy()
    {
        if (Canvas != null)
        {
            Canvas.Release();
            Canvas = null;
        }
        PaintSurfaceManager.Instance?.Unregister(SurfaceId);
    }
}