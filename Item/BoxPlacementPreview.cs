using UnityEngine;

/// <summary>
/// 박스 배치 프리뷰를 관리하는 컴포넌트.
/// 
/// </summary>
public class BoxPlacementPreview : MonoBehaviour
{
    [Header("의존성 컴포넌트")]
    [SerializeField] private Camera playerCamera;

    [Header("시각적 프리뷰 설정")]
    [SerializeField] private Material previewMaterial;
    private float interactDistance = 3.5f;
    public float gBoxSize = 1.0f;

    private GameObject dynamicPreviewInstance;
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

        // 3. 실시간 메쉬 복사본 생성
        if (dynamicPreviewInstance == null)
        {
            CreateDynamicPreview(heldItem.gameObject);
        }

        // 4. 프리뷰 레이어(Ignore Raycast)를 제외한 타겟 레이 마스크 설정
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        int finalLayerMask = ~(1 << ignoreRaycastLayer);

        if (playerCamera == null)
        {
            return;
        }

        // 5. 시선 방향 레이캐스트 발사
        if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, interactDistance, finalLayerMask))
        {
            Vector3 targetPosition = Vector3.zero;
            bool isValidZone = false;
            PlacementZone targetZone = null;

            // 구역 바닥(PlacementZone)을 직접 조준한 경우
            if (hit.collider.TryGetComponent<PlacementZone>(out var zone))
            {
                targetPosition = zone.GetSnappedPosition(hit.point);
                targetZone = zone;
                isValidZone = true;
            }
            // 이미 배치된 상자(PlacedBox) 위를 조준한 경우
            else if (hit.collider.CompareTag("PlacedBox"))
            {
                targetPosition = hit.collider.transform.position + (hit.normal * gBoxSize);

                // 레이캐스트 오차 방지를 위해 OverlapSphere로 주변 바닥 구역 수색
                int zoneLayerMask = LayerMask.GetMask("PlacementZone");
                Collider[] zoneColliders = Physics.OverlapSphere(hit.collider.transform.position, 4.0f, zoneLayerMask);

                if (zoneColliders.Length > 0)
                {
                    targetZone = zoneColliders[0].GetComponent<PlacementZone>();
                }

                if (targetZone != null)
                {
                    isValidZone = true;
                }
            }

            // 6. 최종 배치 타당성 검증 (PlacementValidator)
            if (isValidZone && targetZone != null && PlacementValidator.IsValidPlacement(targetPosition, targetZone, gBoxSize, heldItem.gameObject))
            {
                currentPreviewPosition = targetPosition;
                isPreviewValid = true;

                if (dynamicPreviewInstance != null)
                {
                    dynamicPreviewInstance.transform.position = targetPosition;
                    dynamicPreviewInstance.transform.localScale = heldItem.transform.localScale * 2.0f; // 플레이어 스케일이 2라서 그거 맞춰서 한거임
                    dynamicPreviewInstance.transform.rotation = Quaternion.identity;
                    dynamicPreviewInstance.SetActive(true);
                }
                return;
            }
        }

        // 레이가 빗나가거나 검증에 실패했을 때 프리뷰 숨김 처리
        isPreviewValid = false;
        if (dynamicPreviewInstance != null)
        {
            dynamicPreviewInstance.SetActive(false);
        }
    }

    private void CreateDynamicPreview(GameObject originalObj)
    {
        ClearPreview();
        if (originalObj == null || previewMaterial == null) return;

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
        }

        foreach (var col in dynamicPreviewInstance.GetComponentsInChildren<Collider>())
        {
            Destroy(col);
        }

        dynamicPreviewInstance.SetActive(false);
    }

    public void ClearPreview()
    {
        isPreviewValid = false;
        if (dynamicPreviewInstance != null)
        {
            Destroy(dynamicPreviewInstance);
            dynamicPreviewInstance = null;
        }
    }
}