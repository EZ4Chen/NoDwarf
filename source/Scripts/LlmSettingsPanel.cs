namespace NoDwarf;

// AI service configuration is shared by title and colony through SettingsWindow.
public partial class Startup
{
    public override void _ExitTree()=>_settingsWindow?.Close();
}
