using UnityEngine;

public class PlacementZone : MonoBehaviour
{
    private float gridUnitSize = 1.0f;
    private float boxHalfHeight = 0.5f;

    /// <summary>
    /// 레이캐스트가 맞은 좌표를 바탕으로 가장 가까운 그리드 중심점 좌표를 계산합니다.
    /// </summary>
    public Vector3 GetSnappedPosition(Vector3 hitPoint)
    {
        // 지면의 가장 높은 Y값(표면)을 구합니다.
        float surfaceY = GetComponent<Collider>().bounds.max.y;

        // X, Z 좌표를 gridUnitSize 단위로 반올림하여 스냅(Snap) 처리를 합니다.
        float snappedX = Mathf.Round(hitPoint.x / gridUnitSize) * gridUnitSize;
        float snappedZ = Mathf.Round(hitPoint.z / gridUnitSize) * gridUnitSize;

        // 박스의 중심점이 표면 위로 딱 올라오도록 Y축을 보정합니다.
        float snappedY = surfaceY + boxHalfHeight;

        return new Vector3(snappedX, snappedY, snappedZ);
    }
}