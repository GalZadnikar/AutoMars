using UnityEngine;

/// What's written on a disk. Each program type is its own asset (Create > Trash Game > Disk Programs).
public abstract class DiskProgram : ScriptableObject
{
    public string displayName = "Program";
    [Tooltip("Short text shown on the disk icon and above the bot")]
    public string shortName = "PRG";
    [TextArea] public string description;
    public Color color = new Color(1f, 0.78f, 0.2f);

    public abstract BotBehaviour CreateBehaviour(ScoutBot bot);
}
