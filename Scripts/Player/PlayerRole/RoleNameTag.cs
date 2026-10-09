using TMPro;
using UnityEngine;

public class RoleNameTag : MonoBehaviour
{
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private RoleManager roleManager;
    [SerializeField] private PlayerData playerData; // 변경

    private void Awake()
    {
        if (roleManager == null)
        {
            roleManager = GetComponentInParent<RoleManager>();
        }
        if (playerData == null)
        {
            playerData = GetComponentInParent<PlayerData>();
        }
    }

    private void OnEnable()
    {
        if (roleManager != null)
        {
            roleManager.RoleChanged += HandleRoleChanged;
        }
        if (playerData != null)
        {
            playerData.DisplayNameChanged += RefreshTag;
        }

        RefreshTag();
    }

    private void OnDisable()
    {
        if (roleManager != null)
        {
            roleManager.RoleChanged -= HandleRoleChanged;
        }
        if (playerData != null)
        {
            playerData.DisplayNameChanged -= RefreshTag;
        }
    }

    private void HandleRoleChanged(PlayerRole role) => RefreshTag();

    private void RefreshTag()
    {
        if (_nameText == null) return;

        string displayName = playerData != null ? playerData.GetDisplayName() : string.Empty;
        _nameText.text = string.IsNullOrEmpty(displayName) ? "..." : displayName;
    }
}