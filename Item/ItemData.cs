using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 모든 아이템이 공통으로 가지는 정보 (인벤토리 아이콘, 표시 이름 등).
/// 상자/도구는 이 에셋을 만들어 PickupItem에 연결하고, 쓰레기는 이를 상속한 TrashData를 사용한다.
/// </summary>
[CreateAssetMenu(fileName = "ItemData", menuName = "CleanUpMafia/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("Item Info")]
    [Tooltip("아이템 고유 ID (네트워크 전송/조회용, 다른 아이템과 겹치지 않게)")]
    public string itemID;
    [FormerlySerializedAs("trashName")] // 기존 TrashData.trashName 값 유지
    public string displayName;
    [Tooltip("인벤토리 슬롯에 표시될 아이콘")]
    public Sprite icon;
}
