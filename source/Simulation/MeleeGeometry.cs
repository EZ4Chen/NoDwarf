using System.Numerics;
namespace NoDwarf.Simulation;
public static class MeleeGeometry
{
    public static float? Box(Vector3 a,Vector3 b,Vector3 min,Vector3 max)
    {
        float enter=0,exit=1;var d=b-a;
        for(int axis=0;axis<3;axis++)
        {
            float p=axis==0?a.X:axis==1?a.Y:a.Z,q=axis==0?d.X:axis==1?d.Y:d.Z;
            float lo=axis==0?min.X:axis==1?min.Y:min.Z,hi=axis==0?max.X:axis==1?max.Y:max.Z;
            if(Math.Abs(q)<1e-7){if(p<lo||p>hi)return null;continue;}
            float t0=(lo-p)/q,t1=(hi-p)/q;if(t0>t1)(t0,t1)=(t1,t0);
            enter=Math.Max(enter,t0);exit=Math.Min(exit,t1);if(enter>exit)return null;
        }
        return enter;
    }
    public static float? Sphere(Vector3 a,Vector3 b,Vector3 center,float radius)
    {
        var offset=a-center;float c=offset.LengthSquared()-radius*radius;if(c<=0)return 0;
        var d=b-a;float length=d.LengthSquared();if(length<1e-10)return null;
        float dot=Vector3.Dot(offset,d),discriminant=dot*dot-length*c;
        if(discriminant<0)return null;float t=(-dot-MathF.Sqrt(discriminant))/length;
        return t>=0&&t<=1?t:null;
    }
    // A conservative chain of spheres approximates a short capsule without triangle collision.
    public static float? Capsule(Vector3 a,Vector3 b,Vector3 start,Vector3 end,float radius)
    {
        int steps=Math.Max(1,(int)Math.Ceiling(Vector3.Distance(start,end)/.08f));float? nearest=null;
        for(int i=0;i<=steps;i++){var t=Sphere(a,b,Vector3.Lerp(start,end,(float)i/steps),radius+.01f);if(t is float hit&&(nearest is null||hit<nearest))nearest=hit;}
        return nearest;
    }
}
