namespace PCStore.API.DTOs;

/// <summary>One permission setting for a role, module, and action.</summary>
public sealed record RolePermissionGrantRequest(string Module, string Action, bool IsAllowed);

/// <summary>Updates all permission settings for one role.</summary>
public sealed record UpdateRolePermissionsRequest(string Role, IReadOnlyList<RolePermissionGrantRequest> Permissions);

/// <summary>Describes a module available in the role permission matrix.</summary>
public sealed record PermissionModuleResponse(string Key, string Label);

/// <summary>Describes an action available in the role permission matrix.</summary>
public sealed record PermissionActionResponse(string Key, string Label);

/// <summary>One persisted permission setting for the selected role.</summary>
public sealed record RolePermissionResponse(string Module, string Action, bool IsAllowed);

/// <summary>Permission matrix and its fixed module/action definitions.</summary>
public sealed record RolePermissionMatrixResponse(
    string Role,
    bool IsProtected,
    IReadOnlyList<PermissionModuleResponse> Modules,
    IReadOnlyList<PermissionActionResponse> Actions,
    IReadOnlyList<RolePermissionResponse> Permissions);
