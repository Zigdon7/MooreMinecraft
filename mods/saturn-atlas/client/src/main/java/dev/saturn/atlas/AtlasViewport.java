package dev.saturn.atlas;
/** Aspect-correct rectangular map projection, centered on the current map tile (zoom x zoom chunks). */
public record AtlasViewport(double startX,double startZ,double scale,int width,int height) {
 public static AtlasViewport of(double playerX,double playerZ,int width,int height){
  return of(playerX,playerZ,width,height,1);
 }
 public static AtlasViewport of(double playerX,double playerZ,int width,int height,int zoom){
  if(width<=0||height<=0||zoom<=0)throw new IllegalArgumentException("Empty viewport");
  double tile=16.0*zoom,scale=Math.min(width,height)/(9*tile);
  double cx=Math.floor(playerX/tile)*tile+tile/2,cz=Math.floor(playerZ/tile)*tile+tile/2;
  return new AtlasViewport(cx-width/(2*scale),cz-height/(2*scale),scale,width,height);
 }
 public int x(double worldX){return (int)Math.floor((worldX-startX)*scale);}
 public int z(double worldZ){return (int)Math.floor((worldZ-startZ)*scale);}
 public boolean contains(int x,int z,int margin){return x>=margin&&z>=margin&&x<width-margin&&z<height-margin;}
}
