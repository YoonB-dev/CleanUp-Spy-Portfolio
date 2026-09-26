using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 모든 아이템이 공통으로 가지는 정보 (인벤토리 아이콘, 표시 이름 등).
/// 상자/도구는 이 에셋을 만들어 PickupItem에 연결하고, 쓰레기는 이를 상속한 TrashData를 사용한다.
/// 로드된 에셋은 itemID로 조회할 수 있다 (RPC로 ID만 받은 클라이언트가 데이터를 찾을 때 사용).
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

    // 로드된 ItemData를 itemID로 찾기 위한 목록 (에셋이 로드될 때 OnEnable에서 등록됨)
    private static readonly Dictionary<string, ItemData> Registry = new Dictionary<string, ItemData>();

    /// <summary>
    /// itemID로 로드된 아이템 데이터를 찾습니다. 프리팹/씬에서 참조되어 로드된 에셋만 찾을 수 있습니다.
    /// </summary>
    public static bool TryGetById<T>(string id, out T data) where T : ItemData
    {
        data = null;
        if (string.IsNullOrEmpty(id)) return false;

        if (Registry.TryGetValue(id, out ItemData found) && found != null)
        {
            data = found as T;
        }
        return data != null;
    }

    protected virtual void OnEnable() => Register();
    protected virtual void OnDisable() => Unregister();
    protected virtual void OnValidate() => Register();   // 인스펙터에서 ID를 바꿔도 새 ID로 다시 등록

    private void Register()
    {
        Unregister();
        if (string.IsNullOrEmpty(itemID)) return;

        if (Registry.TryGetValue(itemID, out ItemData existing) && existing != null && existing != this)
        {
            Debug.LogWarning($"[ItemData] itemID '{itemID}'가 '{existing.name}'와 '{name}'에서 중복됩니다.", this);
            return;
        }
        Registry[itemID] = this;
    }

    private void Unregister()
    {
        // ID가 바뀌었을 수 있으므로 이 에셋을 가리키는 항목을 모두 지움
        List<string> keysToRemove = null;
        foreach (KeyValuePair<string, ItemData> pair in Registry)
        {
            if (pair.Value != this) continue;
            (keysToRemove ??= new List<string>()).Add(pair.Key);
        }
        if (keysToRemove == null) return;

        foreach (string key in keysToRemove)
        {
            Registry.Remove(key);
        }
    }
}
