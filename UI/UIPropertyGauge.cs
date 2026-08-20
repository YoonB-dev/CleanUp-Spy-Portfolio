using UnityEngine;
using UnityEngine.UI;

public class UIPropertyGauge : MonoBehaviour
{
    [SerializeField] private Image gaugeImage;
    [Header("Gauge Balancing Settings")]
    [Tooltip("게이지 100%를 채우기 위한 목표 총점")]
    [SerializeField] private float maxTargetScore = 100f;
    [Tooltip("배치되지 않은 박스 1개당 환산 점수")]
    [SerializeField] private float scorePerBox = 1f;

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
    /// 실제 게임 내 점수/개수를 입력받아 MaxScore 대비 0~1 비율로 환산 후 반영
    /// </summary>
    public void CalculateGaugeValues(float trashScore, int unplacedBoxCount, float paintScore)
    {
        if (maxTargetScore <= 0f) return;

        // 1. 박스 개수를 점수 단위로 환산
        float boxScore = unplacedBoxCount * scorePerBox;

        // 2. MaxScore 대비 각 점수의 비율(0~1) 구하기
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

        // 3. 1에서부터 깎아 나가는 '누적 경계 좌표(Threshold)' 계산
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