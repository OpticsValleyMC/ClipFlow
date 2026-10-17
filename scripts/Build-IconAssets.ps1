param(
    [string]$SourceImage
)

$ErrorActionPreference = 'Stop'

$assetPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'ClipFlow/ClipFlow/Assets'

if ($SourceImage) {
    Copy-Item -LiteralPath $SourceImage -Destination (Join-Path $assetPath 'ClipFlow.master.png')
}

Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies (
    [System.Drawing.Bitmap].Assembly.Location
), (
    [System.Drawing.Color].Assembly.Location
) -TypeDefinition @'
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
public static class ClipFlowIconAssets {
    static byte[] Render(Image source,int width,int height,int symbolSize) {
        using(var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb)) {
            using(var graphics=Graphics.FromImage(bitmap)) using(var attributes=new ImageAttributes()) {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode=CompositingMode.SourceCopy;
                graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(source,new Rectangle((width-symbolSize)/2,(height-symbolSize)/2,symbolSize,symbolSize),0,0,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
            }
            using(var memory=new MemoryStream()) { bitmap.Save(memory,ImageFormat.Png); return memory.ToArray(); }
        }
    }
    public static void Build(string directory) {
        using(var source=Image.FromFile(Path.Combine(directory,"ClipFlow.master.png"))) {
            int[] sizes={16,20,24,30,32,36,40,48,60,64,72,80,96,128,256};
            byte[][] entries=new byte[sizes.Length][];
            for(int i=0;i<sizes.Length;i++) {
                int size=sizes[i]; entries[i]=Render(source,size,size,size);
                File.WriteAllBytes(Path.Combine(directory,"ClipFlow."+size+".png"),entries[i]);
                foreach(string suffix in new[]{"","_altform-unplated","_altform-lightunplated"})
                    File.WriteAllBytes(Path.Combine(directory,"Square44x44Logo.targetsize-"+size+suffix+".png"),entries[i]);
            }
            using(var file=File.Create(Path.Combine(directory,"ClipFlow.ico"))) using(var writer=new BinaryWriter(file)) {
                writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
                int offset=6+16*sizes.Length;
                for(int i=0;i<sizes.Length;i++) {
                    writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                    writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);
                    writer.Write(entries[i].Length);writer.Write(offset);offset+=entries[i].Length;
                }
                foreach(var entry in entries)writer.Write(entry);
            }
            foreach(int scale in new[]{100,125,150,200,250,300,400}) {
                foreach(var pair in new[]{Tuple.Create("Square44x44Logo",44),Tuple.Create("Square71x71Logo",71),Tuple.Create("Square150x150Logo",150),Tuple.Create("Square310x310Logo",310),Tuple.Create("StoreLogo",50),Tuple.Create("LockScreenLogo",24)}) {
                    int size=(int)Math.Round(pair.Item2*scale/100.0,MidpointRounding.AwayFromZero);
                    var bytes=Render(source,size,size,size);
                    File.WriteAllBytes(Path.Combine(directory,pair.Item1+".scale-"+scale+".png"),bytes);
                    if(scale==100)File.WriteAllBytes(Path.Combine(directory,pair.Item1+".png"),bytes);
                }
                foreach(var pair in new[]{Tuple.Create("Wide310x150Logo",310,150,120),Tuple.Create("SplashScreen",620,300,150)}) {
                    int w=(int)Math.Round(pair.Item2*scale/100.0,MidpointRounding.AwayFromZero);
                    int h=(int)Math.Round(pair.Item3*scale/100.0,MidpointRounding.AwayFromZero);
                    var bytes=Render(source,w,h,pair.Item4*scale/100);
                    File.WriteAllBytes(Path.Combine(directory,pair.Item1+".scale-"+scale+".png"),bytes);
                    if(scale==100)File.WriteAllBytes(Path.Combine(directory,pair.Item1+".png"),bytes);
                }
            }
        }
    }
}
'@
[ClipFlowIconAssets]::Build($assetPath)
Write-Output "Icon master, multi-size ICO and Windows package PNG assets: $assetPath"
