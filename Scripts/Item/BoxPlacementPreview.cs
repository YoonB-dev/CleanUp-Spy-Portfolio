using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 박스 배치 프리뷰를 관리하는 컴포넌트.
/// 반투명 채움 + 모서리 선으로 표시하며, 배치 가능이면 초록 / 불가면 빨강으로 표시한다.
/// </summary>
public class BoxPlacementPreview : MonoBehaviour
{
    [Header("의존성 컴포넌트")]
    [SerializeField] private Camera playerCamera;

    [Header("시각적 프리뷰 설정")]
    [Tooltip("URP/Unlit + Surface Type: Transparent 머티리얼 권장. 색상은 코드에서 _BaseColor로 덮어씀")]
    [SerializeField] private Material previewMaterial;
    [SerializeField] private Color validFillColor = new Color(0.2f, 1f, 0.4f, 0.2f);
    [SerializeField] private Color validEdgeColor = new Color(0.2f, 1f, 0.4f, 1f);
    [SerializeField] private Color invalidFillColor = new Color(1f, 0.25f, 0.25f, 0.2f);
    [SerializeField] private Color invalidEdgeColor = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] private float edgeWidth = 0.03f;
    private float interactDistance = 3.5f;
    public float gBoxSize = 1.0f;

    [Header("조준 보정")]
    [Tooltip("레이가 구역에 닿지 않아도, 시선이 구역 바닥 평면에서 이 거리 안이면 가장 가까운 칸으로 스냅")]
    [SerializeField] private float zonePlaneSnapRange = 1.0f;
    [Tooltip("조준이 잠깐 빗나가도 마지막 프리뷰를 유지하는 시간(초)")]
    [SerializeField] private float aimGraceTime = 0.15f;

    /// <summary>카메라 기준 최대 배치 거리. 서버 검증에서도 사용합니다.</summary>
    public float MaxPlacementDistance => interactDistance + zonePlaneSnapRange;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly IComparer<RaycastHit> RaycastHitDistanceComparer = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
    private readonly RaycastHit[] raycastHitBuffer = new RaycastHit[16];
    private readonly Collider[] zoneColliderBuffer = new Collider[8];
    private readonly Collider[] occupancyBuffer = new Collider[16];
    private float lastTargetFoundTime = float.NegativeInfinity;

    private GameObject dynamicPreviewInstance;
    private GameObject previewSource; // 프리뷰를 만든 원본 상자
    private PlacementZone currentPreviewZone;
    private readonly List<Renderer> fillRenderers = new List<Renderer>();
    private readonly List<Renderer> edgeRenderers = new List<Renderer>();
    private MaterialPropertyBlock propertyBlock;
    private bool? appliedValidState;
    private Vector3 currentPreviewPosition;
    private bool isPreviewValid = false;

    // PlayerInteraction에서 이 좌표들을 슥 가져가서 서버로 던질 수 있도록 프로퍼티로 노출합니다.
    public Vector3 CurrentPreviewPosition => currentPreviewPosition;
    public bool IsPreviewValid => isPreviewValid;

    private void Awake()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }
    }

    /// <summary>
    /// PlayerInteraction의 Update 등에서 이 함수를 매 프레임 호출해 줍니다.
    /// </summary>
    public void UpdatePreview(PickupItem heldItem)
    {
        // 1. 아이템 유실 검사 (Netcode 동기화 중 일시적으로 null이 되는 타이밍 방어)
        if (heldItem == null)
        {
            if (GetComponent<PlayerInteraction>().IsHoldingItem())
            {
                return;
            }
            ClearPreview();
            return;
        }

        // 2. 배치 가능한 상자 컴포넌트 검사
        if (!heldItem.TryGetComponent<PlaceableBox>(out _))
        {
            ClearPreview();
            return;
        }

        // 3. 실시간 메쉬 복사본 생성 (들고 있는 상자가 바뀌면 다시 생성)
        if (previewSource != heldItem.gameObject)
        {
            CreateDynamicPreview(heldItem.gameObject);
        }

        if (playerCamera == null)
        {
            return;
        }

        // 4. 배치 대상 찾기
        // 레이가 구역/상자에 맞았으면 그 결과만 사용하고, 아무것도 안 맞았을 때만 바닥 평면 투영으로 보정
        // (상자를 조준했는데 막혀있다고 해서 상자 너머 바닥으로 프리뷰가 튀지 않도록)
        // 이미 배치된 상자가 있는 칸은 굳이 표시하지 않으므로 대상에서 제외
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        bool hasTarget = TryFindTargetByRaycast(ray, heldItem, out Vector3 targetPosition, out PlacementZone targetZone, out bool hitPlacementSurface)
            || (!hitPlacementSurface && TryFindTargetByZonePlane(ray, out targetPosition, out targetZone));

        if (hasTarget && !IsOccupiedByPlacedBox(targetPosition))
        {
            // 5. 최종 배치 타당성 검증 (PlacementValidator)
            // 구역은 조준했지만 자리가 막혀있거나 받침이 없으면 빨간색으로 보여줌
            bool canPlace = PlacementValidator.IsValidPlacement(targetPosition, targetZone, gBoxSize, heldItem.gameObject);
            currentPreviewPosition = targetPosition;
            currentPreviewZone = targetZone;
            isPreviewValid = canPlace;
            lastTargetFoundTime = Time.time;

            if (dynamicPreviewInstance != null)
            {
                dynamicPreviewInstance.transform.position = targetPosition;
                dynamicPreviewInstance.transform.localScale = heldItem.transform.localScale * 2.0f; // 플레이어 스케일이 2라서 그거 맞춰서 한거임
                dynamicPreviewInstance.transform.rotation = Quaternion.identity;
                ApplyPreviewColor(canPlace);
                dynamicPreviewInstance.SetActive(true);
            }
            return;
        }

        // 6. 조준이 잠깐 빗나간 경우엔 유예 시간 동안 마지막 프리뷰를 유지 (깜빡임 방지)
        // 단, 그 사이 다른 플레이어가 칸을 채웠을 수 있으니 마지막 위치를 다시 검증함
        if (Time.time - lastTargetFoundTime <= aimGraceTime
            && currentPreviewZone != null && !IsOccupiedByPlacedBox(currentPreviewPosition))
        {
            isPreviewValid = PlacementValidator.IsValidPlacement(currentPreviewPosition, currentPreviewZone, gBoxSize, heldItem.gameObject);
            ApplyPreviewColor(isPreviewValid);
            return;
        }

        // 대상을 못 찾았을 때 프리뷰 숨김 처리
        isPreviewValid = false;
        if (dynamicPreviewInstance != null)
        {
            dynamicPreviewInstance.SetActive(false);
        }
    }

    /// <summary>
    /// 레이 경로에서 구역 바닥 / 배치된 상자만 골라서 조준 대상으로 삼습니다.
    /// 쓰레기, 들고 있는 상자, 플레이어 등 다른 물체는 통과합니다.
    /// </summary>
    /// <param name="hitPlacementSurface">구역 바닥이나 배치된 상자에 레이가 맞았는지 (배치 가능 여부와 무관)</param>
    private bool TryFindTargetByRaycast(Ray ray, PickupItem heldItem, out Vector3 targetPosition, out PlacementZone targetZone, out bool hitPlacementSurface)
    {
        targetPosition = Vector3.zero;
        targetZone = null;
        hitPlacementSurface = false;

        int hitCount = Physics.RaycastNonAlloc(ray, raycastHitBuffer, interactDistance, ~(1 << LayerMask.NameToLayer("Ignore Raycast")));
        System.Array.Sort(raycastHitBuffer, 0, hitCount, RaycastHitDistanceComparer);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = raycastHitBuffer[i];
            Transform hitTransform = hit.collider.transform;
            if (hitTransform.IsChildOf(transform) || hitTransform.IsChildOf(heldItem.transform)) continue;

            // 구역 바닥(PlacementZone)을 직접 조준한 경우
            if (hit.collider.TryGetComponent<PlacementZone>(out var zone))
            {
                hitPlacementSurface = true;
                targetPosition = zone.GetSnappedPosition(hit.point);
                targetZone = zone;
                return true;
            }

            // 이미 배치된 상자(PlacedBox)를 조준한 경우 → 맞은 면 방향으로 한 칸 옆/위
            if (hit.collider.CompareTag("PlacedBox"))
            {
                hitPlacementSurface = true;
                targetPosition = hitTransform.position + (hit.normal * gBoxSize);
                targetZone = FindZoneUnder(targetPosition);
                return targetZone != null;
            }
        }
        return false;
    }

    /// <summary>
    /// 레이가 구역에 직접 닿지 않았을 때, 근처 구역의 바닥 평면에 시선을 투영해서 가장 가까운 칸을 찾습니다.
    /// 에임이 구역 가장자리 밖이나 약간 옆을 보고 있어도 프리뷰가 뜨게 하는 용도입니다.
    /// </summary>
    private bool TryFindTargetByZonePlane(Ray ray, out Vector3 targetPosition, out PlacementZone targetZone)
    {
        targetPosition = Vector3.zero;
        targetZone = null;

        // 아래를 보고 있지 않으면 바닥 평면과 만나지 않음
        if (ray.direction.y >= -0.01f) return false;

        float bestDistance = float.MaxValue;
        int zoneCount = Physics.OverlapSphereNonAlloc(ray.origin, interactDistance + zonePlaneSnapRange, zoneColliderBuffer, LayerMask.GetMask("PlacementZone"));
        for (int i = 0; i < zoneCount; i++)
        {
            if (!zoneColliderBuffer[i].TryGetComponent<PlacementZone>(out var zone)) continue;

            Bounds bounds = zoneColliderBuffer[i].bounds;
            Plane surface = new Plane(Vector3.up, new Vector3(0f, bounds.max.y, 0f));
            if (!surface.Raycast(ray, out float enter)) continue;

            // 시선이 닿은 평면 위의 점을 구역 안쪽으로 끌어옴
            Vector3 aimPoint = ray.GetPoint(enter);
            float margin = gBoxSize * 0.5f;
            Vector3 clampedPoint = new Vector3(
                Mathf.Clamp(aimPoint.x, bounds.min.x + margin, bounds.max.x - margin),
                aimPoint.y,
                Mathf.Clamp(aimPoint.z, bounds.min.z + margin, bounds.max.z - margin));

            // 구역에서 너무 먼 곳을 보고 있으면 무시
            float offZoneDistance = Vector3.Distance(aimPoint, clampedPoint);
            if (offZoneDistance > zonePlaneSnapRange) continue;

            // 손이 닿지 않는 거리면 무시 (서버 검증과 같은 기준: 카메라 → 스냅된 최종 위치 3D 거리)
            Vector3 snappedPosition = zone.GetSnappedPosition(clampedPoint);
            if (Vector3.Distance(ray.origin, snappedPosition) > MaxPlacementDistance) continue;

            if (offZoneDistance < bestDistance)
            {
                bestDistance = offZoneDistance;
                targetPosition = snappedPosition;
                targetZone = zone;
            }
        }
        return targetZone != null;
    }

    /// <summary>
    /// 해당 칸에 이미 배치된 상자가 있는지 검사합니다.
    /// </summary>
    private bool IsOccupiedByPlacedBox(Vector3 position)
    {
        int count = Physics.OverlapBoxNonAlloc(position, Vector3.one * (gBoxSize * 0.45f), occupancyBuffer);
        for (int i = 0; i < count; i++)
        {
            if (occupancyBuffer[i].CompareTag("PlacedBox")) return true;
        }
        return false;
    }

    /// <summary>
    /// 주어진 위치 아래에 있는 구역을 찾습니다. (서버 RPC와 같은 방식)
    /// </summary>
    private PlacementZone FindZoneUnder(Vector3 position)
    {
        if (Physics.Raycast(position + Vector3.up * 0.1f, Vector3.down, out RaycastHit groundHit, 50f, LayerMask.GetMask("PlacementZone")))
        {
            return groundHit.collider.GetComponent<PlacementZone>();
        }
        return null;
    }

    private void CreateDynamicPreview(GameObject originalObj)
    {
        ClearPreview();
        previewSource = originalObj; // 머티리얼이 없어도 매 프레임 재생성을 시도하지 않도록 먼저 기록
        if (originalObj == null) return;
        if (previewMaterial == null)
        {
            Debug.LogWarning("[BoxPlacementPreview] previewMaterial이 비어있어 프리뷰를 표시할 수 없습니다.", this);
            return;
        }

        dynamicPreviewInstance = new GameObject("Dynamic_Placement_Preview");
        dynamicPreviewInstance.layer = LayerMask.NameToLayer("Ignore Raycast");

        // 자식 메쉬(BoxModel 등)의 위치/회전/스케일 보정값을 루트 기준으로 그대로 옮겨서 복사
        Transform originalRoot = originalObj.transform;
        foreach (MeshFilter originalMeshFilter in originalObj.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!originalMeshFilter.TryGetComponent<MeshRenderer>(out var originalMeshRenderer)) continue;

            Transform originalMeshTransform = originalMeshFilter.transform;
            GameObject previewModel = new GameObject(originalMeshFilter.name);
            previewModel.layer = dynamicPreviewInstance.layer;

            Transform previewModelTransform = previewModel.transform;
            previewModelTransform.SetParent(dynamicPreviewInstance.transform, false);
            previewModelTransform.localPosition = originalRoot.InverseTransformPoint(originalMeshTransform.position);
            previewModelTransform.localRotation = Quaternion.Inverse(originalRoot.rotation) * originalMeshTransform.rotation;
            previewModelTransform.localScale = new Vector3(
                originalMeshTransform.lossyScale.x / originalRoot.lossyScale.x,
                originalMeshTransform.lossyScale.y / originalRoot.lossyScale.y,
                originalMeshTransform.lossyScale.z / originalRoot.lossyScale.z);

            MeshFilter previewFilter = previewModel.AddComponent<MeshFilter>();
            previewFilter.sharedMesh = originalMeshFilter.sharedMesh;

            MeshRenderer previewRenderer = previewModel.AddComponent<MeshRenderer>();
            Material[] mats = new Material[originalMeshRenderer.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = previewMaterial;
            previewRenderer.sharedMaterials = mats;
            previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
            previewRenderer.receiveShadows = false;
            fillRenderers.Add(previewRenderer);
        }

        // 부모(Box)의 BoxCollider 크기 기준으로 모서리 선 생성
        if (originalObj.TryGetComponent<BoxCollider>(out var boxCollider))
        {
            CreateEdgeLines(boxCollider.center, boxCollider.size);
        }

        foreach (var col in dynamicPreviewInstance.GetComponentsInChildren<Collider>())
        {
            Destroy(col);
        }

        dynamicPreviewInstance.SetActive(false);
    }

    /// <summary>
    /// 박스의 모서리 12개를 LineRenderer로 그립니다. (프리뷰 루트 기준 로컬 좌표)
    /// </summary>
    private void CreateEdgeLines(Vector3 center, Vector3 size)
    {
        Vector3 half = size * 0.5f;
        Vector3[] corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            // 비트 0:X, 1:Y, 2:Z 부호
            corners[i] = center + new Vector3(
                (i & 1) == 0 ? -half.x : half.x,
                (i & 2) == 0 ? -half.y : half.y,
                (i & 4) == 0 ? -half.z : half.z);
        }

        // 한 축의 비트만 다른 꼭짓점 쌍 = 모서리 (총 12개)
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit <= 4; bit <<= 1)
            {
                if ((i & bit) != 0) continue;

                GameObject edge = new GameObject("Edge");
                edge.layer = dynamicPreviewInstance.layer;
                edge.transform.SetParent(dynamicPreviewInstance.transform, false);

                LineRenderer line = edge.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = 2;
                line.SetPosition(0, corners[i]);
                line.SetPosition(1, corners[i | bit]);
                line.widthMultiplier = edgeWidth;
                line.numCapVertices = 2;
                line.sharedMaterial = previewMaterial;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                edgeRenderers.Add(line);
            }
        }
    }

    /// <summary>
    /// 배치 가능 여부에 따라 채움/모서리 색을 바꿉니다. 상태가 바뀔 때만 갱신합니다.
    /// </summary>
    private void ApplyPreviewColor(bool canPlace)
    {
        if (appliedValidState == canPlace) return;
        appliedValidState = canPlace;

        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

        propertyBlock.SetColor(BaseColorId, canPlace ? validFillColor : invalidFillColor);
        foreach (var fillRenderer in fillRenderers) fillRenderer.SetPropertyBlock(propertyBlock);

        propertyBlock.SetColor(BaseColorId, canPlace ? validEdgeColor : invalidEdgeColor);
        foreach (var edgeRenderer in edgeRenderers) edgeRenderer.SetPropertyBlock(propertyBlock);
    }

    public void ClearPreview()
    {
        isPreviewValid = false;
        if (dynamicPreviewInstance != null)
        {
            Destroy(dynamicPreviewInstance);
            dynamicPreviewInstance = null;
        }
        fillRenderers.Clear();
        edgeRenderers.Clear();
        appliedValidState = null;
        lastTargetFoundTime = float.NegativeInfinity;
        previewSource = null;
        currentPreviewZone = null;
    }
}