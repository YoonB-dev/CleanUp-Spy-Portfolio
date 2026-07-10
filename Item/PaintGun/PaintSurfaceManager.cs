using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 씬 내 모든 PaintableSurface를 등록/관리하며, 실제 브러시(Blit) 연산을 수행하는 로컬 싱글톤.
/// 네트워크 오브젝트가 아님 - 각 클라이언트가 자기 로컬 인스턴스를 가지고 자기 화면만 그림.
/// </summary>
public class PaintSurfaceManager : MonoBehaviour
{
    public static PaintSurfaceManager Instance { get; private set; }

    [Header("Brush")]
    [SerializeField] private Shader brushShader; // Hidden/PaintBrush 셰이더 할당

    private Material _brushMaterial;
    private readonly Dictionary<int, PaintableSurface> _surfaces = new();
    private readonly HashSet<int> _readbackInFlight = new(); // 표면별로 이미 읽기 오쳥이 진행 중인지 추적.
    public IEnumerable<PaintableSurface> AllSurfaces => _surfaces.Values;
    private const float CONTAMINATION_THRES_HOLD = 0.5f;
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
    }

    public void Register(int id, PaintableSurface surface)
    {
        if (_surfaces.ContainsKey(id))
        {
            Debug.LogWarning($"SurfaceId {id}가 중복 등록되었습니다: {surface.name}");
        }
        _surfaces[id] = surface;
    }

    public void Unregister(int id)
    {
        _surfaces.Remove(id);
    }

    /// <summary>
    /// 지정된 표면의 UV 위치에 브러시를 적용한다. (칠하기/지우기 공용, 로컬에서만 실행)
    /// </summary>
    /// <param name="surfaceId">대상 표면 ID</param>
    /// <param name="uv">브러시를 찍을 UV 좌표 (0~1)</param>
    /// <param name="radius">브러시 반경 (UV 기준)</param>
    /// <param name="isPaint">true = 페인트 칠하기, false = 지우기</param>
    public void DrawAt(int surfaceId, Vector2 uv, float radius, bool isPaint)
    {
        
        if (_brushMaterial == null) return;

        if (!_surfaces.TryGetValue(surfaceId, out var surface) || surface.Canvas == null)
        {
            Debug.LogWarning($"SurfaceId {surfaceId}를 찾을 수 없습니다.");
            return;
        }

        RenderTexture temp = RenderTexture.GetTemporary(surface.Canvas.descriptor); // 임시 텍스처를 생성하는데, 재사용 가능하도록 GPU 메모리 풀에서 가져오는 느낌임. (RenderTexture.ReleaseTemporary로 반납해야 함) -> 밑에 있음.

        _brushMaterial.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0)); // 브러쉬의 위치
        _brushMaterial.SetFloat("_BrushRadius", radius); // 브러쉬의 반경
        _brushMaterial.SetFloat("_BrushValue", isPaint ? 1f : 0f); // 브러쉬의 값 (1=칠하기, 0=지우기)

        // 기존 캔버스를 읽어서 temp에 브러시를 합성한 결과를 그린 뒤, 다시 원본 캔버스로 복사
        // Graphics.Blit(A, B, Material)은 A라는 텍스처(이미지)를 복사해서 B라는 이미지에 붙여넣는데, 그 사이에 Material(셰이더)라는 필터를 거치게 해라 라는 뜻이래..
        Graphics.Blit(surface.Canvas, temp, _brushMaterial);
        Graphics.Blit(temp, surface.Canvas);
        RenderTexture.ReleaseTemporary(temp); // 메모리에 있던 임시 텍스처를 반납한다는 의미임. -> 그래픽 작업은 무거워서 그냥 삭제를 하면 안됨.

        // 오염도 수치 갱신 요청 (서버가 아니면 내부에서 무시됨)
        //ScoreManager.Instance?.OnBrushApplied(isPaint);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            RequestContaminationReadback(surfaceId, surface);
        }
    }

    /// <summary>
    /// 표면의 실제 오염 픽셀 비율을 비동기로 읽어온다.
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

            float percent = (paintedPixels / (float)totalPixels) * 100f;

            // 잔여 얼룩(눈에 안 보이는 수준)은 0으로 취급
            if (percent < CONTAMINATION_THRES_HOLD)
            {
                percent = 0f;
            }

            // 소수점 2자리로 반올림
            percent = Mathf.Round(percent * 100f) / 100f;

            surface.ContaminationPercent = percent;

            ScoreManager.Instance?.RecalculateTotalContamination();
        });
    }
}