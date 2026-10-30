// FlashColor : remplace le tonemap du jeu (shader/sm5/posteffect/ToneMapping.shcd).
//
// Mêmes entrées que le shader du jeu (t0/s0 image, t1/s1 courbe, b0, b1) ; nos réglages arrivent dans b13, et la
// courbe du filtre de la GPose choisi dans t2/s2.
// Le jeu fait : image en « gamma 2 » → lumière linéaire → facteur lu dans sa courbe selon la luminosité → « gamma 2 ».
// Une passe suivante (Saturate.shcd) coupe ensuite chaque couleur au-dessus de 1 : c'est là que les flammes se
// délavent. On règle les couleurs juste après la courbe du jeu, quand rien n'est encore coupé, puis on adoucit les
// lumières fortes pour qu'elles restent sous 1 sans changer de teinte.
//
// Réglages neutres et adoucissement coupé : même calcul que le jeu (chaque étape neutre est sautée).

cbuffer cCommonTexParam : register(b0)
{
    float4 cCommonTexParam; // du jeu ; y : son exposition
};

cbuffer cToneMapParam : register(b1)
{
    float4 cToneMapParam[2]; // du jeu ; [0].z : échelle de la luminosité ; [1].xy : début et fin de sa courbe
};

cbuffer FlashColorParams : register(b13)
{
    float3 Gain;          // exposition × balance des blancs, couleur par couleur
    float Contrast;       // 1 : inchangé
    float Saturation;     // 1 : inchangé
    float ShoulderStart;  // lumière (linéaire) où commence l'adoucissement ; 1 ou plus : pas d'adoucissement
    float KeepColor;      // 0 : les lumières fortes blanchissent naturellement (couleur par couleur) ; 1 : gardent leur couleur
    float Vibrance;       // -1 à 1 : agit surtout sur les couleurs ternes
    float4 ShadowTint;    // par zone : gains RGB (même luminosité), w : luminosité en IL
    float4 MidtoneTint;
    float4 HighlightTint;
    float4 Detail;        // x : netteté, y : clarté, z : rayon de la clarté (part de la hauteur)
    float4 Flags;         // x : zones, y : couleur par couleur, z : détails ; 0 : étape sautée
    float4 HueShift[2];       // par couleur, rouge → magenta : décalage de teinte (en tours)
    float4 HueSaturation[2];  // saturation (1 : inchangée)
    float4 HueLuminance[2];   // luminosité en IL
    float4 Local;         // garder les détails : x part du détail gardée, y rayon (part de la hauteur), z tolérance des bords (crans), w 1 : actif
    float4 FilterRed;     // filtre de la GPose : lignes de sa matrice de couleurs (3 couleurs, puis décalage)
    float4 FilterGreen;
    float4 FilterBlue;
    float4 Filter;        // filtre de la GPose : x force (0 : pas de filtre)
};

static const float3 GameLumaWeights = float3(0.298910, 0.586610, 0.114480); // ceux du jeu, pour lire sa courbe
static const float3 LumaWeights = float3(0.2126, 0.7152, 0.0722);
static const float MidGray = 0.18;

// Lumière de la scène à partir de l'image du jeu avant son tonemap (« gamma 2 », avec son exposition).
float3 SceneLight(float3 encoded)
{
    float3 color = max(encoded, 0.0) * cCommonTexParam.y;
    return color * color;
}

Texture2D<float4> sInputT : register(t0);
SamplerState sInputS : register(s0);
Texture2D<float4> sToneMapT : register(t1);
SamplerState sToneMapS : register(s1);

// Courbe du filtre de la GPose (256 valeurs), liée par FlashColor quand un filtre est choisi.
Texture2D<float> FilterCurveT : register(t2);
SamplerState FilterCurveS : register(s2);

// Centres des couleurs réglables, en tours : rouge, orange, jaune, vert, cyan, bleu, violet, magenta, puis rouge.
static const float BandCenters[9] = { 0.0, 30.0 / 360.0, 60.0 / 360.0, 120.0 / 360.0, 180.0 / 360.0, 240.0 / 360.0, 270.0 / 360.0, 300.0 / 360.0, 1.0 };

// Place d'une luminosité entre les ombres (0) et les lumières (1) : de 4 crans sous le gris moyen à 4 crans au-dessus.
float ZonePosition(float luma)
{
    return saturate((log2(max(luma, 1e-6) / MidGray) + 4.0) / 8.0);
}

float3 HueColor(float hue)
{
    return saturate(abs(frac(hue + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
}

float HueOf(float3 color, float peak, float chroma)
{
    float h;
    if (peak == color.r)
        h = (color.g - color.b) / chroma;
    else if (peak == color.g)
        h = 2.0 + (color.b - color.r) / chroma;
    else
        h = 4.0 + (color.r - color.g) / chroma;
    return frac(h / 6.0 + 1.0);
}

// Valeur d'un réglage par couleur pour cette teinte : entre les deux couleurs voisines, au prorata.
float BandValue(float4 values[2], float hue)
{
    float result = values[0].x;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float start = BandCenters[i];
        float end = BandCenters[i + 1];
        if (hue >= start && hue < end)
        {
            int next = (i + 1) & 7;
            result = lerp(values[i >> 2][i & 3], values[next >> 2][next & 3], (hue - start) / (end - start));
        }
    }

    return result;
}

float SceneLuma(float2 uv)
{
    return dot(SceneLight(sInputT.SampleLevel(sInputS, uv, 0).rgb), LumaWeights);
}

// Netteté (voisins immédiats) et clarté (anneau plus large), sur la luminosité seule : les couleurs ne bavent pas.
float3 ApplyDetail(float3 color, float2 uv)
{
    float luma = dot(color, LumaWeights);
    if (luma <= 0.0)
        return color;

    float width, height;
    sInputT.GetDimensions(width, height);
    float2 texel = 1.0 / float2(width, height);
    float graded = luma;

    if (Detail.x > 0.0)
    {
        float around = 0.25 * (SceneLuma(uv + float2(texel.x, 0.0)) + SceneLuma(uv - float2(texel.x, 0.0))
            + SceneLuma(uv + float2(0.0, texel.y)) + SceneLuma(uv - float2(0.0, texel.y)));
        // Pas plus de 25 % de la luminosité du pixel : pas de halo autour des contours.
        graded += clamp((luma - around) * Detail.x * 2.0, -0.25 * luma, 0.25 * luma);
    }

    if (Detail.y != 0.0)
    {
        // Moyenne en crans sur un anneau autour du pixel ; on écarte le pixel de cette moyenne, surtout en tons moyens.
        float radius = Detail.z * height;
        float sum = 0.0;
        [unroll]
        for (int i = 0; i < 12; i++)
        {
            float angle = i * (6.2831853 / 12.0);
            float distance = radius * ((i & 1) != 0 ? 1.0 : 0.5);
            sum += log2(max(SceneLuma(uv + float2(cos(angle), sin(angle)) * distance * texel), 1e-6));
        }

        float stops = log2(max(graded, 1e-6));
        float local = clamp(stops - sum / 12.0, -2.0, 2.0);
        float position = ZonePosition(graded);
        graded *= exp2(Detail.y * 0.5 * local * 4.0 * position * (1.0 - position));
    }

    return color * (graded / luma);
}

// Ombres, tons moyens, lumières : trois parts qui se recouvrent en douceur et font toujours 1 au total.
float3 ApplyZones(float3 color)
{
    float t = ZonePosition(dot(color, LumaWeights));
    float3 weight = float3((1.0 - t) * (1.0 - t), 2.0 * t * (1.0 - t), t * t);
    float3 gain = weight.x * ShadowTint.rgb + weight.y * MidtoneTint.rgb + weight.z * HighlightTint.rgb;
    float stops = weight.x * ShadowTint.w + weight.y * MidtoneTint.w + weight.z * HighlightTint.w;

    // Les couleurs vives (une flamme, un sort) gardent leur teinte : la couleur des zones touche surtout ce qui est
    // terne (pierre, ombres, ciel gris). Sinon une teinte bleue rend une flamme orange rose.
    float peak = max(color.r, max(color.g, color.b));
    float colorfulness = peak > 0.0 ? (peak - min(color.r, min(color.g, color.b))) / peak : 0.0;
    gain = lerp(gain, 1.0.xxx, 0.75 * colorfulness);
    return color * gain * exp2(stops);
}

float3 ApplyVibrance(float3 color)
{
    float peak = max(color.r, max(color.g, color.b));
    if (peak <= 0.0)
        return color;

    float colorfulness = (peak - min(color.r, min(color.g, color.b))) / peak;
    float luma = dot(color, LumaWeights);
    return max(lerp(luma.xxx, color, 1.0 + Vibrance * (1.0 - colorfulness)), 0.0);
}

float3 ApplyPerColor(float3 color)
{
    float peak = max(color.r, max(color.g, color.b));
    float chroma = peak - min(color.r, min(color.g, color.b));
    if (peak <= 0.0 || chroma <= 0.0)
        return color;

    float colorfulness = chroma / peak;
    float hue = HueOf(color, peak, chroma);
    // Les gris ne bougent pas, les couleurs franches à fond.
    float weight = smoothstep(0.0, 0.2, colorfulness);

    float shift = BandValue(HueShift, hue) * weight;
    if (shift != 0.0)
        color = peak * lerp(1.0.xxx, HueColor(frac(hue + shift)), colorfulness);

    float luma = dot(color, LumaWeights);
    color = max(lerp(luma.xxx, color, lerp(1.0, BandValue(HueSaturation, hue), weight)), 0.0);
    return color * exp2(BandValue(HueLuminance, hue) * weight);
}

float3 Grade(float3 color)
{
    color *= Gain;
    if (Flags.x != 0.0)
        color = ApplyZones(color);

    // Contraste sur la luminosité seule : la teinte ne bouge pas.
    float luma = dot(color, LumaWeights);
    if (Contrast != 1.0 && luma > 0.0)
    {
        float graded = MidGray * pow(luma / MidGray, Contrast);
        color *= graded / luma;
        luma = graded;
    }

    if (Saturation != 1.0)
        color = max(lerp(luma.xxx, color, Saturation), 0.0);

    if (Vibrance != 0.0)
        color = ApplyVibrance(color);

    if (Flags.y != 0.0)
        color = ApplyPerColor(color);

    return color;
}

// Au-delà du début, une valeur tend vers 1 sans jamais l'atteindre (même pente au raccord).
float Compress(float value)
{
    float range = 1.0 - ShoulderStart;
    float over = max(value - ShoulderStart, 0.0) / range;
    return value > ShoulderStart ? ShoulderStart + range * over / (1.0 + over) : value;
}

float3 Shoulder(float3 color)
{
    if (ShoulderStart >= 1.0)
        return color;

    // Chaque couleur adoucie à part, comme sur une pellicule : une flamme orange passe au jaune puis au blanc.
    float3 natural = float3(Compress(color.r), Compress(color.g), Compress(color.b));

    // Ou la même compression pour les trois : la couleur est gardée telle quelle.
    float peak = max(color.r, max(color.g, color.b));
    float3 kept = peak > ShoulderStart ? color * (Compress(peak) / peak) : color;

    return lerp(natural, kept, KeepColor);
}

// Dernière limite, couleur par couleur, seulement dans les 10 % du haut : ce qu'un détail remis pousse au-delà du blanc.
float3 Limit(float3 color)
{
    float knee = max(ShoulderStart, 0.9);
    float range = 1.0 - knee;
    float3 over = max(color - knee, 0.0) / range;
    return color > knee ? knee + range * over / (1.0 + over) : color;
}

// Luminosité d'un point de l'image après la courbe du jeu (celle sur laquelle travaillent nos réglages).
float CurvedLuma(float2 uv)
{
    float3 color = SceneLight(sInputT.SampleLevel(sInputS, uv, 0).rgb);
    float level = saturate(dot(color, GameLumaWeights) * cToneMapParam[0].z);
    float curve = lerp(cToneMapParam[1].x, cToneMapParam[1].y, level);
    return dot(color * sToneMapT.SampleLevel(sToneMapS, float2(curve, 0.5), 0).rgb, LumaWeights);
}

// La « base » d'un pixel : la luminosité moyenne autour de lui (en crans), sur deux anneaux. Les voisins trop
// différents (de l'autre côté d'un bord franc) comptent peu : pas de halo autour des contours.
float LocalBase(float2 uv, float luma)
{
    float width, height;
    sInputT.GetDimensions(width, height);
    float2 texel = 1.0 / float2(width, height);
    float radius = Local.y * height;
    float center = log2(max(luma, 1e-6));
    float sum = center;
    float total = 1.0;

    [unroll]
    for (int i = 0; i < 16; i++)
    {
        float angle = i * (6.2831853 / 16.0);
        float distance = radius * ((i & 1) != 0 ? 1.0 : 0.5);
        float stops = log2(max(CurvedLuma(uv + float2(cos(angle), sin(angle)) * distance * texel), 1e-6));
        float gap = (stops - center) / Local.z;
        float weight = exp(-0.5 * gap * gap);
        sum += stops * weight;
        total += weight;
    }

    return exp2(sum / total);
}

// Filtre de la GPose, calculé comme le shader de filtre du jeu (ColorFilter_DarkBlend.shcd) : sur l'image coupée à 1
// (le jeu le fait juste avant, dans Saturate.shcd), la courbe lue au centre de ses 256 cases pour chaque couleur, puis la
// matrice, et le mélange selon la force. Ses réglages pour les zones sombres sont neutres pour tous les filtres.
float3 GameFilter(float3 encoded)
{
    float3 input = saturate(encoded);
    float3 coord = input * (255.0 / 256.0) + 0.5 / 256.0;
    float4 curved = float4(
        FilterCurveT.SampleLevel(FilterCurveS, float2(coord.r, 0.5), 0),
        FilterCurveT.SampleLevel(FilterCurveS, float2(coord.g, 0.5), 0),
        FilterCurveT.SampleLevel(FilterCurveS, float2(coord.b, 0.5), 0),
        1.0);
    float3 filtered = float3(dot(FilterRed, curved), dot(FilterGreen, curved), dot(FilterBlue, curved));
    return lerp(input, filtered, Filter.x);
}

float4 main(float4 position : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    float4 source = sInputT.Sample(sInputS, uv);
    float3 color = SceneLight(source.rgb);

    if (Flags.z != 0.0)
        color = ApplyDetail(color, uv);

    float luma = saturate(dot(color, GameLumaWeights) * cToneMapParam[0].z);
    float curve = lerp(cToneMapParam[1].x, cToneMapParam[1].y, luma);
    color *= sToneMapT.Sample(sToneMapS, float2(curve, 0.5)).rgb;

    if (Local.w != 0.0)
    {
        // Garder les détails : les réglages et l'adoucissement ne voient que la base (la lumière moyenne autour) ;
        // l'écart du pixel à sa base (la texture) est remis tel quel après. Seul ce qui dépasse alors le haut de
        // l'image est encore adouci, tout près du blanc.
        float pixel = dot(color, LumaWeights);
        float baseLuma = LocalBase(uv, pixel);
        float detail = pixel > 1e-6 && baseLuma > 1e-6 ? pow(pixel / baseLuma, Local.x) : 1.0;
        float3 baseColor = pixel > 1e-6 ? color * (baseLuma / pixel) : color;
        color = Shoulder(Grade(baseColor)) * detail;
        if (ShoulderStart < 1.0)
            color = Limit(color);
    }
    else
    {
        color = Shoulder(Grade(color));
    }

    float3 encoded = sqrt(color);
    if (Filter.x > 0.0)
        encoded = GameFilter(encoded);

    return float4(encoded, source.a);
}
