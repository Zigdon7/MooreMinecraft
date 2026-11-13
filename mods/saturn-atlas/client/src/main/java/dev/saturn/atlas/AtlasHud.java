package dev.saturn.atlas;
/** Testable minimap placement and bounded refresh scheduling. */
public final class AtlasHud {
 private long nextRequest;
 public static AtlasLayout.Rect bounds(int width,int height){int size=Math.min(96,Math.max(40,Math.min(width/4,height/3)));return new AtlasLayout.Rect(width-size-8,8,size,size);}
 public static AtlasLayout.Rect bounds(int width,int height,int sizeChoice,int corner){
  int preferred=switch(sizeChoice){case 0->64;case 2->128;default->96;};
  int size=Math.max(0,Math.min(preferred,Math.min(width-16,height-38)));
  return new AtlasLayout.Rect(corner%2==1?8:width-size-8,corner>=2?height-size-30:8,size,size);
 }
 public boolean due(long now,boolean connected,boolean supported,boolean screenAllowed){return false;}
 public void reply(long now){}
 public void reset(){}
}
