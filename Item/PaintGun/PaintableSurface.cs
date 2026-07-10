using UnityEngine;

/// <summary>
/// 페인트가 묻을 수 있는 표면(벽, 바닥 등)에 부착하는 컴포넌트.
/// 원본 렌더러/머테리얼은 절대 건드리지 않는다.
/// 대신 같은 메시를 쓰는 투명 오버레이 자식 오브젝트를 런타임에 자동 생성해서
/// 그 위에 페인트 마스크를 덧그린다 (원본 셰이더/텍스처/노멀맵 등 그대로 유지).
/// 반드시 MeshCollider를 사용해야 함 (hit.textureCoord를 얻기 위해).
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
public class PaintableSurface : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("씬 내에서 유일해야 하는 표면 ID. 수동으로 겹치지 않게 부여할 것.")]
    [SerializeField] private int surfaceId;

    [Header("Canvas Settings")]
    [SerializeField] private int textureSize = 1024;

    [Header("Overlay Settings")]
    [Tooltip("Custom/PaintOverlayURP 셰이더. 원본 머테리얼 위에 덧그려지는 투명 레이어용.")]
    [SerializeField] private Shader overlayShader;
    [SerializeField] private Color paintColor = new Color(0.8f, 0.05f, 0.05f, 1f);
    [Tooltip("원본 표면과 겹칠 때 Z-fighting을 막기 위한 노멀 방향 오프셋")]
    [SerializeField] private float normalOffset = 0.001f;

    public int SurfaceId => surfaceId;
    public RenderTexture Canvas { get; private set; }

    private const string MaskPropertyName = "_PaintMask";

    public float ContaminationPercent { get; set; } = 0f;
    private void Awake()
    {
        Canvas = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.R8)
        {
            name = $"PaintCanvas_{surfaceId}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
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

        var overlayObj = new GameObject($"PaintOverlay_{surfaceId}");
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
        PaintSurfaceManager.Instance.Register(surfaceId, this);
    }

    private void OnDestroy()
    {
        if (Canvas != null)
        {
            Canvas.Release();
            Canvas = null;
        }
        PaintSurfaceManager.Instance?.Unregister(surfaceId);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (surfaceId < 0)
        {
            Debug.LogWarning($"{name}: surfaceId는 0 이상이어야 합니다.", this);
        }
    }
#endif
}