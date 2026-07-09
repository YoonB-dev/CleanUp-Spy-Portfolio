using System.Collections.Generic;
using UnityEngine;

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

        RenderTexture temp = RenderTexture.GetTemporary(surface.Canvas.descriptor);

        _brushMaterial.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0));
        _brushMaterial.SetFloat("_BrushRadius", radius);
        _brushMaterial.SetFloat("_BrushValue", isPaint ? 1f : 0f);

        // 기존 캔버스를 읽어서 temp에 브러시를 합성한 결과를 그린 뒤, 다시 원본 캔버스로 복사
        Graphics.Blit(surface.Canvas, temp, _brushMaterial);
        Graphics.Blit(temp, surface.Canvas);
        Debug.Log($"DrawAt 호출: SurfaceId={surfaceId}, UV={uv}, Radius={radius}, IsPaint={isPaint}");
        RenderTexture.ReleaseTemporary(temp);

        // 오염도 수치 갱신 요청 (서버가 아니면 내부에서 무시됨)
        ContaminationTracker.Instance?.OnBrushApplied(isPaint);
    }
}