using System;

/// <summary>
/// 공개용 포트폴리오용 간단 예시:
/// 서버가 역할을 배정하고, 클라이언트는 상태를 읽기만 하는 구조.
/// 실제 프로젝트의 RoleManager / RoleAssignmentManager의 핵심 의도를 요약한 버전.
/// </summary>
public enum PlayerRole : byte
{
    None,
    Citizen,
    Mafia
}

public class RoleSystemExample
{
    public event Action<PlayerRole> RoleChanged;

    private PlayerRole _currentRole = PlayerRole.None;

    public PlayerRole CurrentRole => _currentRole;

    public void AssignRole(PlayerRole newRole)
    {
        if (_currentRole == newRole) return;
        _currentRole = newRole;
        RoleChanged?.Invoke(_currentRole);
    }
}
