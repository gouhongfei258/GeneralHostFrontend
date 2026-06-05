namespace GeneralHostFrontend.Core.Security;

public enum WorkspaceKind
{
    Operator,
    Maintenance,
    Engineering,
    Administration
}

public sealed record Permission(string Key, string DisplayName);

public sealed record RoleDefinition(
    string Name,
    WorkspaceKind DefaultWorkspace,
    IReadOnlySet<string> Permissions);

public sealed record UserSession(
    string UserName,
    IReadOnlySet<string> RoleNames,
    IReadOnlySet<string> Permissions,
    WorkspaceKind Workspace,
    DateTimeOffset LoginAt)
{
    public bool HasPermission(string permission) => Permissions.Contains(permission);
}

public static class KnownPermissions
{
    public const string ViewDashboard = "dashboard.view";
    public const string ViewIo = "io.view";
    public const string ForceIo = "io.force";
    public const string EditRecipe = "recipe.edit";
    public const string ViewAlarms = "alarm.view";
    public const string EditSettings = "settings.edit";
    public const string ManageUsers = "users.manage";
    public const string EngineeringTools = "engineering.tools";
}

public interface IAuthorizationService
{
    UserSession Current { get; }

    bool Can(string permission);

    WorkspaceKind ResolveWorkspace();
}
