using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

/// <summary>
/// 표에 컴포넌트를 붙일 수 없는 곳에서 쓰는 문구 조회.
/// 드롭다운 항목 목록이나 설명 패널처럼 코드가 직접 문자열을 만들어야 하는 자리다.
/// </summary>
public static class SettingsText
{
    public const string TABLE = "Settings";

    /// <summary>표에 없으면 넘긴 값을 그대로 돌려준다. 숫자 같은 번역이 필요 없는 항목도 통과시킨다</summary>
    public static string Translate(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        StringTable table = LocalizationSettings.StringDatabase.GetTable(TABLE);
        StringTableEntry entry = table != null ? table.GetEntry(key) : null;

        return entry != null ? entry.GetLocalizedString() : key;
    }
}
