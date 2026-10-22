namespace Aprillz.MewUI;

public readonly partial struct Color
{
    private static readonly Dictionary<string, Color> _named = CreateNamedColors();

    /// <summary>Gets the named colors keyed by name, compared case-insensitively.</summary>
    public static IReadOnlyDictionary<string, Color> NamedColors => _named;

    // Named colors (WPF/System.Drawing KnownColor set, excluding system colors)

    /// <summary>Gets the fully transparent color (#00FFFFFF).</summary>
    public static Color Transparent => FromArgb(0x00FFFFFF);

    /// <summary>Gets the named color Black (#000000).</summary>
    public static Color Black => FromArgb(0xFF000000);
    /// <summary>Gets the named color Blue (#0000FF).</summary>
    public static Color Blue => FromArgb(0xFF0000FF);
    /// <summary>Gets the named color Cyan (#00FFFF).</summary>
    public static Color Cyan => FromArgb(0xFF00FFFF);
    /// <summary>Gets the named color DarkGray (#A9A9A9).</summary>
    public static Color DarkGray => FromArgb(0xFFA9A9A9);
    /// <summary>Gets the named color Fuchsia (#FF00FF).</summary>
    public static Color Fuchsia => FromArgb(0xFFFF00FF);
    /// <summary>Gets the named color Gray (#808080).</summary>
    public static Color Gray => FromArgb(0xFF808080);
    /// <summary>Gets the named color Green (#008000).</summary>
    public static Color Green => FromArgb(0xFF008000);
    /// <summary>Gets the named color LightGray (#D3D3D3).</summary>
    public static Color LightGray => FromArgb(0xFFD3D3D3);
    /// <summary>Gets the named color Magenta (#FF00FF).</summary>
    public static Color Magenta => FromArgb(0xFFFF00FF);
    /// <summary>Gets the named color Maroon (#800000).</summary>
    public static Color Maroon => FromArgb(0xFF800000);
    /// <summary>Gets the named color Orange (#FFA500).</summary>
    public static Color Orange => FromArgb(0xFFFFA500);
    /// <summary>Gets the named color Purple (#800080).</summary>
    public static Color Purple => FromArgb(0xFF800080);
    /// <summary>Gets the named color Red (#FF0000).</summary>
    public static Color Red => FromArgb(0xFFFF0000);
    /// <summary>Gets the named color White (#FFFFFF).</summary>
    public static Color White => FromArgb(0xFFFFFFFF);
    /// <summary>Gets the named color Yellow (#FFFF00).</summary>
    public static Color Yellow => FromArgb(0xFFFFFF00);

    /// <summary>Gets the named color AliceBlue (#F0F8FF).</summary>
    public static Color AliceBlue => FromArgb(0xFFF0F8FF);
    /// <summary>Gets the named color AntiqueWhite (#FAEBD7).</summary>
    public static Color AntiqueWhite => FromArgb(0xFFFAEBD7);
    /// <summary>Gets the named color Aqua (#00FFFF).</summary>
    public static Color Aqua => FromArgb(0xFF00FFFF);
    /// <summary>Gets the named color Aquamarine (#7FFFD4).</summary>
    public static Color Aquamarine => FromArgb(0xFF7FFFD4);
    /// <summary>Gets the named color Azure (#F0FFFF).</summary>
    public static Color Azure => FromArgb(0xFFF0FFFF);
    /// <summary>Gets the named color Beige (#F5F5DC).</summary>
    public static Color Beige => FromArgb(0xFFF5F5DC);
    /// <summary>Gets the named color Bisque (#FFE4C4).</summary>
    public static Color Bisque => FromArgb(0xFFFFE4C4);
    /// <summary>Gets the named color BlanchedAlmond (#FFEBCD).</summary>
    public static Color BlanchedAlmond => FromArgb(0xFFFFEBCD);
    /// <summary>Gets the named color BlueViolet (#8A2BE2).</summary>
    public static Color BlueViolet => FromArgb(0xFF8A2BE2);
    /// <summary>Gets the named color Brown (#A52A2A).</summary>
    public static Color Brown => FromArgb(0xFFA52A2A);
    /// <summary>Gets the named color BurlyWood (#DEB887).</summary>
    public static Color BurlyWood => FromArgb(0xFFDEB887);
    /// <summary>Gets the named color CadetBlue (#5F9EA0).</summary>
    public static Color CadetBlue => FromArgb(0xFF5F9EA0);
    /// <summary>Gets the named color Chartreuse (#7FFF00).</summary>
    public static Color Chartreuse => FromArgb(0xFF7FFF00);
    /// <summary>Gets the named color Chocolate (#D2691E).</summary>
    public static Color Chocolate => FromArgb(0xFFD2691E);
    /// <summary>Gets the named color Coral (#FF7F50).</summary>
    public static Color Coral => FromArgb(0xFFFF7F50);
    /// <summary>Gets the named color CornflowerBlue (#6495ED).</summary>
    public static Color CornflowerBlue => FromArgb(0xFF6495ED);
    /// <summary>Gets the named color Cornsilk (#FFF8DC).</summary>
    public static Color Cornsilk => FromArgb(0xFFFFF8DC);
    /// <summary>Gets the named color Crimson (#DC143C).</summary>
    public static Color Crimson => FromArgb(0xFFDC143C);
    /// <summary>Gets the named color DarkBlue (#00008B).</summary>
    public static Color DarkBlue => FromArgb(0xFF00008B);
    /// <summary>Gets the named color DarkCyan (#008B8B).</summary>
    public static Color DarkCyan => FromArgb(0xFF008B8B);
    /// <summary>Gets the named color DarkGoldenrod (#B8860B).</summary>
    public static Color DarkGoldenrod => FromArgb(0xFFB8860B);
    /// <summary>Gets the named color DarkGreen (#006400).</summary>
    public static Color DarkGreen => FromArgb(0xFF006400);
    /// <summary>Gets the named color DarkKhaki (#BDB76B).</summary>
    public static Color DarkKhaki => FromArgb(0xFFBDB76B);
    /// <summary>Gets the named color DarkMagenta (#8B008B).</summary>
    public static Color DarkMagenta => FromArgb(0xFF8B008B);
    /// <summary>Gets the named color DarkOliveGreen (#556B2F).</summary>
    public static Color DarkOliveGreen => FromArgb(0xFF556B2F);
    /// <summary>Gets the named color DarkOrange (#FF8C00).</summary>
    public static Color DarkOrange => FromArgb(0xFFFF8C00);
    /// <summary>Gets the named color DarkOrchid (#9932CC).</summary>
    public static Color DarkOrchid => FromArgb(0xFF9932CC);
    /// <summary>Gets the named color DarkRed (#8B0000).</summary>
    public static Color DarkRed => FromArgb(0xFF8B0000);
    /// <summary>Gets the named color DarkSalmon (#E9967A).</summary>
    public static Color DarkSalmon => FromArgb(0xFFE9967A);
    /// <summary>Gets the named color DarkSeaGreen (#8FBC8F).</summary>
    public static Color DarkSeaGreen => FromArgb(0xFF8FBC8F);
    /// <summary>Gets the named color DarkSlateBlue (#483D8B).</summary>
    public static Color DarkSlateBlue => FromArgb(0xFF483D8B);
    /// <summary>Gets the named color DarkSlateGray (#2F4F4F).</summary>
    public static Color DarkSlateGray => FromArgb(0xFF2F4F4F);
    /// <summary>Gets the named color DarkTurquoise (#00CED1).</summary>
    public static Color DarkTurquoise => FromArgb(0xFF00CED1);
    /// <summary>Gets the named color DarkViolet (#9400D3).</summary>
    public static Color DarkViolet => FromArgb(0xFF9400D3);
    /// <summary>Gets the named color DeepPink (#FF1493).</summary>
    public static Color DeepPink => FromArgb(0xFFFF1493);
    /// <summary>Gets the named color DeepSkyBlue (#00BFFF).</summary>
    public static Color DeepSkyBlue => FromArgb(0xFF00BFFF);
    /// <summary>Gets the named color DimGray (#696969).</summary>
    public static Color DimGray => FromArgb(0xFF696969);
    /// <summary>Gets the named color DodgerBlue (#1E90FF).</summary>
    public static Color DodgerBlue => FromArgb(0xFF1E90FF);
    /// <summary>Gets the named color Firebrick (#B22222).</summary>
    public static Color Firebrick => FromArgb(0xFFB22222);
    /// <summary>Gets the named color FloralWhite (#FFFAF0).</summary>
    public static Color FloralWhite => FromArgb(0xFFFFFAF0);
    /// <summary>Gets the named color ForestGreen (#228B22).</summary>
    public static Color ForestGreen => FromArgb(0xFF228B22);
    /// <summary>Gets the named color Gainsboro (#DCDCDC).</summary>
    public static Color Gainsboro => FromArgb(0xFFDCDCDC);
    /// <summary>Gets the named color GhostWhite (#F8F8FF).</summary>
    public static Color GhostWhite => FromArgb(0xFFF8F8FF);
    /// <summary>Gets the named color Gold (#FFD700).</summary>
    public static Color Gold => FromArgb(0xFFFFD700);
    /// <summary>Gets the named color Goldenrod (#DAA520).</summary>
    public static Color Goldenrod => FromArgb(0xFFDAA520);
    /// <summary>Gets the named color GreenYellow (#ADFF2F).</summary>
    public static Color GreenYellow => FromArgb(0xFFADFF2F);
    /// <summary>Gets the named color Honeydew (#F0FFF0).</summary>
    public static Color Honeydew => FromArgb(0xFFF0FFF0);
    /// <summary>Gets the named color HotPink (#FF69B4).</summary>
    public static Color HotPink => FromArgb(0xFFFF69B4);
    /// <summary>Gets the named color IndianRed (#CD5C5C).</summary>
    public static Color IndianRed => FromArgb(0xFFCD5C5C);
    /// <summary>Gets the named color Indigo (#4B0082).</summary>
    public static Color Indigo => FromArgb(0xFF4B0082);
    /// <summary>Gets the named color Ivory (#FFFFF0).</summary>
    public static Color Ivory => FromArgb(0xFFFFFFF0);
    /// <summary>Gets the named color Khaki (#F0E68C).</summary>
    public static Color Khaki => FromArgb(0xFFF0E68C);
    /// <summary>Gets the named color Lavender (#E6E6FA).</summary>
    public static Color Lavender => FromArgb(0xFFE6E6FA);
    /// <summary>Gets the named color LavenderBlush (#FFF0F5).</summary>
    public static Color LavenderBlush => FromArgb(0xFFFFF0F5);
    /// <summary>Gets the named color LawnGreen (#7CFC00).</summary>
    public static Color LawnGreen => FromArgb(0xFF7CFC00);
    /// <summary>Gets the named color LemonChiffon (#FFFACD).</summary>
    public static Color LemonChiffon => FromArgb(0xFFFFFACD);
    /// <summary>Gets the named color LightBlue (#ADD8E6).</summary>
    public static Color LightBlue => FromArgb(0xFFADD8E6);
    /// <summary>Gets the named color LightCoral (#F08080).</summary>
    public static Color LightCoral => FromArgb(0xFFF08080);
    /// <summary>Gets the named color LightCyan (#E0FFFF).</summary>
    public static Color LightCyan => FromArgb(0xFFE0FFFF);
    /// <summary>Gets the named color LightGoldenrodYellow (#FAFAD2).</summary>
    public static Color LightGoldenrodYellow => FromArgb(0xFFFAFAD2);
    /// <summary>Gets the named color LightGreen (#90EE90).</summary>
    public static Color LightGreen => FromArgb(0xFF90EE90);
    /// <summary>Gets the named color LightPink (#FFB6C1).</summary>
    public static Color LightPink => FromArgb(0xFFFFB6C1);
    /// <summary>Gets the named color LightSalmon (#FFA07A).</summary>
    public static Color LightSalmon => FromArgb(0xFFFFA07A);
    /// <summary>Gets the named color LightSeaGreen (#20B2AA).</summary>
    public static Color LightSeaGreen => FromArgb(0xFF20B2AA);
    /// <summary>Gets the named color LightSkyBlue (#87CEFA).</summary>
    public static Color LightSkyBlue => FromArgb(0xFF87CEFA);
    /// <summary>Gets the named color LightSlateGray (#778899).</summary>
    public static Color LightSlateGray => FromArgb(0xFF778899);
    /// <summary>Gets the named color LightSteelBlue (#B0C4DE).</summary>
    public static Color LightSteelBlue => FromArgb(0xFFB0C4DE);
    /// <summary>Gets the named color LightYellow (#FFFFE0).</summary>
    public static Color LightYellow => FromArgb(0xFFFFFFE0);
    /// <summary>Gets the named color Lime (#00FF00).</summary>
    public static Color Lime => FromArgb(0xFF00FF00);
    /// <summary>Gets the named color LimeGreen (#32CD32).</summary>
    public static Color LimeGreen => FromArgb(0xFF32CD32);
    /// <summary>Gets the named color Linen (#FAF0E6).</summary>
    public static Color Linen => FromArgb(0xFFFAF0E6);
    /// <summary>Gets the named color MediumAquamarine (#66CDAA).</summary>
    public static Color MediumAquamarine => FromArgb(0xFF66CDAA);
    /// <summary>Gets the named color MediumBlue (#0000CD).</summary>
    public static Color MediumBlue => FromArgb(0xFF0000CD);
    /// <summary>Gets the named color MediumOrchid (#BA55D3).</summary>
    public static Color MediumOrchid => FromArgb(0xFFBA55D3);
    /// <summary>Gets the named color MediumPurple (#9370DB).</summary>
    public static Color MediumPurple => FromArgb(0xFF9370DB);
    /// <summary>Gets the named color MediumSeaGreen (#3CB371).</summary>
    public static Color MediumSeaGreen => FromArgb(0xFF3CB371);
    /// <summary>Gets the named color MediumSlateBlue (#7B68EE).</summary>
    public static Color MediumSlateBlue => FromArgb(0xFF7B68EE);
    /// <summary>Gets the named color MediumSpringGreen (#00FA9A).</summary>
    public static Color MediumSpringGreen => FromArgb(0xFF00FA9A);
    /// <summary>Gets the named color MediumTurquoise (#48D1CC).</summary>
    public static Color MediumTurquoise => FromArgb(0xFF48D1CC);
    /// <summary>Gets the named color MediumVioletRed (#C71585).</summary>
    public static Color MediumVioletRed => FromArgb(0xFFC71585);
    /// <summary>Gets the named color MidnightBlue (#191970).</summary>
    public static Color MidnightBlue => FromArgb(0xFF191970);
    /// <summary>Gets the named color MintCream (#F5FFFA).</summary>
    public static Color MintCream => FromArgb(0xFFF5FFFA);
    /// <summary>Gets the named color MistyRose (#FFE4E1).</summary>
    public static Color MistyRose => FromArgb(0xFFFFE4E1);
    /// <summary>Gets the named color Moccasin (#FFE4B5).</summary>
    public static Color Moccasin => FromArgb(0xFFFFE4B5);
    /// <summary>Gets the named color NavajoWhite (#FFDEAD).</summary>
    public static Color NavajoWhite => FromArgb(0xFFFFDEAD);
    /// <summary>Gets the named color Navy (#000080).</summary>
    public static Color Navy => FromArgb(0xFF000080);
    /// <summary>Gets the named color OldLace (#FDF5E6).</summary>
    public static Color OldLace => FromArgb(0xFFFDF5E6);
    /// <summary>Gets the named color Olive (#808000).</summary>
    public static Color Olive => FromArgb(0xFF808000);
    /// <summary>Gets the named color OliveDrab (#6B8E23).</summary>
    public static Color OliveDrab => FromArgb(0xFF6B8E23);
    /// <summary>Gets the named color OrangeRed (#FF4500).</summary>
    public static Color OrangeRed => FromArgb(0xFFFF4500);
    /// <summary>Gets the named color Orchid (#DA70D6).</summary>
    public static Color Orchid => FromArgb(0xFFDA70D6);
    /// <summary>Gets the named color PaleGoldenrod (#EEE8AA).</summary>
    public static Color PaleGoldenrod => FromArgb(0xFFEEE8AA);
    /// <summary>Gets the named color PaleGreen (#98FB98).</summary>
    public static Color PaleGreen => FromArgb(0xFF98FB98);
    /// <summary>Gets the named color PaleTurquoise (#AFEEEE).</summary>
    public static Color PaleTurquoise => FromArgb(0xFFAFEEEE);
    /// <summary>Gets the named color PaleVioletRed (#DB7093).</summary>
    public static Color PaleVioletRed => FromArgb(0xFFDB7093);
    /// <summary>Gets the named color PapayaWhip (#FFEFD5).</summary>
    public static Color PapayaWhip => FromArgb(0xFFFFEFD5);
    /// <summary>Gets the named color PeachPuff (#FFDAB9).</summary>
    public static Color PeachPuff => FromArgb(0xFFFFDAB9);
    /// <summary>Gets the named color Peru (#CD853F).</summary>
    public static Color Peru => FromArgb(0xFFCD853F);
    /// <summary>Gets the named color Pink (#FFC0CB).</summary>
    public static Color Pink => FromArgb(0xFFFFC0CB);
    /// <summary>Gets the named color Plum (#DDA0DD).</summary>
    public static Color Plum => FromArgb(0xFFDDA0DD);
    /// <summary>Gets the named color PowderBlue (#B0E0E6).</summary>
    public static Color PowderBlue => FromArgb(0xFFB0E0E6);
    /// <summary>Gets the named color RebeccaPurple (#663399).</summary>
    public static Color RebeccaPurple => FromArgb(0xFF663399);
    /// <summary>Gets the named color RosyBrown (#BC8F8F).</summary>
    public static Color RosyBrown => FromArgb(0xFFBC8F8F);
    /// <summary>Gets the named color RoyalBlue (#4169E1).</summary>
    public static Color RoyalBlue => FromArgb(0xFF4169E1);
    /// <summary>Gets the named color SaddleBrown (#8B4513).</summary>
    public static Color SaddleBrown => FromArgb(0xFF8B4513);
    /// <summary>Gets the named color Salmon (#FA8072).</summary>
    public static Color Salmon => FromArgb(0xFFFA8072);
    /// <summary>Gets the named color SandyBrown (#F4A460).</summary>
    public static Color SandyBrown => FromArgb(0xFFF4A460);
    /// <summary>Gets the named color SeaGreen (#2E8B57).</summary>
    public static Color SeaGreen => FromArgb(0xFF2E8B57);
    /// <summary>Gets the named color SeaShell (#FFF5EE).</summary>
    public static Color SeaShell => FromArgb(0xFFFFF5EE);
    /// <summary>Gets the named color Sienna (#A0522D).</summary>
    public static Color Sienna => FromArgb(0xFFA0522D);
    /// <summary>Gets the named color Silver (#C0C0C0).</summary>
    public static Color Silver => FromArgb(0xFFC0C0C0);
    /// <summary>Gets the named color SkyBlue (#87CEEB).</summary>
    public static Color SkyBlue => FromArgb(0xFF87CEEB);
    /// <summary>Gets the named color SlateBlue (#6A5ACD).</summary>
    public static Color SlateBlue => FromArgb(0xFF6A5ACD);
    /// <summary>Gets the named color SlateGray (#708090).</summary>
    public static Color SlateGray => FromArgb(0xFF708090);
    /// <summary>Gets the named color Snow (#FFFAFA).</summary>
    public static Color Snow => FromArgb(0xFFFFFAFA);
    /// <summary>Gets the named color SpringGreen (#00FF7F).</summary>
    public static Color SpringGreen => FromArgb(0xFF00FF7F);
    /// <summary>Gets the named color SteelBlue (#4682B4).</summary>
    public static Color SteelBlue => FromArgb(0xFF4682B4);
    /// <summary>Gets the named color Tan (#D2B48C).</summary>
    public static Color Tan => FromArgb(0xFFD2B48C);
    /// <summary>Gets the named color Teal (#008080).</summary>
    public static Color Teal => FromArgb(0xFF008080);
    /// <summary>Gets the named color Thistle (#D8BFD8).</summary>
    public static Color Thistle => FromArgb(0xFFD8BFD8);
    /// <summary>Gets the named color Tomato (#FF6347).</summary>
    public static Color Tomato => FromArgb(0xFFFF6347);
    /// <summary>Gets the named color Turquoise (#40E0D0).</summary>
    public static Color Turquoise => FromArgb(0xFF40E0D0);
    /// <summary>Gets the named color Violet (#EE82EE).</summary>
    public static Color Violet => FromArgb(0xFFEE82EE);
    /// <summary>Gets the named color Wheat (#F5DEB3).</summary>
    public static Color Wheat => FromArgb(0xFFF5DEB3);
    /// <summary>Gets the named color WhiteSmoke (#F5F5F5).</summary>
    public static Color WhiteSmoke => FromArgb(0xFFF5F5F5);
    /// <summary>Gets the named color YellowGreen (#9ACD32).</summary>
    public static Color YellowGreen => FromArgb(0xFF9ACD32);

    /// <summary>Gets <see cref="Gray"/> under its alternative spelling.</summary>
    public static Color Grey => Gray;
    /// <summary>Gets <see cref="DarkGray"/> under its alternative spelling.</summary>
    public static Color DarkGrey => DarkGray;
    /// <summary>Gets <see cref="LightGray"/> under its alternative spelling.</summary>
    public static Color LightGrey => LightGray;

    private static Dictionary<string, Color> CreateNamedColors()
    {
        return new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Transparent)] = Transparent,
            [nameof(Black)] = Black,
            [nameof(Blue)] = Blue,
            [nameof(Cyan)] = Cyan,
            [nameof(DarkGray)] = DarkGray,
            [nameof(Fuchsia)] = Fuchsia,
            [nameof(Gray)] = Gray,
            [nameof(Green)] = Green,
            [nameof(LightGray)] = LightGray,
            [nameof(Magenta)] = Magenta,
            [nameof(Maroon)] = Maroon,
            [nameof(Orange)] = Orange,
            [nameof(Purple)] = Purple,
            [nameof(Red)] = Red,
            [nameof(White)] = White,
            [nameof(Yellow)] = Yellow,
            [nameof(AliceBlue)] = AliceBlue,
            [nameof(AntiqueWhite)] = AntiqueWhite,
            [nameof(Aqua)] = Aqua,
            [nameof(Aquamarine)] = Aquamarine,
            [nameof(Azure)] = Azure,
            [nameof(Beige)] = Beige,
            [nameof(Bisque)] = Bisque,
            [nameof(BlanchedAlmond)] = BlanchedAlmond,
            [nameof(BlueViolet)] = BlueViolet,
            [nameof(Brown)] = Brown,
            [nameof(BurlyWood)] = BurlyWood,
            [nameof(CadetBlue)] = CadetBlue,
            [nameof(Chartreuse)] = Chartreuse,
            [nameof(Chocolate)] = Chocolate,
            [nameof(Coral)] = Coral,
            [nameof(CornflowerBlue)] = CornflowerBlue,
            [nameof(Cornsilk)] = Cornsilk,
            [nameof(Crimson)] = Crimson,
            [nameof(DarkBlue)] = DarkBlue,
            [nameof(DarkCyan)] = DarkCyan,
            [nameof(DarkGoldenrod)] = DarkGoldenrod,
            [nameof(DarkGreen)] = DarkGreen,
            [nameof(DarkKhaki)] = DarkKhaki,
            [nameof(DarkMagenta)] = DarkMagenta,
            [nameof(DarkOliveGreen)] = DarkOliveGreen,
            [nameof(DarkOrange)] = DarkOrange,
            [nameof(DarkOrchid)] = DarkOrchid,
            [nameof(DarkRed)] = DarkRed,
            [nameof(DarkSalmon)] = DarkSalmon,
            [nameof(DarkSeaGreen)] = DarkSeaGreen,
            [nameof(DarkSlateBlue)] = DarkSlateBlue,
            [nameof(DarkSlateGray)] = DarkSlateGray,
            [nameof(DarkTurquoise)] = DarkTurquoise,
            [nameof(DarkViolet)] = DarkViolet,
            [nameof(DeepPink)] = DeepPink,
            [nameof(DeepSkyBlue)] = DeepSkyBlue,
            [nameof(DimGray)] = DimGray,
            [nameof(DodgerBlue)] = DodgerBlue,
            [nameof(Firebrick)] = Firebrick,
            [nameof(FloralWhite)] = FloralWhite,
            [nameof(ForestGreen)] = ForestGreen,
            [nameof(Gainsboro)] = Gainsboro,
            [nameof(GhostWhite)] = GhostWhite,
            [nameof(Gold)] = Gold,
            [nameof(Goldenrod)] = Goldenrod,
            [nameof(GreenYellow)] = GreenYellow,
            [nameof(Honeydew)] = Honeydew,
            [nameof(HotPink)] = HotPink,
            [nameof(IndianRed)] = IndianRed,
            [nameof(Indigo)] = Indigo,
            [nameof(Ivory)] = Ivory,
            [nameof(Khaki)] = Khaki,
            [nameof(Lavender)] = Lavender,
            [nameof(LavenderBlush)] = LavenderBlush,
            [nameof(LawnGreen)] = LawnGreen,
            [nameof(LemonChiffon)] = LemonChiffon,
            [nameof(LightBlue)] = LightBlue,
            [nameof(LightCoral)] = LightCoral,
            [nameof(LightCyan)] = LightCyan,
            [nameof(LightGoldenrodYellow)] = LightGoldenrodYellow,
            [nameof(LightGreen)] = LightGreen,
            [nameof(LightPink)] = LightPink,
            [nameof(LightSalmon)] = LightSalmon,
            [nameof(LightSeaGreen)] = LightSeaGreen,
            [nameof(LightSkyBlue)] = LightSkyBlue,
            [nameof(LightSlateGray)] = LightSlateGray,
            [nameof(LightSteelBlue)] = LightSteelBlue,
            [nameof(LightYellow)] = LightYellow,
            [nameof(Lime)] = Lime,
            [nameof(LimeGreen)] = LimeGreen,
            [nameof(Linen)] = Linen,
            [nameof(MediumAquamarine)] = MediumAquamarine,
            [nameof(MediumBlue)] = MediumBlue,
            [nameof(MediumOrchid)] = MediumOrchid,
            [nameof(MediumPurple)] = MediumPurple,
            [nameof(MediumSeaGreen)] = MediumSeaGreen,
            [nameof(MediumSlateBlue)] = MediumSlateBlue,
            [nameof(MediumSpringGreen)] = MediumSpringGreen,
            [nameof(MediumTurquoise)] = MediumTurquoise,
            [nameof(MediumVioletRed)] = MediumVioletRed,
            [nameof(MidnightBlue)] = MidnightBlue,
            [nameof(MintCream)] = MintCream,
            [nameof(MistyRose)] = MistyRose,
            [nameof(Moccasin)] = Moccasin,
            [nameof(NavajoWhite)] = NavajoWhite,
            [nameof(Navy)] = Navy,
            [nameof(OldLace)] = OldLace,
            [nameof(Olive)] = Olive,
            [nameof(OliveDrab)] = OliveDrab,
            [nameof(OrangeRed)] = OrangeRed,
            [nameof(Orchid)] = Orchid,
            [nameof(PaleGoldenrod)] = PaleGoldenrod,
            [nameof(PaleGreen)] = PaleGreen,
            [nameof(PaleTurquoise)] = PaleTurquoise,
            [nameof(PaleVioletRed)] = PaleVioletRed,
            [nameof(PapayaWhip)] = PapayaWhip,
            [nameof(PeachPuff)] = PeachPuff,
            [nameof(Peru)] = Peru,
            [nameof(Pink)] = Pink,
            [nameof(Plum)] = Plum,
            [nameof(PowderBlue)] = PowderBlue,
            [nameof(RebeccaPurple)] = RebeccaPurple,
            [nameof(RosyBrown)] = RosyBrown,
            [nameof(RoyalBlue)] = RoyalBlue,
            [nameof(SaddleBrown)] = SaddleBrown,
            [nameof(Salmon)] = Salmon,
            [nameof(SandyBrown)] = SandyBrown,
            [nameof(SeaGreen)] = SeaGreen,
            [nameof(SeaShell)] = SeaShell,
            [nameof(Sienna)] = Sienna,
            [nameof(Silver)] = Silver,
            [nameof(SkyBlue)] = SkyBlue,
            [nameof(SlateBlue)] = SlateBlue,
            [nameof(SlateGray)] = SlateGray,
            [nameof(Snow)] = Snow,
            [nameof(SpringGreen)] = SpringGreen,
            [nameof(SteelBlue)] = SteelBlue,
            [nameof(Tan)] = Tan,
            [nameof(Teal)] = Teal,
            [nameof(Thistle)] = Thistle,
            [nameof(Tomato)] = Tomato,
            [nameof(Turquoise)] = Turquoise,
            [nameof(Violet)] = Violet,
            [nameof(Wheat)] = Wheat,
            [nameof(WhiteSmoke)] = WhiteSmoke,
            [nameof(YellowGreen)] = YellowGreen,
            [nameof(Grey)] = Grey,
            [nameof(DarkGrey)] = DarkGrey,
            [nameof(LightGrey)] = LightGrey,
        };
    }
}
