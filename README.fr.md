# FlashColor

*[English version](README.md)*

Un plugin Dalamud pour Final Fantasy XIV : régler le **contraste**, la **saturation**, l'**exposition** et la **balance
des blancs** de l'image du jeu, sans ReShade, sans écrêtage des lumières fortes et sans toucher à l'interface.

## Installation
1. En jeu, tape `/xlsettings` et ouvre l'onglet **Expérimental**.
2. Dans **Custom Plugin Repositories**, ajoute cette adresse, clique sur **+**, coche la case puis enregistre :
   ```
   https://raw.githubusercontent.com/Arzhaell/FlashColor/main/repo.json
   ```
3. Tape `/xlplugins`, cherche **FlashColor** et installe-le.

## Utilisation
`/flashcolor` ouvre la fenêtre. Interface en français et en anglais (suit la langue de Dalamud, réglable). Garde le
bouton **Maintenir : image d'origine** enfoncé pour comparer avec l'image du jeu.

## Pourquoi pas ReShade
ReShade travaille sur l'image finale, déjà ramenée à ce que l'écran peut afficher. Une flamme y est déjà presque
blanche : monter le contraste la fait dépasser le blanc, elle est coupée en tache plate et sa couleur glisse vers le
jaune. Les effets qui se servent de la profondeur réagissent aussi mal aux particules, qui n'en ont pas.

FlashColor intervient **dans le tonemap du jeu**, au moment où les lumières fortes dépassent encore le blanc. Les
réglages s'appliquent là, puis les lumières fortes sont **adoucies** au lieu d'être coupées : chaque couleur tend vers
le blanc sans jamais l'atteindre, comme sur une pellicule (une flamme orange passe au jaune puis au blanc), ou garde sa
teinte si on le préfère.

**Les détails sont gardés** : contraste, couleurs et adoucissement s'appliquent à la lumière moyenne autour de chaque
pixel (moyenne sur deux anneaux jusqu'à 1,5 % de la hauteur de l'image, qui ignore les voisins de l'autre côté d'un bord
franc), puis l'écart du pixel à cette moyenne — sa texture — est remis tel quel. Rehausser les couleurs n'aplatit donc
plus l'intérieur des flammes ni les textures claires. Les teintes de zone épargnent les couleurs vives (une flamme
orange ne vire pas au rose sous une teinte bleue).

## Réglages
La fenêtre range les réglages en onglets. Clic droit sur un curseur : valeur par défaut.

- **Couleurs** : exposition, contraste, saturation, vibrance (surtout les couleurs ternes), température, teinte,
  adoucissement des lumières fortes (début, couleur des lumières fortes), garder les détails.
- **Zones** : ombres, tons moyens et lumières, chacune avec sa luminosité et sa couleur (teinte + force). Les trois
  zones se recouvrent en douceur selon la luminosité, de 4 crans sous le gris moyen à 4 crans au-dessus.
- **Par couleur** : rouge, orange, jaune, vert, cyan, bleu, violet et magenta, chacune avec son décalage de teinte, sa
  saturation et sa luminosité. Les gris ne sont jamais touchés.
- **Détails** : netteté (voisins immédiats, limitée autour des contours) et clarté (contraste local sur un anneau de
  2 % de la hauteur de l'image), sur la luminosité seule.
- **Filtre GPose** : un des filtres de la GPose du jeu, partout, avec sa force (voir plus bas).
- **Flash** : voir plus bas.

Le menu **…** à côté des préréglages enregistre les réglages comme nouveau préréglage, met à jour, renomme ou
supprime le préréglage choisi, et copie ou colle les réglages en texte (`FlashColor:{…}`) pour les partager.

## Préréglages
- **Image du jeu** : les couleurs du jeu, seules les lumières fortes sont adoucies.
- **Mesurés sur des presets ReShade** : Façon [Alive](https://www.nexusmods.com/finalfantasy14/mods/156) (Wafflezs),
  Façon [Arfien](https://www.nexusmods.com/finalfantasy14/mods/684), Façon
  [Stamp's Enhanced Gameplay](https://www.nexusmods.com/finalfantasy14/mods/1651), Façon
  [Colorful Fantasy](https://www.nexusmods.com/finalfantasy14/mods/790), Façon
  [Simple and Clean](https://www.nexusmods.com/finalfantasy14/mods/1592).
- **Ambiances** créées pour FlashColor : Cinéma, Pastel doux, Nuit froide, Coucher de soleil, Noir et blanc contrasté,
  Très vivant.
- **Filtres de la GPose** : les 24 filtres de la GPose du jeu (Sépia 1, Monochrome 1…), à l'identique, partout.

Les presets ReShade ont été **mesurés** sur les captures avant/après publiées par leurs auteurs : le calcul de
FlashColor (exposition, contraste, saturation, vibrance, couleur et luminosité des trois zones) a été ajusté pour
transformer au mieux chaque image « avant » en image « après » (écart des logarithmes, 2 500 pixels par paire), avec
une petite pénalité sur les réglages extrêmes. Les paires mal alignées sont écartées (REALXIV n'en a pas d'utilisable).
Rien n'est repris de leurs fichiers. Leur ombrage d'ambiance, leur netteté, leur brouillard et leur flou ne sont pas
des couleurs : FlashColor ne les reproduit pas. Owl's FFXIV Fantasy Reshades ne publie pas de capture sans son preset,
et les captures d'Alex's, Obsidian et Newfound Adventures ne disent pas laquelle est avec le preset : pas mesurés.

## Flash blanc
Le bouton **Flash blanc** recrée un flash de cinématique : la lumière de la scène monte d'un coup (force réglable,
+5 IL par défaut, soit ×32), tient un instant, puis redescend en une seconde environ. **Flash, image du jeu** rejoue le
même flash tel que le jeu le rendrait, lumières fortes coupées, pour comparer. Le flash passe par le tonemap : ce qui
ne change pas pendant le flash (l'interface, par exemple) est dessiné après lui.

## Filtres de la GPose
Les 24 filtres de la GPose (« Filtre de couleurs ») sont disponibles partout, hors GPose, à l'identique : choisis dans
l'onglet **Filtre GPose** ou dans le groupe de préréglages **Filtres de la GPose**.

Chaque filtre a été **relevé en jeu** (le 2026-10-15, jeu 2026.10.06) : en GPose, ce que le jeu envoie à son shader de
filtre (`ColorFilter_DarkBlend.shcd`) pour chaque filtre, soit une courbe de 256 valeurs, la même pour les trois
couleurs, puis une matrice de couleurs 3 × 4. Chaque relevé correspond exactement à sa ligne de la feuille
`ColorFilter` du jeu. FlashColor refait le même calcul à la fin de son tonemap, sur l'image coupée à 1 comme celle que
reçoit le filtre du jeu.

- Comme en GPose, le filtre choisi **remplace** le filtre de couleur de la zone : celui du jeu est mis à zéro pendant
  qu'un filtre de FlashColor est actif.
- Il s'applique **après** les réglages de couleurs, qui restent utilisables avec lui. La **force** mélange l'image sans
  filtre (0 %) et l'image filtrée (100 %, comme dans le jeu).
- En GPose, un filtre choisi dans le jeu passe avant celui de FlashColor (les deux ne s'empilent pas) ; avec
  « Standard », celui de FlashColor reste.

## Comment ça marche
Chaîne d'image du jeu (shaders `shader/sm5/posteffect/*.shcd`, relevée dans les fichiers du jeu) :

1. la scène, en lumière « gamma 2 », peut dépasser le blanc ;
2. `ToneMapping.shcd` : un facteur lu dans une courbe selon la luminosité, appliqué aux trois couleurs ;
3. `Saturate.shcd` : **chaque couleur est coupée à 1** (c'est là que les flammes du jeu jaunissent déjà) ;
4. `ColorFilter_DarkBlend.shcd` : courbe et filtre de couleur de la zone ;
5. vignettage, gamma (`ToneAdjust.shcd`), puis l'interface par-dessus.

FlashColor intercepte la fonction du jeu qui active les shaders (`ImmediateContext.SetPixelShader`). Quand il s'agit
du tonemap (étape 2), il active son propre shader à la place : même calcul que le jeu, puis les réglages, puis
l'adoucissement des lumières fortes. Ses réglages passent par un tampon à part (registre b13), remis en place juste après.

Avec les réglages neutres et l'adoucissement coupé, l'image est **identique** à celle du jeu (vérifié par les tests).

Avec un filtre de la GPose choisi, il lie aussi la courbe du filtre (t2 / s2) pour son tonemap, et quand le jeu active
son propre filtre de couleur (étape 4), il lui donne une force de 0 (tampon b2), remise en place juste après.

### Branchement sur le jeu
Pour intercepter la fonction du jeu, Dalamud a besoin d'un peu de mémoire libre à un endroit précis ; il arrive qu'il
n'y en ait pas pendant un moment (« Unable to find memory location to fit MemoryBuffer » dans `dalamud.log`, au hasard
d'un rechargement du plugin). FlashColor le signale dans sa fenêtre et réessaie tout seul, de plus en plus espacé
(2 s, puis jusqu'à 30 s), au lieu de s'arrêter.

La fenêtre reste ouverte en GPose, pour régler les couleurs pendant les photos ; masquer l'interface du jeu la cache.

### Après une mise à jour du jeu
Au chargement, FlashColor compare l'empreinte de `ToneMapping.shcd` à celle pour laquelle il est écrit
(`0x85E777EF`). Si le jeu l'a changé, FlashColor reste éteint et le signale dans sa fenêtre : il faut alors l'adapter.

## Avertissement
Comme tout outil tiers, l'utilisation de Dalamud et de ses plugins est contraire aux conditions d'utilisation de
FFXIV. Tu l'utilises à tes risques.

FlashColor n'est ni affilié ni approuvé par ReShade ou par les auteurs des presets cités plus haut.

## Compiler et tester
- `dotnet test FlashColor.slnx` : les tests lisent les vrais `ToneMapping.shcd` et `ColorFilter_DarkBlend.shcd` du jeu
  installé (lecture seule) et font tourner les shaders du jeu et celui de FlashColor sur les mêmes pixels avec WARP, la
  carte graphique logicielle de Windows :
  - réglages neutres : même image que le jeu ;
  - une flamme qui dépasse le blanc : le jeu la coupe et change sa teinte, FlashColor peut garder sa teinte sous le
    blanc, ou la faire passer au jaune sans la rendre rose ;
  - la texture d'une flamme très lumineuse survit à l'adoucissement quand on garde les détails ;
  - réglages forts : aucune couleur ne dépasse le blanc ;
  - en dessous du début de l'adoucissement : rien ne change ;
  - mêmes entrées que le shader du jeu (signature, emplacements) ;
  - les 24 filtres de la GPose : même image que le vrai shader de filtre du jeu (`ColorFilter_DarkBlend.shcd`), à
    0,00001 près, avec des réglages de couleurs en plus ; à demi-force, l'image est à mi-chemin.

  Sans le jeu installé, ces tests échouent, sauf avec la variable d'environnement `FLASHCOLOR_TESTS_WITHOUT_GAME=1`.
- `dotnet build FlashColor -c Release` : pour l'essayer, ajoute `FlashColor\bin\Release\FlashColor.dll` aux emplacements
  des plugins de développement (`/xlsettings`, onglet **Expérimental**) ; Dalamud la recharge toute seule, même jeu
  ouvert. Il ne charge pas en même temps la version installée depuis le dépôt : désactive-la.

## Publier une nouvelle version
Pousser un tag annoté `vX.Y.Z` suffit : le workflow GitHub lance les tests, compile le plugin, crée la release (ses
notes sont le message du tag) et met à jour `repo.json`.

```bash
git tag -a v1.0.0 --cleanup=verbatim -F notes.txt
git push origin v1.0.0
```

## Crédits
- [Dalamud](https://github.com/goatcorp/Dalamud) (AGPL-3.0) : chargement du plugin, accès au jeu.
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) (MIT) : structures du jeu (`ImmediateContext`,
  `PixelShader`).
- [TerraFX.Interop.Windows](https://github.com/terrafx/terrafx.interop.windows) (MIT) : accès à DirectX 11, fourni par
  Dalamud.
- [Lumina](https://github.com/NotAdam/Lumina) (MIT) : lecture des fichiers du jeu dans les tests.
- [RenoDX](https://github.com/clshortfuse/renodx) (MIT) : son mod FFXIV a montré quels shaders du jeu forment la
  chaîne de tonemap. Aucun code n'en est repris : les shaders du jeu ont été relus directement.
- [Dear ImGui](https://github.com/ocornut/imgui) (MIT) : la bibliothèque d'interface utilisée par Dalamud.
- [xUnit](https://xunit.net/) pour les tests.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. Ce plugin n'est ni affilié ni approuvé par Square Enix.
