package dev.saturn.atlas;
/** All coordinates in scaled GUI units. */
public record AtlasLayout(int width,int height,boolean compact,Rect map,Rect sidebar,Rect refresh,Rect held,Rect quote,Rect confirm,Rect close) {
 public record Rect(int x,int y,int w,int h){public int right(){return x+w;}public int bottom(){return y+h;}public boolean overlaps(Rect b){return x<b.right()&&right()>b.x&&y<b.bottom()&&bottom()>b.y;}}
 /** Zoom controls sit in the footer between Held Atlas and Close; absent in the compact layout. */
 public Rect zoomOut(){return compact?new Rect(0,0,0,0):new Rect(210,height-28,20,20);}
 public Rect zoomIn(){return compact?new Rect(0,0,0,0):new Rect(234,height-28,20,20);}
 public static boolean utilitySupported(int width,int height){return width>=240&&height>=150;}
 public static Rect[] utilityControls(int width,int height,boolean settings){
  if(!utilitySupported(width,height)){int x=Math.min(12,Math.max(0,width/2));return new Rect[]{new Rect(x,Math.max(0,height-28),Math.max(0,width-2*x),Math.min(20,Math.max(0,height)))};}
  int w=(width-30)/2;
  return settings?new Rect[]{new Rect(12,38,w,20),new Rect(18+w,38,w,20),new Rect(12,62,w,20),new Rect(18+w,62,w,20),new Rect(12,86,width-24,20),new Rect(12,height-28,width-24,20)}
   :new Rect[]{new Rect(12,height-52,w,20),new Rect(18+w,height-52,w,20),new Rect(12,height-28,w,20),new Rect(18+w,height-28,w,20)};
 }
 public static AtlasLayout of(int width,int height){
  boolean compact=width<380||height<200;
  if(compact){
   int buttonWidth=Math.max(0,Math.min(90,(width-36)/2)),y=Math.max(0,height-28),buttonHeight=Math.min(20,Math.max(0,height));
   return new AtlasLayout(width,height,true,new Rect(12,60,0,0),new Rect(12,38,Math.max(0,width-24),(height<160?20:Math.max(44,height-118))),new Rect(12,y,buttonWidth,buttonHeight),new Rect(0,0,0,0),new Rect(12,Math.max(0,height-52),Math.max(0,(width-30)/2),20),new Rect(18+Math.max(0,(width-30)/2),Math.max(0,height-52),Math.max(0,(width-30)/2),20),new Rect(Math.max(12,width-12-buttonWidth),y,buttonWidth,buttonHeight));
  }
  int side=144,sx=width-side-12;
  return new AtlasLayout(width,height,false,new Rect(12,30,width-side-36,height-64),new Rect(sx,38,side,height-118),new Rect(12,height-28,86,20),new Rect(104,height-28,100,20),new Rect(sx,height-52,68,20),new Rect(sx+74,height-52,70,20),new Rect(width-82,height-28,70,20));
 }
}
