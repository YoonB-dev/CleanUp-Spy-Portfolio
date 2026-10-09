using UnityEngine;
using UnityEngine.UI;

public class PaintGaugeUI : SceneSingleton<PaintGaugeUI>
{
    [SerializeField] private Image gaugeFillImage;
    private MafiaPaintAction paintAction;

    public void Subscribe(MafiaPaintAction pa)
    {
        // 기존 구독이 있다면 먼저 해제 (중복 구독 방지)
        if (paintAction != null)
        {
            paintAction.OnPaintGaugeChanged -= UpdateUI;
        }

        paintAction = pa;

        if (paintAction != null)
        {
            paintAction.OnPaintGaugeChanged += UpdateUI;
            UpdateUI(paintAction.CurrentPaint); // 초기값 세팅
        }
    }

    public void Unsubscribe()
    {
        if (paintAction != null)
        {
            paintAction.OnPaintGaugeChanged -= UpdateUI;
            paintAction = null;
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    override protected void OnDestroy()
    {
        base.OnDestroy();
        Unsubscribe();
    }

    private void UpdateUI(float currentRatio)
    {
        if (gaugeFillImage != null)
        {
            gaugeFillImage.fillAmount = Mathf.Clamp01(currentRatio);
        }
    }
}