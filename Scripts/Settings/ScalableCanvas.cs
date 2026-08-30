using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 스케일 설정을 이 캔버스에 반영한다. 크기를 조절하고 싶은 캔버스에만 붙인다.
/// </summary>
[RequireComponent(typeof(CanvasScaler))]
public class ScalableCanvas : MonoBehaviour
{
    private CanvasScaler _scaler;
    private float _baseScaleFactor;
    private Vector2 _baseReference;

    private void Awake()
    {
        _scaler = GetComponent<CanvasScaler>();
        _baseScaleFactor = _scaler.scaleFactor;
        _baseReference = _scaler.referenceResolution;
    }

    private void OnEnable()
    {
        SettingsManager.Instance.Changed += Apply;
        Apply();
    }

    private void OnDisable()
    {
        if (SettingsManager.Existing != null) SettingsManager.Existing.Changed -= Apply;
    }

    private void Apply()
    {
        float scale = SettingsManager.Instance.Settings.uiScale;

        // 화면 비율을 따라가는 모드에서는 기준 해상도를 줄여야 UI가 커진다
        if (_scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) _scaler.scaleFactor = _baseScaleFactor * scale;
        else _scaler.referenceResolution = _baseReference / scale;
    }
}
