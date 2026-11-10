package dev.saturn.atlas;
/** All coordinates in scaled GUI units. */
public record AtlasLayout(int width,int height,boolean compact,Rect map,Rect sidebar,Rect refresh,Rect held,Rect quote,Rect confirm,Rect close) {
 public record Rect(int x,int y,int w,int h){public int right(){return x+w;}public int bottom(){return y+h;}public boolean overlaps(Rect b){return x<b.right()&&right()>b.x&&y<b.bottom()&&bottom()>b.y;}}
 public static AtlasLayout of(int width,int height){
  boolean compact=width<380||height<200;
  if(compact)return new AtlasLayout(width,height,true,new Rect(12,60,0,0),new Rect(12,60,0,0),new Rect(12,height-28,90,20),new Rect(0,0,0,0),new Rect(0,0,0,0),new Rect(0,0,0,0),new Rect(width-82,height-28,70,20));
  int side=144,sx=width-side-12;
  return new AtlasLayout(width,height,false,new Rect(12,30,width-side-36,height-64),new Rect(sx,38,side,height-118),new Rect(12,height-28,86,20),new Rect(104,height-28,100,20),new Rect(sx,height-52,68,20),new Rect(sx+74,height-52,70,20),new Rect(width-82,height-28,70,20));
 }
}
