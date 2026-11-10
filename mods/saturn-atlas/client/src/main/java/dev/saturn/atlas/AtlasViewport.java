package dev.saturn.atlas;
/** Aspect-correct rectangular map projection, centered on the current chunk. */
public record AtlasViewport(double startX,double startZ,double scale,int width,int height) {
 public static AtlasViewport of(double playerX,double playerZ,int width,int height){
  if(width<=0||height<=0)throw new IllegalArgumentException("Empty viewport");
  double scale=Math.min(width,height)/144.0;
  double cx=Math.floor(playerX/16)*16+8,cz=Math.floor(playerZ/16)*16+8;
  return new AtlasViewport(cx-width/(2*scale),cz-height/(2*scale),scale,width,height);
 }
 public int x(double worldX){return (int)Math.floor((worldX-startX)*scale);}
 public int z(double worldZ){return (int)Math.floor((worldZ-startZ)*scale);}
 public boolean contains(int x,int z,int margin){return x>=margin&&z>=margin&&x<width-margin&&z<height-margin;}
}
