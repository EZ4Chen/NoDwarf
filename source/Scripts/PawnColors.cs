using Godot;
namespace NoDwarf;

/// <summary>Material-aware palette replacement; outlines and underwear keep their original colors.</summary>
public static class PawnColors
{
    public static Color Skin(string value)=>Parse(value,value switch
    {"porcelain"=>"f5d2b3","tan"=>"c68d60","brown"=>"926141","deep"=>"593c30",_=>"efb17f"});
    public static Color Hair(string value)=>Parse(value,value switch
    {"black"=>"302d30","gold"=>"cfaa61","auburn"=>"a45437","silver"=>"918d88","white"=>"e9e3d6",_=>"76543b"});
    private static Color Parse(string value,string fallback)=>new(value is {Length:7}&&value[0]=='#'?value:fallback);

    public static Image Recolor(Image image,Color target,string material)
    {
        image.Convert(Image.Format.Rgba8);
        byte[] data=image.GetData();
        float baseline=material=="skin"?.2126f*.937f+.7152f*.694f+.0722f*.498f:material=="beard"?.48f:.2126f*.463f+.7152f*.329f+.0722f*.231f;
        for(int i=0;i<data.Length;i+=4)
        {
            if(data[i+3]==0)continue;
            float r=data[i]/255f,g=data[i+1]/255f,b=data[i+2]/255f;
            float l=.2126f*r+.7152f*g+.0722f*b;
            if(material=="skin")
            {
                // Warm peach fill only; cream underwear, white highlights and
                // the dark face/outline ink must not acquire the skin color.
                if(r<.52f||r<g*1.12f||g<b*1.15f)continue;
            }
            else
            {
                if(l<.13f)continue;
                // Preserve the light leather ties on braids/ponytails.
                if(material=="hair"&&g>.46f&&r>g*1.2f&&b<g*.8f)continue;
            }
            float shade=Math.Clamp(l/baseline,.38f,1.3f);
            float blend=material=="skin"?1:Math.Clamp((l-.13f)/.09f,0,1);
            data[i]=(byte)Math.Clamp((int)(Mathf.Lerp(r,target.R*shade,blend)*255),0,255);
            data[i+1]=(byte)Math.Clamp((int)(Mathf.Lerp(g,target.G*shade,blend)*255),0,255);
            data[i+2]=(byte)Math.Clamp((int)(Mathf.Lerp(b,target.B*shade,blend)*255),0,255);
        }
        return Image.CreateFromData(image.GetWidth(),image.GetHeight(),false,Image.Format.Rgba8,data);
    }
}
