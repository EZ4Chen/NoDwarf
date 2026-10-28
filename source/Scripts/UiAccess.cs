using Godot;
namespace NoDwarf;

internal static class UiAccess
{
    public static StyleBoxFlat FocusRing()=>new()
    {
        DrawCenter=false,BorderColor=new Color("edcf87"),
        BorderWidthLeft=2,BorderWidthRight=2,BorderWidthTop=2,BorderWidthBottom=2
    };
    public static void Focus(Control control)
    {
        control.FocusMode=Control.FocusModeEnum.All;
        control.AddThemeStyleboxOverride("focus",FocusRing());
    }
    public static T Name<T>(T control,string name,string description="") where T:Control
    {
        control.AccessibilityName=name;control.AccessibilityDescription=description;
        if(control is BaseButton or LineEdit)Focus(control);
        if(control is SpinBox spin)
        {spin.GetLineEdit().AccessibilityName=name;spin.GetLineEdit().AccessibilityDescription=description;Focus(spin.GetLineEdit());}
        return control;
    }
}
