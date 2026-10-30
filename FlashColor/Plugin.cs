using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FlashColor;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/flashcolor";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    // L'image du jeu rendue par notre shader : réglages neutres, lumières fortes coupées comme le fait le jeu.
    private static readonly GradeSettings GameImage = new() { HighlightRolloff = false };

    private readonly WindowSystem windowSystem = new("FlashColor");
    private readonly MainWindow mainWindow;
    private ShaderParams sentParams;

    public Plugin()
    {
        Configuration = LoadConfiguration();
        Loc.Update(Configuration.Language);

        sentParams = ShaderParams.From(Configuration.Grade);
        Replacer = new ToneMapReplacer(
            DataManager.GetFile(ToneMapReplacer.GameShaderPath)?.Data, DataManager.GetFile(ToneMapReplacer.ColorFilterShaderPath)?.Data, sentParams)
        {
            Enabled = Configuration.Enabled,
        };

        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);

        // Dalamud cache les fenêtres des plugins en GPose ; on y règle les couleurs aussi, la fenêtre reste donc ouverte.
        // Masquer l'interface du jeu la cache toujours.
        PluginInterface.UiBuilder.DisableGposeUiHide = true;

        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += mainWindow.Toggle;
        PluginInterface.LanguageChanged += OnDalamudLanguageChanged;

        RegisterCommand();
    }

    public Configuration Configuration { get; }

    internal ToneMapReplacer Replacer { get; }

    /// <summary>Montrer l'image du jeu (bouton maintenu dans la fenêtre).</summary>
    internal bool ShowOriginal { get; set; }

    internal FlashEffect Flash { get; } = new();

    /// <summary>Les réglages partent au shader à la prochaine image ; <paramref name="save"/> : les écrire sur le disque.</summary>
    internal void ApplyGrade(bool save)
    {
        if (save)
            Configuration.Save();
    }

    internal void StartFlash(bool gameImage) => Flash.Start(Environment.TickCount64, Configuration.FlashStrength, gameImage);

    internal void SetEnabled(bool enabled)
    {
        Configuration.Enabled = enabled;
        Replacer.Enabled = enabled;
        Configuration.Save();
    }

    public void SetLanguage(PluginLanguage language)
    {
        Configuration.Language = language;
        Configuration.Save();
        RefreshLanguage();
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.LanguageChanged -= OnDalamudLanguageChanged;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= mainWindow.Toggle;
        windowSystem.RemoveAllWindows();
        Replacer.Dispose();
    }

    // Un fichier de configuration illisible ne doit pas empêcher le plugin de se charger : on repart des réglages par défaut.
    private static Configuration LoadConfiguration()
    {
        try
        {
            return PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        }
        catch (System.Exception e)
        {
            Log.Error(e, "Could not read the configuration, falling back to default settings.");
            return new Configuration();
        }
    }

    // À chaque image : les réglages, ou l'image du jeu, avec le flash éventuel. Hors flash, l'image du jeu est celle
    // de son propre shader ; pendant un flash « image du jeu », c'est le nôtre avec des réglages neutres (même calcul).
    private void OnFrameworkUpdate(IFramework framework)
    {
        Replacer.RetryHook();

        var now = Environment.TickCount64;
        var flash = Flash.Current(now);
        var gameImage = flash?.GameImage ?? ShowOriginal;
        Replacer.Bypass = gameImage && flash == null;

        var parameters = ShaderParams.From(gameImage ? GameImage : Configuration.Grade);
        parameters.Gain *= Flash.Gain(now);

        // En GPose, un filtre choisi dans le jeu passe avant celui de FlashColor (les deux ne s'empilent pas).
        if (ClientState.IsGPosing && GameMemory.GPoseFilterChosen())
            parameters.Filter = default;
        if (parameters != sentParams)
        {
            sentParams = parameters;
            Replacer.SetParams(parameters);
        }
    }

    private void OnCommand(string command, string arguments) => mainWindow.Toggle();

    private static string CommandHelp => Loc.T(
        "/flashcolor: open the contrast and color settings.",
        "/flashcolor : ouvrir les réglages de contraste et de couleurs.");

    private void OnDalamudLanguageChanged(string languageCode)
    {
        if (Configuration.Language == PluginLanguage.Auto)
            RefreshLanguage();
    }

    private void RefreshLanguage()
    {
        Loc.Update(Configuration.Language);
        CommandManager.RemoveHandler(CommandName);
        RegisterCommand();
    }

    private void RegisterCommand()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) { HelpMessage = CommandHelp });
    }
}
