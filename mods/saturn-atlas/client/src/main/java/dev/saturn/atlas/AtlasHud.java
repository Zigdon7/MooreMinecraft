package dev.saturn.atlas;
/** Testable minimap placement and bounded refresh scheduling. */
public final class AtlasHud {
 private long nextRequest;
 public static AtlasLayout.Rect bounds(int width,int height){int size=Math.min(96,Math.max(40,Math.min(width/4,height/3)));return new AtlasLayout.Rect(width-size-8,8,size,size);}
 public boolean due(long now,boolean connected,boolean supported,boolean screenAllowed){if(!connected||!supported||!screenAllowed||now<nextRequest)return false;nextRequest=now+5_000_000_000L;return true;}
 public void reply(long now){nextRequest=now+5_000_000_000L;}
 public void reset(){nextRequest=0;}
}
