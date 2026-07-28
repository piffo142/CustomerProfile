namespace SIG.ClientCard.App.Services;

/// <summary>
/// Supabase project settings. Empty until Phase 1/2 — the app then runs
/// local-only and the outbox simply accumulates. Project should live in
/// eu-west-2 (London) for UK salons: special category data stays in-region.
/// </summary>
public static class SupabaseConfig
{
    public const string Url = ""; // e.g. https://xyzcompany.supabase.co/
    public const string AnonKey = "";

    public static bool IsConfigured => !string.IsNullOrEmpty(Url) && !string.IsNullOrEmpty(AnonKey);
}
