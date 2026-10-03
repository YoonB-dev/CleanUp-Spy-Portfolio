using UnityEngine;
using UnityEngine.UI;

public class UIPropertyGauge : MonoBehaviour
{
    [SerializeField] private Image gaugeImage;

    [Header("차오르는 애니메이션")]
    [Tooltip("목표값까지 따라가는 데 걸리는 대략적인 시간(초). 작을수록 빠르다")]
    [SerializeField] private float fillSmoothTime = 0.35f;

    // Shader Graph Blackboard의 Reference Name과 정확히 일치해야 합니다.
    private static readonly int TrashProp = Shader.PropertyToID("_Trash");
    private static readonly int BoxProp = Shader.PropertyToID("_Box");
    private static readonly int PaintProp = Shader.PropertyToID("_Paint");
    private static readonly int PatternAspectProp = Shader.PropertyToID("_PatternAspect");

    // 경계 좌표가 이 값 이내로 목표에 붙으면 애니메이션 종료로 본다
    private const float SNAP_EPSILON = 0.0005f;

    // 경계 좌표는 오른쪽 끝(1)에서 깎아 나가므로 1이면 빈 게이지
    private Vector3 _target = Vector3.one;   // x=Trash, y=Box, z=Paint 경계
    private Vector3 _current = Vector3.one;
    private Vector3 _velocity;
    private bool _isAnimating;

    private Material _gaugeMaterial;
    private Vector2 _lastRectSize; // 무늬 비율 보정용. 크기가 바뀔 때만 셰이더에 다시 넘긴다

    private void Awake()
    {
        if (gaugeImage == null) return;

        // Image.material은 공유 에셋을 돌려주므로, 그대로 쓰면 플레이할 때마다 .mat 파일이 바뀐다.
        // 복제본을 만들어 이 게이지만 쓰게 한다
        _gaugeMaterial = new Material(gaugeImage.material);
        gaugeImage.material = _gaugeMaterial;
        ApplyToMaterial(_current);
    }

    private void OnDestroy()
    {
        if (_gaugeMaterial != null)
        {
            Destroy(_gaugeMaterial);
        }
    }

    private void Update()
    {
        UpdatePatternAspect();

        if (!_isAnimating) return;

        _current = Vector3.SmoothDamp(_current, _target, ref _velocity, fillSmoothTime);

        if ((_current - _target).sqrMagnitude < SNAP_EPSILON * SNAP_EPSILON)
        {
            _current = _target;
            _velocity = Vector3.zero;
            _isAnimating = false;
        }

        ApplyToMaterial(_current);
    }

    /// <summary>
    /// 경계 좌표 목표값을 지정한다. 실제 표시는 Update에서 부드럽게 따라간다
    /// </summary>
    public void UpdateGaugeValues(float trashRatio, float boxRatio, float paintRatio)
    {
        _target = new Vector3(Mathf.Clamp01(trashRatio), Mathf.Clamp01(boxRatio), Mathf.Clamp01(paintRatio));
        _isAnimating = true;
    }

    /// <summary>
    /// 게이지가 가로로 길어서 UV 그대로면 대각선 무늬가 늘어나 보인다. 실제 가로/세로 비율을 셰이더에 넘긴다
    /// </summary>
    private void UpdatePatternAspect()
    {
        if (_gaugeMaterial == null) return;

        Vector2 size = gaugeImage.rectTransform.rect.size;
        if (size == _lastRectSize || size.y <= 0f) return;

        _lastRectSize = size;
        _gaugeMaterial.SetFloat(PatternAspectProp, size.x / size.y);
    }

    private void ApplyToMaterial(Vector3 thresholds)
    {
        if (_gaugeMaterial == null) return;

        _gaugeMaterial.SetFloat(TrashProp, thresholds.x);
        _gaugeMaterial.SetFloat(BoxProp, thresholds.y);
        _gaugeMaterial.SetFloat(PaintProp, thresholds.z);
    }

    /// <summary>
    /// 서버가 계산한 최종 오염 점수(배율 적용됨)를 목표 총점 대비 0~1 비율로 환산 후 반영.
    /// 밸런스 값은 승패 판정과 같이 쓰도록 ScoreManager가 들고 있다.
    /// </summary>
    public void CalculateGaugeValues(float trashScore, float boxScore, float paintScore, float maxTargetScore)
    {
        if (maxTargetScore <= 0f) return;

        // 1. MaxScore 대비 각 점수의 비율(0~1) 구하기
        float paintRatio = paintScore / maxTargetScore;
        float boxRatio = boxScore / maxTargetScore;
        float trashRatio = trashScore / maxTargetScore;

        // 총 비율이 1.0을 초과할 경우 비율 보정
        float totalRatio = paintRatio + boxRatio + trashRatio;
        if (totalRatio > 1.0f)
        {
            paintRatio /= totalRatio;
            boxRatio /= totalRatio;
            trashRatio /= totalRatio;
        }

        // 2. 1에서부터 깎아 나가는 '누적 경계 좌표(Threshold)' 계산
        float paintThreshold = 1.0f - paintRatio;
        float boxThreshold = paintThreshold - boxRatio;
        float trashThreshold = boxThreshold - trashRatio;

        // 셰이더로 전달 (경계 좌표 전달)
        UpdateGaugeValues(trashThreshold, boxThreshold, paintThreshold);
    }

    public void Start()
    {
        // Debug.Log("UIPropertyGauge Start() called. Initializing gauge values.");
        // CalculateGaugeValues(30, , 20f);
    }
}