/// <summary>
/// Carries the session across the scene load from Launch_Scene into MR_Scene: the researcher's
/// per-participant config (published by <see cref="StudySetup"/> in Awake) and the task the
/// participant picked (set by <see cref="LaunchMenu"/> when they press START).
///
/// WHY A STATIC AND NOT A DontDestroyOnLoad OBJECT: a carried-over GameObject would have to
/// exist in Launch_Scene for MR_Scene to work, which would break opening MR_Scene directly
/// in the editor — the thing developers do fifty times a day. A static survives the load with
/// nothing to wire, and when MR_Scene is opened on its own <see cref="HasConfig"/> and
/// <see cref="HasTask"/> are simply false and <see cref="StudyControlPanel"/> falls back to its
/// own inspector values. So the scene stays runnable standalone.
///
/// Statics persist for the lifetime of the PLAYER, not the scene, but they do NOT survive a
/// domain reload in the editor (entering play mode resets them) — which is the behaviour we
/// want: every play-mode run starts clean until Launch_Scene populates it.
/// </summary>
public static class StudySession
{
    // ---- Per-participant config, from StudySetup ----

    /// <summary>True once <see cref="StudySetup"/> has published a config for this run.</summary>
    public static bool HasConfig { get; private set; }

    /// <summary>The researcher's per-participant config. Only valid if <see cref="HasConfig"/>.</summary>
    public static StudyConfig Config { get; private set; }

    /// <summary>Called by <see cref="StudySetup"/> in Awake, before anything reads the session.</summary>
    public static void SetConfig(StudyConfig config)
    {
        Config = config;
        HasConfig = config != null;
    }

    // ---- Per-run task, from the launch menu ----

    /// <summary>True once the launch menu has confirmed a task for this run.</summary>
    public static bool HasTask { get; private set; }

    /// <summary>The task the participant picked. Only valid if <see cref="HasTask"/>.</summary>
    public static StudyControlPanel.StudyTask Task { get; private set; }

    /// <summary>Called by <see cref="LaunchMenu"/> when the participant hits START.</summary>
    public static void SetTask(StudyControlPanel.StudyTask task)
    {
        Task = task;
        HasTask = true;
    }

    /// <summary>Forget everything (e.g. if you ever add a return-to-menu path).</summary>
    public static void Clear()
    {
        HasTask = false;
        HasConfig = false;
        Config = null;
    }
}
