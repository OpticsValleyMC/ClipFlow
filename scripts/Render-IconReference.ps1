$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies (
    [System.Drawing.Bitmap].Assembly.Location
), (
    [System.Drawing.Color].Assembly.Location
) -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
public static class ClipFlowIconReference {
    static double Distance(double x,double y,double cx,double cy,double w,double h) {
        double dx=Math.Abs(x-cx)-(w/2-3),dy=Math.Abs(y-cy)-(h/2-3);
        return Math.Sqrt(Math.Max(dx,0)*Math.Max(dx,0)+Math.Max(dy,0)*Math.Max(dy,0))+Math.Min(Math.Max(dx,dy),0)-3;
    }
    public static void Save(string path) {
        const int size=512,samples=4;
        using(var image=new Bitmap(size,size,PixelFormat.Format32bppArgb)) {
            for(int y=0;y<size;y++) for(int x=0;x<size;x++) {
                int coverage=0;
                for(int sy=0;sy<samples;sy++) for(int sx=0;sx<samples;sx++) {
                    double px=(x+(sx+.5)/samples)*32/size,py=(y+(sy+.5)/samples)*32/size;
                    double front=Distance(px,py,21,13,16,22),back=Distance(px,py,12,21,16,20);
                    if(Math.Abs(front)<1 || (front>1 && Math.Abs(back)<1)) coverage++;
                }
                image.SetPixel(x,y,Color.FromArgb(coverage*255/(samples*samples),24,158,235));
            }
            image.Save(path,ImageFormat.Png);
            using(var preview=new Bitmap(size,size)) using(var graphics=Graphics.FromImage(preview)) {
                graphics.Clear(Color.White); graphics.DrawImage(image,0,0,size,size);
                preview.Save(System.IO.Path.ChangeExtension(path,"preview.png"),ImageFormat.Png);
            }
        }
    }
}
'@
$assetPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'ClipFlow/ClipFlow/Assets'
[ClipFlowIconReference]::Save((Join-Path $assetPath 'ClipFlow.reference.png'))

