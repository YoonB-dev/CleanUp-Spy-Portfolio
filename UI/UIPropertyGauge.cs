using UnityEngine;
using UnityEngine.UI;

public class UIPropertyGauge : MonoBehaviour
{
    [SerializeField] private Image gaugeImage;

    // Shader Graph Blackboard의 Reference Name과 정확히 일치해야 합니다.
    private static readonly int TrashProp = Shader.PropertyToID("_Trash");
    private static readonly int BoxProp = Shader.PropertyToID("_Box");
    private static readonly int PaintProp = Shader.PropertyToID("_Paint");

    public void UpdateGaugeValues(float trashRatio, float boxRatio, float paintRatio)
    {
        if (gaugeImage == null) return;

        // UI Image의 머티리얼 인스턴스 파라미터 직접 변경
        gaugeImage.material.SetFloat(TrashProp, Mathf.Clamp01(trashRatio));
        gaugeImage.material.SetFloat(BoxProp, Mathf.Clamp01(boxRatio));
        gaugeImage.material.SetFloat(PaintProp, Mathf.Clamp01(paintRatio));
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