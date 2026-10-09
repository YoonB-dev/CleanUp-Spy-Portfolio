using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 씬 내 모든 PaintableSurface를 등록/관리하며, 실제 브러시 연산을 수행하는 로컬 싱글톤.
/// 네트워크 오브젝트가 아님 - 각 클라이언트가 자기 로컬 인스턴스를 가지고 자기 화면만 그림.
/// </summary>
public class PaintSurfaceManager : MonoBehaviour
{
    public static PaintSurfaceManager Instance { get; private set; }

    [Header("Brush")]
    [SerializeField] private Shader brushShader; // Hidden/PaintBrush 셰이더 할당

    private static readonly int BrushPosId = Shader.PropertyToID("_BrushPos");
    private static readonly int BrushRadiusId = Shader.PropertyToID("_BrushRadius");
    private static readonly int BrushValueId = Shader.PropertyToID("_BrushValue");

    private const int BRUSH_PASS = 0;
    private const int AREA_PASS = 1;

    // 픽셀당 면적 측정용 임시 텍스처 크기. 평균값만 필요하므로 표면 해상도보다 작아도 충분
    private const int AREA_MAP_SIZE = 512;

    // 칠해진 면적이 이보다 작으면(눈에 안 보이는 잔여 얼룩) 0으로 취급 (m²)
    private const float MIN_PAINTED_AREA = 0.01f;

    private Material _brushMaterial;
    private CommandBuffer _brushCommands;
    private readonly Dictionary<int, PaintableSurface> _surfaces = new();
    private readonly HashSet<int> _readbackInFlight = new(); // 표면별로 이미 읽기 오쳥이 진행 중인지 추적.
    public IEnumerable<PaintableSurface> AllSurfaces => _surfaces.Values;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (brushShader == null)
        {
            Debug.LogError("PaintSurfaceManager: brushShader가 할당되지 않았습니다.");
            return;
        }
        _brushMaterial = new Material(brushShader);
        _brushCommands = new CommandBuffer { name = "PaintBrush" };
    }

    private void OnDestroy()
    {
        _brushCommands?.Release();
        if (_brushMaterial != null)
        {
            Destroy(_brushMaterial);
        }
    }

    public void Register(int id, PaintableSurface surface)
    {
        if (_surfaces.ContainsKey(id))
        {
            Debug.LogWarning($"SurfaceId {id}가 중복 등록되었습니다: {surface.name}");
        }
        _surfaces[id] = surface;

        MeasureAreaPerPixel(surface);
    }

    /// <summary>
    /// 페인트 마스크 1픽셀이 덮는 평균 월드 면적(m²)을 GPU로 한 번 측정한다. <br/>
    /// 메시가 Read/Write 비활성이라 빌드에서는 스크립트로 정점을 읽을 수 없으므로, 메시를 UV 공간에 펼쳐 그리면서
    /// 픽셀마다 월드 면적을 기록하고 합산한다. 칠해진 픽셀 수 × 이 값 = 실제로 칠해진 면적
    /// </summary>
    private void MeasureAreaPerPixel(PaintableSurface surface)
    {
        if (_brushMaterial == null || surface.Canvas == null) return;

        var filter = surface.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        Mesh mesh = filter.sharedMesh;

        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
            ? RenderTextureFormat.RFloat
            : RenderTextureFormat.RHalf;
        RenderTexture areaMap = RenderTexture.GetTemporary(AREA_MAP_SIZE, AREA_MAP_SIZE, 0, format);

        _brushCommands.Clear();
        _brushCommands.SetRenderTarget(areaMap);
        _brushCommands.ClearRenderTarget(false, true, Color.clear);
        _brushCommands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        Matrix4x4 localToWorld = surface.transform.localToWorldMatrix;
        int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
        for (int i = 0; i < subMeshCount; i++)
        {
            _brushCommands.DrawMesh(mesh, localToWorld, _brushMaterial, i, AREA_PASS);
        }
        Graphics.ExecuteCommandBuffer(_brushCommands);

        int canvasPixels = surface.Canvas.width * surface.Canvas.height;
        AsyncGPUReadback.Request(areaMap, 0, TextureFormat.RFloat, request =>
        {
            RenderTexture.ReleaseTemporary(areaMap);

            if (request.hasError || surface == null)
            {
                Debug.LogWarning($"[PaintSurfaceManager] {(surface != null ? surface.name : "?")} 면적 측정 실패. 이 표면은 페인트 점수에 반영되지 않습니다.");
                return;
            }

            NativeArray<float> areas = request.GetData<float>();
            double totalArea = 0;
            int coveredPixels = 0;
            for (int i = 0; i < areas.Length; i++)
            {
                if (areas[i] > 0f)
                {
                    totalArea += areas[i];
                    coveredPixels++;
                }
            }

            if (coveredPixels == 0) return;

            // 측정용 텍스처에서 메시가 차지한 비율 = 실제 페인트 마스크에서 메시가 차지하는 비율
            float coveredRatio = coveredPixels / (float)areas.Length;
            surface.AreaPerPixel = (float)(totalArea / (coveredRatio * canvasPixels));
        });
    }

    public void Unregister(int id)
    {
        _surfaces.Remove(id);
    }

    /// <summary>
    /// 지정된 표면에 월드 좌표 기준 브러시를 적용한다. (칠하기/지우기 공용, 로컬에서만 실행) <br/>
    /// 표면 메시를 UV 공간에 펼쳐 그리면서 픽셀마다 월드 거리를 재므로, 메시 크기/스케일/UV 배치와
    /// 상관없이 어느 표면이든 같은 실제 크기의 자국이 된다. 모서리에 찍으면 양쪽 면에 걸쳐 칠해진다.
    /// </summary>
    /// <param name="surfaceId">대상 표면 ID</param>
    /// <param name="worldPoint">브러시 중심 (레이가 맞은 월드 좌표)</param>
    /// <param name="radius">브러시 반경 (미터)</param>
    /// <param name="isPaint">true = 페인트 칠하기, false = 지우기</param>
    public void DrawAt(int surfaceId, Vector3 worldPoint, float radius, bool isPaint)
    {
        if (_brushMaterial == null) return;

        if (!_surfaces.TryGetValue(surfaceId, out var surface) || surface.Canvas == null)
        {
            Debug.LogWarning($"SurfaceId {surfaceId}를 찾을 수 없습니다.");
            return;
        }

        var filter = surface.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        Mesh mesh = filter.sharedMesh;

        _brushMaterial.SetVector(BrushPosId, worldPoint);
        _brushMaterial.SetFloat(BrushRadiusId, Mathf.Max(0.001f, radius));
        _brushMaterial.SetFloat(BrushValueId, isPaint ? 1f : 0f);

        _brushCommands.Clear();
        _brushCommands.SetRenderTarget(surface.Canvas);
        // 뷰/투영을 단위 행렬로 두면 셰이더의 UNITY_MATRIX_P에는 플랫폼 보정(렌더 텍스처 상하 반전 등)만 남는다
        _brushCommands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);

        // 서브메시(멀티 머테리얼)로 나뉜 메시도 전체 면이 빠짐없이 칠해지도록 전부 그린다
        Matrix4x4 localToWorld = surface.transform.localToWorldMatrix;
        int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
        for (int i = 0; i < subMeshCount; i++)
        {
            _brushCommands.DrawMesh(mesh, localToWorld, _brushMaterial, i, BRUSH_PASS);
        }

        Graphics.ExecuteCommandBuffer(_brushCommands);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            RequestContaminationReadback(surfaceId, surface);
        }
    }

    /// <summary>
    /// 표면의 칠해진 픽셀을 비동기로 세어 오염 비율과 실제 칠해진 면적(m²)을 갱신한다.
    /// 같은 표면에 대해 이미 읽기 요청이 진행 중이면 새 요청을 보내지 않고 건너뛴다.
    /// (연사로 인해 초당 여러 번 호출되어도, 이전 요청이 끝나야 다음 요청이 나가므로
    ///  자연스럽게 스로틀링되어 104만 픽셀 풀스캔이 겹쳐 쌓이는 걸 막아준다)
    /// </summary>
    private void RequestContaminationReadback(int surfaceId, PaintableSurface surface)
    {
        if (_readbackInFlight.Contains(surfaceId)) return;
        _readbackInFlight.Add(surfaceId);

        AsyncGPUReadback.Request(surface.Canvas, 0, TextureFormat.RGBA32, request =>
        {
            _readbackInFlight.Remove(surfaceId);

            if (request.hasError) return;

            NativeArray<Color32> rawData = request.GetData<Color32>();
            int totalPixels = rawData.Length;
            if (totalPixels == 0) return;

            int paintedPixels = 0;
            for (int i = 0; i < totalPixels; i++)
            {
                if (rawData[i].r > 50) // 임계값
                {
                    paintedPixels++;
                }
            }

            // 페인트 점수는 비율이 아니라 실제 면적으로 매긴다 (큰 바닥이든 작은 기둥이든 같은 면적이면 같은 점수)
            float paintedArea = paintedPixels * surface.AreaPerPixel;

            // 잔여 얼룩(눈에 안 보이는 수준)은 0으로 취급. 비율 기준이면 큰 바닥에서 수 m²가 무시되므로 면적 기준
            if (paintedArea < MIN_PAINTED_AREA)
            {
                paintedArea = 0f;
                paintedPixels = 0;
            }

            surface.PaintedArea = paintedArea;

            // 참고용 비율 (소수점 2자리)
            float percent = (paintedPixels / (float)totalPixels) * 100f;
            surface.ContaminationPercent = Mathf.Round(percent * 100f) / 100f;

            ScoreManager.Instance?.RecalculateTotalContamination();
        });
    }
}