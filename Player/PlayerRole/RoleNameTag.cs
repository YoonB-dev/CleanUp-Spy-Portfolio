using TMPro;
using UnityEngine;

public class RoleNameTag : MonoBehaviour
{
    [SerializeField] private TMP_Text roleText;
    [SerializeField] private RoleManager roleManager;

    private void Awake()
    {
        if (roleManager == null)
        {
            roleManager = GetComponentInParent<RoleManager>();
        }
    }

    private void OnEnable()
    {
        if (roleManager == null)
        {
            return;
        }

        roleManager.RoleChanged += HandleRoleChanged;
        HandleRoleChanged(roleManager.CurrentRole);
    }

    private void OnDisable()
    {
        if (roleManager == null)
        {
            return;
        }

        roleManager.RoleChanged -= HandleRoleChanged;
    }

    private void HandleRoleChanged(PlayerRole role)
    {
        if (roleText == null)
        {
            return;
        }

        roleText.text = role switch
        {
            PlayerRole.Citizen => "Citizen",
            PlayerRole.Mafia => "Mafia",
            _ => "None"
        };
    }
}