using Godot;
using NoDwarf.Simulation;

namespace NoDwarf;

// Original paper-doll art, composed in the same 80×104 canvas as resident.svg.
// Feet and height stay fixed so cosmetic body types never alter navigation.
public static class CharacterArt
{
    public static ImageTexture Texture(CharacterAppearance appearance)
    {
        var image = new Image();
        var error = image.LoadSvgFromString(Svg(appearance), 3);
        if (error != Error.Ok) throw new InvalidOperationException($"Character SVG: {error}");
        return ImageTexture.CreateFromImage(image);
    }

    public static string Svg(CharacterAppearance appearance,string clothing="",string headgear="")
    {
        var a = appearance.Normalize();
        string skin = a.Skin switch { "porcelain" => "f5d9bd", "tan" => "ce986c", "brown" => "a36c49", "deep" => "704a37", _ => "efc39d" };
        string hair = a.HairColor switch { "black" => "302d30", "gold" => "cfaa61", "auburn" => "a45437", "silver" => "9b9d99", "white" => "e4dfd0", _ => "694b38" };
        string cloth = a.Cloth switch { "ochre" => "bd995a", "blue" => "728f9f", "plum" => "98758e", "rust" => "b57556", "cream" => "ded5bd", _ => "9aa991" };
        int left = a.Body == "slim" ? 26 : a.Body == "broad" ? 18 : 23;
        int right = 80 - left;
        string face = a.Face switch
        {
            "oval" => $"<ellipse cx='40' cy='29' rx='20' ry='26' fill='#{skin}' stroke='#553e32' stroke-width='2'/>",
            "square" => $"<rect x='18' y='5' width='44' height='47' rx='14' fill='#{skin}' stroke='#553e32' stroke-width='2'/>",
            _ => $"<ellipse cx='40' cy='29' rx='23' ry='25' fill='#{skin}' stroke='#553e32' stroke-width='2'/>"
        };
        string backHair = a.Hair switch
        {
            "bob" => $"<path d='M15 48V23Q15 0 40 2q27-2 26 25v23z' fill='#{hair}'/>",
            "braid" => $"<path d='M55 22q15 8 7 27l2 11-5 7-7-9 3-12z' fill='#{hair}' stroke='#553e32' stroke-width='2'/>",
            _ => ""
        };
        string frontHair = a.Hair switch
        {
            "bald" => "",
            "sweep" => "M17 29Q9 1 39 2q31-2 25 30L57 14Q38 35 20 25z",
            "bob" => "M17 33V21Q13 0 40 2q28-2 24 31l-9-17q-15 11-30 0z",
            "braid" => "M17 30Q11 1 39 2q29-2 25 30L54 14 41 20 26 14z",
            _ => "M17 29Q9 1 39 2q31-2 25 30l-8-18-17 7-12-6-8 19z"
        };
        string outfit = a.Outfit switch
        {
            "tunic" => $"<path d='M{left} 48Q40 41 {right} 48l5 35H{left-5}z' fill='#{cloth}'/>",
            "vest" => $"<path d='M{left} 48l10-3 7 17 7-17 10 3 4 34H{left-4}z' fill='#{cloth}'/><path d='M40 62v19' stroke='#553e32' stroke-width='2'/>",
            "coat" => $"<path d='M{left} 48Q40 41 {right} 48l7 41H{left-7}z' fill='#{cloth}'/><path d='M34 47l6 9 6-9M40 56v32' fill='none' stroke='#e9dfc6' stroke-width='3'/>",
            _ => $"<path d='M{left+4} 51h{right-left-8}v30H{left+4}z' fill='#{cloth}'/>"
        };
        string beard = a.Beard switch
        {
            "stubble" => $"<path d='M24 39q16 16 32 0-2 17-16 15Q26 54 24 39' fill='#{hair}' opacity='.7'/>",
            "full" => $"<path d='M19 35l9 7 12-4 12 4 9-7-6 22-15 9-15-9z' fill='#{hair}'/>",
            _ => ""
        };
        return $"""
        <svg xmlns="http://www.w3.org/2000/svg" width="80" height="104" viewBox="0 0 80 104">
        <ellipse cx="40" cy="96" rx="23" ry="5" fill="#192f2d" fill-opacity=".2"/>
        {backHair}
        <path d="M25 76h12v20H23zm18 0h12l4 20H43" fill="#383d43"/>
        <path d="M21 94h17v7H19zm24 0h17v7H44" fill="#49362e"/>
        <path d="M{left} 48Q40 41 {right} 48l5 34H{left-5}z" fill="#f1ecdc"/>
        <path d="M{left} 52l-9 23 9 5 11-24M{right} 52l9 23-9 5-11-24" fill="#{skin}" stroke="#553e32" stroke-width="2"/>
        {outfit}
        <path d="M{left+4} 68h{right-left-8}" stroke="#655443" stroke-width="3"/>
        {clothing}
        <rect x="33" y="41" width="14" height="13" rx="5" fill="#{skin}"/>
        {face}
        <path d="{frontHair}" fill="#{hair}"/>
        {headgear}
        <ellipse cx="32" cy="31" rx="2.2" ry="3" fill="#38332e"/><ellipse cx="49" cy="31" rx="2.2" ry="3" fill="#38332e"/>
        {beard}
        <path d="M36 41q5 3 9-1" fill="none" stroke="#96634b" stroke-width="2" stroke-linecap="round"/>
        </svg>
        """;
    }
}
