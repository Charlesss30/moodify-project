namespace mood_recommendation.Services;

public static class AdminRoles
{
    public const string Name = "Admin";
    public static bool IsAdmin(string role) => role.Equals(Name, StringComparison.OrdinalIgnoreCase);
}
