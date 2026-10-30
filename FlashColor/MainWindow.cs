using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace FlashColor;

/// <summary>
/// Fenêtre de réglages : état du remplacement, comparaison avec l'image d'origine, préréglages, puis les réglages
/// rangés en onglets (couleurs, zones, couleur par couleur, détails, flash).
/// </summary>
internal sealed class MainWindow : Window
{
    public const string Key = "###FlashColorMain";

    private const long NoticeMs = 4000;

    // Rouge, orange, jaune, vert, cyan, bleu, violet, magenta : mêmes teintes que dans le shader.
    private static readonly float[] ColorHues = [0f, 30f, 60f, 120f, 180f, 240f, 270f, 300f];

    private readonly Plugin plugin;

    // Pendant le dessin des réglages : un curseur a bougé, et un curseur a été relâché (on enregistre alors sur le disque).
    private bool changed;
    private bool released;

    // Préréglage du joueur choisi ou enregistré en dernier : c'est lui que « … » met à jour, renomme ou supprime.
    private string? currentUserPreset;
    private string presetName = string.Empty;
    private bool openNameDialog;
    private bool renaming;
    private bool openDeleteDialog;

    private string? notice;
    private long noticeUntil;

    public MainWindow(Plugin plugin)
        : base("FlashColor" + Key)
    {
        this.plugin = plugin;
        Size = new Vector2(540, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(440, 360), MaximumSize = new Vector2(float.MaxValue) };
    }

    private Configuration Config => plugin.Configuration;

    private GradeSettings Grade => Config.Grade;

    private static string RightClickHint => Loc.T("Right-click: default value.", "Clic droit : valeur par défaut.");

    public override void OnClose()
    {
        plugin.ShowOriginal = false;
    }

    public override void Draw()
    {
        DrawStatus();
        ImGui.Spacing();

        var enabled = Config.Enabled;
        if (ImGui.Checkbox(Loc.T("Enable FlashColor", "Activer FlashColor"), ref enabled))
            plugin.SetEnabled(enabled);

        ImGui.SameLine();
        ImGui.Button(Loc.T("Hold: original image", "Maintenir : image d'origine"));
        plugin.ShowOriginal = ImGui.IsItemActive();
        Tooltip(Loc.T(
            "Shows the game's own image while the button is held, to compare.",
            "Montre l'image du jeu tant que le bouton est maintenu, pour comparer."));

        ImGui.Separator();
        changed = released = false;
        using (ImRaii.Disabled(!Config.Enabled))
        {
            DrawPresets();
            ImGui.Spacing();
            DrawTabs();
        }

        // Le relâchement arrive souvent sur une image où le curseur n'a pas bougé : il suffit pour enregistrer.
        if (changed || released)
            plugin.ApplyGrade(released);

        ImGui.Separator();
        DrawLanguage();
    }

    private void DrawStatus()
    {
        var replacer = plugin.Replacer;
        switch (replacer.State)
        {
            case ReplacerState.Active:
                ImGui.TextColored(ImGuiColors.HealerGreen, Config.Enabled
                    ? Loc.T("Active: the game's tone mapping is replaced.", "Actif : le tonemap du jeu est remplacé.")
                    : Loc.T("Ready.", "Prêt."));
                break;
            case ReplacerState.Waiting when !Config.Enabled:
                ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T("Off: the image is the game's.", "Désactivé : l'image est celle du jeu."));
                break;
            case ReplacerState.Waiting:
                ImGui.TextColored(ImGuiColors.DalamudGrey, replacer.EverReplaced
                    ? Loc.T("The game isn't drawing the world right now.", "Le jeu n'affiche pas le monde en ce moment.")
                    : Loc.T("Waiting for the game's first image (title screen, loading)…", "En attente de la première image du jeu (écran titre, chargement)…"));
                break;
            case ReplacerState.Hooking:
                ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T(
                    "Dalamud couldn't hook FlashColor into the game yet: trying again shortly…",
                    "Dalamud n'a pas encore pu brancher FlashColor sur le jeu : nouvel essai dans un instant…"));
                break;
            case ReplacerState.GameShaderChanged:
                Warning(Loc.T(
                    $"Off: a game update changed the tone mapping shader (0x{replacer.GameShaderCrc:X8}). FlashColor needs an update.",
                    $"Désactivé : une mise à jour du jeu a changé le shader du tonemap (0x{replacer.GameShaderCrc:X8}). FlashColor doit être mis à jour."));
                break;
            case ReplacerState.Failed:
                Warning(Loc.T(
                    "Stopped after an error: the image is the game's. Details in dalamud.log.",
                    "Arrêté après une erreur : l'image est celle du jeu. Détails dans dalamud.log."));
                break;
        }
    }

    // ---------------------------------------------------------------- Préréglages

    private void DrawPresets()
    {
        var builtIn = Presets.Find(Grade);
        var mine = builtIn == null ? UserPresets.Matching(Config.UserPresets, Grade) : null;
        var label = builtIn?.Name ?? mine?.Name ?? Loc.T("Custom", "Personnalisé");

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(Loc.T("Preset", "Préréglage"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X);
        if (ImGui.BeginCombo("##Preset", label))
        {
            var group = PresetGroup.Game;
            foreach (var preset in Presets.All)
            {
                if (preset.Group != group)
                {
                    group = preset.Group;
                    ImGui.Separator();
                    ImGui.TextColored(ImGuiColors.DalamudGrey, group switch
                    {
                        PresetGroup.Measured => Loc.T("Measured on ReShade presets", "Mesurés sur des presets ReShade"),
                        PresetGroup.GPose => Loc.T("GPose filters", "Filtres de la GPose"),
                        _ => Loc.T("Ambiances", "Ambiances"),
                    });
                }

                if (ImGui.Selectable(preset.Name, preset == builtIn))
                    Apply(preset.Create(), null);
                Tooltip(preset.Hint);
            }

            if (Config.UserPresets.Count > 0)
            {
                ImGui.Separator();
                ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T("My presets", "Mes préréglages"));
            }

            foreach (var preset in Config.UserPresets)
            {
                if (ImGui.Selectable(preset.Name + "##User", preset == mine))
                    Apply(preset.Settings.Clone(), preset.Name);
            }

            ImGui.EndCombo();
        }
        else if (builtIn != null)
        {
            Tooltip(builtIn.Hint);
        }

        ImGui.SameLine();
        if (ImGui.Button("…##PresetMenu", new Vector2(ImGui.GetFrameHeight())))
            ImGui.OpenPopup("##PresetActions");
        Tooltip(Loc.T("Save, rename, delete, copy or paste presets.", "Enregistrer, renommer, supprimer, copier ou coller des préréglages."));

        DrawPresetMenu();
        DrawNameDialog();
        DrawDeleteDialog();

        if (notice != null && Environment.TickCount64 < noticeUntil)
            ImGui.TextColored(ImGuiColors.DalamudGrey, notice);
    }

    private void Apply(GradeSettings settings, string? userPreset)
    {
        Config.Grade = settings;
        currentUserPreset = userPreset;
        changed = released = true;
    }

    private void DrawPresetMenu()
    {
        if (!ImGui.BeginPopup("##PresetActions"))
            return;

        var current = currentUserPreset != null ? UserPresets.Find(Config.UserPresets, currentUserPreset) : null;

        if (ImGui.Selectable(Loc.T("Save as a new preset…", "Enregistrer comme nouveau préréglage…")))
        {
            presetName = string.Empty;
            renaming = false;
            openNameDialog = true;
        }

        if (current != null)
        {
            using (ImRaii.Disabled(current.Settings.SameAs(Grade)))
            {
                if (ImGui.Selectable(Loc.T($"Update “{current.Name}” with the current settings", $"Mettre à jour « {current.Name} » avec les réglages actuels")))
                {
                    UserPresets.Save(Config.UserPresets, current.Name, Grade);
                    Config.Save();
                    Notify(Loc.T($"“{current.Name}” updated.", $"« {current.Name} » mis à jour."));
                }
            }

            if (ImGui.Selectable(Loc.T($"Rename “{current.Name}”…", $"Renommer « {current.Name} »…")))
            {
                presetName = current.Name;
                renaming = true;
                openNameDialog = true;
            }

            if (ImGui.Selectable(Loc.T($"Delete “{current.Name}”…", $"Supprimer « {current.Name} »…")))
                openDeleteDialog = true;
        }

        ImGui.Separator();
        if (ImGui.Selectable(Loc.T("Copy the settings", "Copier les réglages")))
        {
            ImGui.SetClipboardText(SettingsText.Write(Grade));
            Notify(Loc.T("Settings copied: paste them anywhere to share them.", "Réglages copiés : colle-les où tu veux pour les partager."));
        }

        if (ImGui.Selectable(Loc.T("Paste settings", "Coller des réglages")))
        {
            var pasted = SettingsText.Read(ImGui.GetClipboardText());
            if (pasted == null)
            {
                Notify(Loc.T("The clipboard doesn't contain FlashColor settings.", "Le presse-papiers ne contient pas de réglages FlashColor."));
            }
            else
            {
                Apply(pasted, null);
                Notify(Loc.T("Settings pasted.", "Réglages collés."));
            }
        }

        ImGui.EndPopup();
    }

    private void DrawNameDialog()
    {
        // Ouvert après la fermeture du menu : un popup ne s'ouvre pas depuis un autre qui se ferme.
        if (openNameDialog)
        {
            ImGui.OpenPopup("##PresetName");
            openNameDialog = false;
        }

        if (!ImGui.BeginPopup("##PresetName"))
            return;

        ImGui.TextUnformatted(Loc.T("Preset name", "Nom du préréglage"));
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
        var entered = ImGui.InputText("##Name", ref presetName, 64, ImGuiInputTextFlags.EnterReturnsTrue);

        var name = presetName.Trim();
        var other = UserPresets.Find(Config.UserPresets, name);
        var taken = other != null && !(renaming && string.Equals(other.Name, currentUserPreset, StringComparison.OrdinalIgnoreCase));
        if (taken && renaming)
            ImGui.TextColored(ImGuiColors.DalamudOrange, Loc.T("This name is already used.", "Ce nom est déjà pris."));
        else if (taken)
            ImGui.TextColored(ImGuiColors.DalamudOrange, Loc.T("Replaces the preset with this name.", "Remplace le préréglage qui porte ce nom."));

        var valid = name.Length > 0 && !(taken && renaming);
        using (ImRaii.Disabled(!valid))
        {
            if (ImGui.Button(renaming ? Loc.T("Rename", "Renommer") : Loc.T("Save", "Enregistrer")) || (entered && valid))
            {
                if (renaming)
                    UserPresets.Rename(Config.UserPresets, currentUserPreset!, name);
                else
                    UserPresets.Save(Config.UserPresets, name, Grade);

                currentUserPreset = UserPresets.Find(Config.UserPresets, name)?.Name;
                Config.Save();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Cancel", "Annuler")))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void DrawDeleteDialog()
    {
        if (openDeleteDialog)
        {
            ImGui.OpenPopup("##PresetDelete");
            openDeleteDialog = false;
        }

        if (!ImGui.BeginPopup("##PresetDelete"))
            return;

        ImGui.TextUnformatted(Loc.T($"Delete “{currentUserPreset}”?", $"Supprimer « {currentUserPreset} » ?"));
        if (ImGui.Button(Loc.T("Delete", "Supprimer")) && currentUserPreset != null)
        {
            UserPresets.Delete(Config.UserPresets, currentUserPreset);
            currentUserPreset = null;
            Config.Save();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Cancel", "Annuler")))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void Notify(string text)
    {
        notice = text;
        noticeUntil = Environment.TickCount64 + NoticeMs;
    }

    // ---------------------------------------------------------------- Onglets

    private void DrawTabs()
    {
        if (!ImGui.BeginTabBar("##Tabs"))
            return;

        if (ImGui.BeginTabItem(Loc.T("Colors", "Couleurs") + "###TabColors"))
        {
            DrawColorsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Zones", "Zones") + "###TabZones"))
        {
            DrawZonesTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Per color", "Par couleur") + "###TabPerColor"))
        {
            DrawPerColorTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Details", "Détails") + "###TabDetails"))
        {
            DrawDetailsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("GPose filter", "Filtre GPose") + "###TabGPoseFilter"))
        {
            DrawGPoseFilterTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Flash", "Flash") + "###TabFlash"))
        {
            DrawFlashTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawColorsTab()
    {
        if (BeginSliders("##Color"))
        {
            var exposure = Grade.Exposure;
            if (Slider(Loc.T("Exposure", "Exposition"), ref exposure, 1f, -2f, 2f, Loc.T("%+.2f EV", "%+.2f IL"), 0f,
                    Loc.T("+1 doubles the light, -1 halves it.", "+1 double la lumière, -1 la divise par deux.")))
                Grade.Exposure = exposure;

            var contrast = Grade.Contrast;
            if (Slider(Loc.T("Contrast", "Contraste"), ref contrast, 100f, 50f, 200f, "%.0f %%", 1f,
                    Loc.T(
                        "Darker shadows and brighter lights, around middle grey. Colors keep their hue.",
                        "Ombres plus sombres et lumières plus claires, autour du gris moyen. Les couleurs gardent leur teinte.")))
                Grade.Contrast = contrast;

            var saturation = Grade.Saturation;
            if (Slider(Loc.T("Saturation", "Saturation"), ref saturation, 100f, 0f, 200f, "%.0f %%", 1f,
                    Loc.T("All colors alike. 0: black and white.", "Toutes les couleurs pareil. 0 : noir et blanc.")))
                Grade.Saturation = saturation;

            var vibrance = Grade.Vibrance;
            if (Slider(Loc.T("Vibrance", "Vibrance"), ref vibrance, 100f, -100f, 100f, "%+.0f %%", 0f,
                    Loc.T(
                        "Mostly the dull colors: livens up the image without burning colors that are already vivid.",
                        "Surtout les couleurs ternes : ravive l'image sans brûler les couleurs déjà vives.")))
                Grade.Vibrance = vibrance;

            var temperature = Grade.Temperature;
            if (Slider(Loc.T("Temperature", "Température"), ref temperature, 1f, -100f, 100f, "%.0f", 0f,
                    Loc.T("Left: colder (blue). Right: warmer (orange).", "À gauche : plus froid (bleu). À droite : plus chaud (orange).")))
                Grade.Temperature = temperature;

            var tint = Grade.Tint;
            if (Slider(Loc.T("Tint", "Teinte"), ref tint, 1f, -100f, 100f, "%.0f", 0f,
                    Loc.T("Left: greener. Right: more magenta.", "À gauche : plus vert. À droite : plus magenta.")))
                Grade.Tint = tint;

            ImGui.EndTable();
        }

        ImGui.Spacing();
        var rolloff = Grade.HighlightRolloff;
        if (ImGui.Checkbox(Loc.T("Soften bright lights instead of clipping them", "Adoucir les lumières fortes au lieu de les couper"), ref rolloff))
        {
            Grade.HighlightRolloff = rolloff;
            changed = released = true;
        }

        Tooltip(Loc.T(
            "Flames and bright effects keep their gradient and color instead of becoming flat patches.",
            "Les flammes et les effets lumineux gardent leur dégradé et leur couleur au lieu de devenir des taches plates."));

        using (ImRaii.Disabled(!Grade.HighlightRolloff))
        {
            if (BeginSliders("##Rolloff"))
            {
                var start = Grade.RolloffStart;
                if (Slider(Loc.T("Starts at", "Début"), ref start, 100f, 50f, 99f, "%.0f %%", 0.8f,
                        Loc.T(
                            "Brightness from which lights are softened. Lower: softer, but bright areas get a bit dimmer.",
                            "Luminosité à partir de laquelle les lumières sont adoucies. Plus bas : plus doux, mais les zones claires baissent un peu.")))
                    Grade.RolloffStart = start;

                var keepColor = Grade.KeepHighlightColor;
                if (Slider(Loc.T("Color of bright lights", "Couleur des lumières fortes"), ref keepColor, 100f, 0f, 100f, "%.0f %%", 0.2f,
                        Loc.T(
                            "0: bright lights whiten naturally, color by color, like film (an orange flame turns yellow, then white). 100: they keep their full color.",
                            "0 : les lumières fortes blanchissent naturellement, couleur par couleur, comme sur une pellicule (une flamme orange passe au jaune puis au blanc). 100 : elles gardent toute leur couleur.")))
                    Grade.KeepHighlightColor = keepColor;

                ImGui.EndTable();
            }
        }

        ImGui.Spacing();
        if (BeginSliders("##KeepDetails"))
        {
            var keepDetails = Grade.KeepDetails;
            if (Slider(Loc.T("Keep details", "Garder les détails"), ref keepDetails, 100f, 0f, 100f, "%.0f %%", 1f,
                    Loc.T(
                        "Contrast, colors and the softening of bright lights act on the average light around each pixel; the texture (the pixel's difference from that average) is put back afterwards. 100: all the texture is kept, even in flames.",
                        "Le contraste, les couleurs et l'adoucissement agissent sur la lumière moyenne autour de chaque pixel ; la texture (l'écart du pixel à cette moyenne) est remise ensuite. 100 : toute la texture est gardée, même dans les flammes.")))
                Grade.KeepDetails = keepDetails;

            ImGui.EndTable();
        }
    }

    private void DrawZonesTab()
    {
        ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T(
            "Each zone gets its own brightness and color. The zones overlap smoothly.",
            "Chaque zone reçoit sa luminosité et sa couleur. Les zones se recouvrent en douceur."));

        if (BeginSliders("##Zones"))
        {
            DrawZone(Loc.T("Shadows", "Ombres"), Grade.Shadows);
            DrawZone(Loc.T("Midtones", "Tons moyens"), Grade.Midtones);
            DrawZone(Loc.T("Lights", "Lumières"), Grade.Highlights);
            ImGui.EndTable();
        }

        if (ImGui.Button(Loc.T("Reset the zones", "Réinitialiser les zones")))
        {
            Grade.Shadows = new ToneZone();
            Grade.Midtones = new ToneZone();
            Grade.Highlights = new ToneZone();
            changed = released = true;
        }
    }

    private void DrawZone(string title, ToneZone zone)
    {
        using var id = ImRaii.PushId(title);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudYellow, title);

        var brightness = zone.Brightness;
        if (Slider(Loc.T("Brightness", "Luminosité"), ref brightness, 1f, -1f, 1f, Loc.T("%+.2f EV", "%+.2f IL"), 0f,
                Loc.T("Lightens or darkens this zone only.", "Éclaircit ou assombrit cette zone seulement.")))
            zone.Brightness = brightness;

        var hue = zone.Hue;
        if (Slider(Loc.T("Color", "Couleur"), ref hue, 1f, 0f, 360f, "%.0f°", 0f,
                Loc.T("The color given to this zone: 0 red, 60 yellow, 120 green, 180 cyan, 240 blue, 300 magenta.", "La couleur donnée à cette zone : 0 rouge, 60 jaune, 120 vert, 180 cyan, 240 bleu, 300 magenta."),
                swatch: ShaderParams.HueColor(zone.Hue)))
            zone.Hue = hue;

        var strength = zone.Strength;
        if (Slider(Loc.T("Strength", "Force"), ref strength, 100f, 0f, 100f, "%.0f %%", 0f,
                Loc.T("How much of this color the zone gets. 0: none.", "Quantité de cette couleur dans la zone. 0 : aucune.")))
            zone.Strength = strength;
    }

    private void DrawPerColorTab()
    {
        ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T(
            "Each color of the image, one by one. Greys are never affected.",
            "Chaque couleur de l'image, une par une. Les gris ne sont jamais touchés."));

        var names = new[]
        {
            Loc.T("Red", "Rouge"), Loc.T("Orange", "Orange"), Loc.T("Yellow", "Jaune"), Loc.T("Green", "Vert"),
            Loc.T("Cyan", "Cyan"), Loc.T("Blue", "Bleu"), Loc.T("Purple", "Violet"), Loc.T("Magenta", "Magenta"),
        };

        if (ImGui.BeginTable("##PerColor", 4))
        {
            ImGui.TableSetupColumn(Loc.T("Color", "Couleur"), ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn(Loc.T("Hue", "Teinte"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Loc.T("Saturation", "Saturation"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Loc.T("Brightness", "Luminosité"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            // Un fichier d'une autre version peut avoir moins de couleurs : on complète avant de régler.
            if (Grade.Colors.Length < GradeSettings.ColorCount)
                Grade.Colors = GradeSettings.NewColors();

            for (var i = 0; i < GradeSettings.ColorCount; i++)
            {
                using var id = ImRaii.PushId(i);
                var color = Grade.Colors[i];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Swatch(ShaderParams.HueColor(ColorHues[i]));
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(names[i]);

                ImGui.TableNextColumn();
                var hue = color.Hue;
                if (CellSlider("##Hue", ref hue, 1f, -30f, 30f, "%+.0f°", 0f,
                        Loc.T("Shifts this color towards its neighbors.", "Décale cette couleur vers ses voisines.")))
                    color.Hue = hue;

                ImGui.TableNextColumn();
                var saturation = color.Saturation;
                if (CellSlider("##Saturation", ref saturation, 100f, 0f, 200f, "%.0f %%", 1f,
                        Loc.T("0: this color turns grey.", "0 : cette couleur devient grise.")))
                    color.Saturation = saturation;

                ImGui.TableNextColumn();
                var luminance = color.Luminance;
                if (CellSlider("##Luminance", ref luminance, 1f, -1f, 1f, Loc.T("%+.2f EV", "%+.2f IL"), 0f,
                        Loc.T("Lightens or darkens this color only.", "Éclaircit ou assombrit cette couleur seulement.")))
                    color.Luminance = luminance;
            }

            ImGui.EndTable();
        }

        if (ImGui.Button(Loc.T("Reset the colors", "Réinitialiser les couleurs")))
        {
            Grade.Colors = GradeSettings.NewColors();
            changed = released = true;
        }
    }

    private void DrawDetailsTab()
    {
        ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T(
            "These settings read the neighboring pixels: a bit more work for the graphics card. The interface is never sharpened.",
            "Ces réglages lisent les pixels voisins : un peu plus de travail pour la carte graphique. L'interface n'est jamais touchée."));

        if (BeginSliders("##Details"))
        {
            var sharpness = Grade.Sharpness;
            if (Slider(Loc.T("Sharpness", "Netteté"), ref sharpness, 100f, 0f, 100f, "%.0f %%", 0f,
                    Loc.T("Sharper small details, limited around edges to avoid halos.", "Petits détails plus nets, limités autour des contours pour éviter les halos.")))
                Grade.Sharpness = sharpness;

            var clarity = Grade.Clarity;
            if (Slider(Loc.T("Clarity", "Clarté"), ref clarity, 100f, -100f, 100f, "%+.0f %%", 0f,
                    Loc.T(
                        "Local contrast: shapes stand out more, mostly in the midtones. Negative: softer, dreamier image.",
                        "Contraste local : les formes ressortent davantage, surtout dans les tons moyens. Négatif : image plus douce, plus rêveuse.")))
                Grade.Clarity = clarity;

            ImGui.EndTable();
        }
    }

    private void DrawGPoseFilterTab()
    {
        ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T(
            "The game's GPose filters (\"Color filter\"), exactly as in the game, everywhere. Like in GPose, the filter replaces the zone's own filter. It comes after your color settings. In GPose, a filter chosen in the game takes over.",
            "Les filtres de la GPose du jeu (« Filtre de couleurs »), à l'identique, partout. Comme en GPose, le filtre remplace celui de la zone. Il s'applique après tes réglages de couleurs. En GPose, un filtre choisi dans le jeu passe avant."));

        var chosen = GameFilters.Find(Grade.GameFilter);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(Loc.T("Filter", "Filtre"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##GPoseFilter", chosen?.Name ?? Loc.T("None", "Aucun")))
        {
            if (ImGui.Selectable(Loc.T("None", "Aucun"), chosen == null))
            {
                Grade.GameFilter = 0;
                changed = released = true;
            }

            foreach (var filter in GameFilters.All)
            {
                if (ImGui.Selectable(filter.Name, filter == chosen))
                {
                    Grade.GameFilter = filter.Row;
                    changed = released = true;
                }
            }

            ImGui.EndCombo();
        }

        using (ImRaii.Disabled(chosen == null))
        {
            if (BeginSliders("##GPoseFilterStrength"))
            {
                var strength = Grade.GameFilterStrength;
                if (Slider(Loc.T("Strength", "Force"), ref strength, 100f, 0f, 100f, "%.0f %%", 1f,
                        Loc.T("100 %: the filter as in the game. 0: no filter.", "100 % : le filtre comme dans le jeu. 0 : pas de filtre.")))
                    Grade.GameFilterStrength = strength;

                ImGui.EndTable();
            }
        }
    }

    // Flash blanc de cinématique, rendu par FlashColor ou comme le jeu le rendrait, pour comparer.
    private void DrawFlashTab()
    {
        ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T(
            "Recreates a cutscene white flash to see how bright lights and effects behave.",
            "Recrée un flash blanc de cinématique pour voir comment se comportent les lumières et les effets."));

        if (ImGui.Button(Loc.T("White flash", "Flash blanc")))
            plugin.StartFlash(gameImage: false);
        Tooltip(Loc.T(
            "The scene's light jumps up, then fades back, with your settings.",
            "La lumière de la scène monte d'un coup puis redescend, avec tes réglages."));

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Flash, game image", "Flash, image du jeu")))
            plugin.StartFlash(gameImage: true);
        Tooltip(Loc.T(
            "The same flash as the game would render it, with bright lights clipped, to compare.",
            "Le même flash tel que le jeu le rendrait, lumières fortes coupées, pour comparer."));

        if (BeginSliders("##Flash"))
        {
            var strength = Config.FlashStrength;
            if (Slider(Loc.T("Strength", "Force"), ref strength, 1f, 1f, 8f, Loc.T("%+.1f EV", "%+.1f IL"), 5f,
                    Loc.T(
                        "How much the light rises at the peak of the flash: +1 doubles it, +5 multiplies it by 32.",
                        "De combien la lumière monte au plus fort du flash : +1 la double, +5 la multiplie par 32.")))
                Config.FlashStrength = strength;

            ImGui.EndTable();
        }
    }

    // ---------------------------------------------------------------- Curseurs

    // Deux colonnes : le libellé à sa largeur, le curseur prend le reste.
    private static bool BeginSliders(string id)
    {
        if (!ImGui.BeginTable(id, 2))
            return false;

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        return true;
    }

    // Une ligne « libellé | curseur » ; swatch : petite case de couleur après le libellé.
    private bool Slider(string label, ref float value, float scale, float min, float max, string format, float defaultValue, string tooltip, Vector3? swatch = null)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        Tooltip(tooltip);
        if (swatch != null)
        {
            ImGui.SameLine();
            Swatch(swatch.Value);
        }

        ImGui.TableNextColumn();
        return CellSlider("##" + label, ref value, scale, min, max, format, defaultValue, tooltip);
    }

    // Les réglages suivent le curseur en direct ; ils ne sont écrits sur le disque qu'au relâchement.
    // scale : rapport entre la valeur affichée et la valeur enregistrée (100 pour un pourcentage).
    private bool CellSlider(string id, ref float value, float scale, float min, float max, string format, float defaultValue, string tooltip)
    {
        ImGui.SetNextItemWidth(-1);
        var shown = value * scale;
        var moved = ImGui.SliderFloat(id, ref shown, min, max, format);
        released |= ImGui.IsItemDeactivatedAfterEdit();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            shown = defaultValue * scale;
            moved = released = true;
        }

        Tooltip(tooltip + "\n" + RightClickHint);
        if (moved)
            value = shown / scale;

        changed |= moved;
        return moved;
    }

    private static void Swatch(Vector3 color)
    {
        var size = ImGui.GetTextLineHeight();
        ImGui.ColorButton("##Swatch", new Vector4(color, 1f), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop, new Vector2(size, size));
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(text);
    }

    private static void Warning(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange))
            ImGui.TextWrapped(text);
    }

    private void DrawLanguage()
    {
        var current = Config.Language;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (!ImGui.BeginCombo(Loc.T("Language", "Langue"), LanguageName(current)))
            return;

        foreach (var language in Enum.GetValues<PluginLanguage>())
        {
            if (ImGui.Selectable(LanguageName(language), language == current))
                plugin.SetLanguage(language);
        }

        ImGui.EndCombo();
    }

    private static string LanguageName(PluginLanguage language) => language switch
    {
        PluginLanguage.English => "English",
        PluginLanguage.French => "Français",
        _ => Loc.T("Automatic (Dalamud)", "Automatique (Dalamud)"),
    };
}
