using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

public class RecordsModal : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button dimButton;
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private GameObject emptyText;
    [SerializeField] private LocalizeStringEvent sectionTemplate;
    [SerializeField] private RecordRow rowTemplate;

    private void Awake()
    {
        openButton.onClick.AddListener(Open);
        closeButton.onClick.AddListener(Close);
        dimButton.onClick.AddListener(Close);
        root.SetActive(false);

        PlayStats stats = PlayerSave.Data.stats;

        AddSection("records_section_results");
        AddRow("records_games", Count(stats.totalGamesPlayed));
        AddRow("records_mafia", WinRate(stats.mafiaWins, stats.mafiaPlayed));
        AddRow("records_citizen", WinRate(stats.citizenWins, stats.citizenPlayed));

        AddSection("records_section_activity");
        AddRow("records_clean", Count(stats.totalCleanCount));
        AddRow("records_trash", Count(stats.totalTrashCount));
        AddRow("records_organized", Count(stats.totalItemsOrganized));
        AddRow("records_messup", Count(stats.totalItemsMessUp));
    }

    private void Update()
    {
        if (root.activeSelf && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
    }

    private void Open()
    {
        bool hasRecords = PlayerSave.Data.stats.totalGamesPlayed > 0;
        scroll.gameObject.SetActive(hasRecords);
        emptyText.SetActive(!hasRecords);
        scroll.content.anchoredPosition = Vector2.zero;

        root.SetActive(true);
    }

    private void Close() => root.SetActive(false);

    private void AddSection(string key)
    {
        LocalizeStringEvent section = Instantiate(sectionTemplate, sectionTemplate.transform.parent);
        section.StringReference.TableEntryReference = key;
        section.gameObject.SetActive(true);
    }

    private void AddRow(string key, string value)
    {
        RecordRow row = Instantiate(rowTemplate, rowTemplate.transform.parent);
        row.Set(key, value);
        row.gameObject.SetActive(true);
    }

    private static string Count(int value) => value.ToString("N0");

    private static string WinRate(int wins, int played) =>
        played == 0 ? "-" : $"{wins * 100 / played}%  ({wins}/{played})";
}
