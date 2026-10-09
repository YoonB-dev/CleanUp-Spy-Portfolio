using UnityEngine;

public static class PlacementValidator
{
    /// <summary>
    /// 클라이언트 프리뷰와 서버 RPC에서 같은 규칙으로 검증을 수행한다.
    /// 상자가 설치 가능한지를 판단한다.
    /// </summary>
    public static bool IsValidPlacement(Vector3 targetPos, PlacementZone zone, float boxSize, GameObject heldObjForIgnore = null)
    {
        // 1. 영역 이탈 검사
        if (!IsPositionInsideZone(targetPos, zone, boxSize)) return false;
        // 2. 공간 비어있는지 검사
        if (!IsSpaceEmpty(targetPos, boxSize, heldObjForIgnore)) return false;
        // 3. 발판(밑장) 존재 검사
        if (!HasValidGround(targetPos, zone, boxSize)) return false;
        return true;
    }

    private static bool IsPositionInsideZone(Vector3 targetPos, PlacementZone zone, float boxSize)
    {
        Collider zoneCollider = zone.GetComponent<Collider>();
        if (zoneCollider == null) return false;

        Bounds zoneBounds = zoneCollider.bounds;
        float margin = boxSize * 0.5f;

        return (targetPos.x >= zoneBounds.min.x + margin) && (targetPos.x <= zoneBounds.max.x - margin) &&
               (targetPos.z >= zoneBounds.min.z + margin) && (targetPos.z <= zoneBounds.max.z - margin);
    }

    private static bool IsSpaceEmpty(Vector3 targetPosition, float boxSize, GameObject heldObjForIgnore)
    {
        Collider[] obstacles = Physics.OverlapBox(targetPosition, Vector3.one * (boxSize * 0.45f));
        foreach (var obstacle in obstacles)
        {
            if (heldObjForIgnore != null && obstacle.gameObject == heldObjForIgnore) continue;
            return false;
        }
        return true;
    }

    private static bool HasValidGround(Vector3 targetPos, PlacementZone zone, float boxSize)
    {
        float groundLevel = zone.GetSnappedPosition(targetPos).y;
        if (Mathf.Approximately(targetPos.y, groundLevel)) return true;

        Vector3 underPos = targetPos + (Vector3.down * boxSize);
        Collider[] underColliders = Physics.OverlapBox(underPos, Vector3.one * (boxSize * 0.45f));
        foreach (var col in underColliders)
        {
            if (col.CompareTag("PlacedBox")) return true;
        }
        return false;
    }
}