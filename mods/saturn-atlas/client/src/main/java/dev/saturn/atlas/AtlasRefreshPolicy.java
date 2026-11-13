package dev.saturn.atlas;
/** Only explicit opens/refreshes may request terrain; navigation is local. */
public final class AtlasRefreshPolicy {
 public enum Event { OPEN, MANUAL, ZOOM, PAN, CENTER, CLOSE, TICK }
 private long next=Long.MIN_VALUE;
 public boolean allow(Event event,long now){
  if(event!=Event.OPEN&&event!=Event.MANUAL)return false;
  if(now<next)return false;
  next=now+2_000_000_000L;return true;
 }
 public void reset(){next=Long.MIN_VALUE;}
}
