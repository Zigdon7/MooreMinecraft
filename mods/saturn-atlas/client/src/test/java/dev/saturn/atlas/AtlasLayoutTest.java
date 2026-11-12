package dev.saturn.atlas;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
class AtlasLayoutTest {
 @Test void supportedGuiSizesHaveNoOverlapsOrOffscreenControls(){
  for(int[] size:new int[][]{{380,200},{400,220},{380,240},{400,240},{480,270},{640,360},{960,540},{1280,720}}){var l=AtlasLayout.of(size[0],size[1]);assertFalse(l.compact());var controls=new AtlasLayout.Rect[]{l.refresh(),l.held(),l.quote(),l.confirm(),l.close(),l.zoomOut(),l.zoomIn()};for(var r:controls){assertTrue(r.w()>=20&&r.h()>=20);assertTrue(r.x()>=0&&r.y()>=0&&r.right()<=l.width()&&r.bottom()<=l.height());assertFalse(r.overlaps(l.map()));}for(int a=0;a<controls.length;a++)for(int b=a+1;b<controls.length;b++)assertFalse(controls[a].overlaps(controls[b]),size[0]+"x"+size[1]);assertFalse(l.map().overlaps(l.sidebar()));assertTrue(l.map().w()>=80);}
 }
 @Test void narrowCompactControlsRemainSeparated(){
  for(int width:new int[]{120,160,180,240,320})for(int height:new int[]{80,120,180}){
   var l=AtlasLayout.of(width,height);assertTrue(l.compact());assertFalse(l.refresh().overlaps(l.close()));
   for(var r:new AtlasLayout.Rect[]{l.refresh(),l.close()}){assertTrue(r.x()>=0&&r.right()<=width);assertTrue(r.y()>=0&&r.bottom()<=height);}
  }
 }
 @Test void smallGuiHasExplicitCompactState(){for(int[] size:new int[][]{{320,180},{400,190},{360,240}}){var l=AtlasLayout.of(size[0],size[1]);assertTrue(l.compact());assertFalse(l.refresh().overlaps(l.close()));assertTrue(l.close().right()<=size[0]);assertTrue(l.confirm().w()>0);assertTrue(l.quote().w()>0);assertTrue(l.sidebar().w()>0);}}
 @Test void compactTravelAndDestinationControlsFitAndDoNotOverlap(){
  for(int[] size:new int[][]{{240,150},{320,150},{240,160},{320,180},{360,180},{400,190}}){
   var l=AtlasLayout.of(size[0],size[1]);var controls=new AtlasLayout.Rect[]{l.refresh(),l.close(),l.quote(),l.confirm(),l.sidebar()};
   for(var r:controls)assertTrue(r.x()>=0&&r.y()>=0&&r.right()<=size[0]&&r.bottom()<=size[1]);
   for(int a=0;a<controls.length;a++)for(int b=a+1;b<controls.length;b++)assertFalse(controls[a].overlaps(controls[b]),size[0]+"x"+size[1]);
  }
 }
 @Test void utilityScreenControlsFitCompactSizesWithoutOverlap(){
  for(int[] size:new int[][]{{240,150},{320,150},{320,180},{400,190},{640,360}})for(boolean settings:new boolean[]{false,true}){
   var controls=AtlasLayout.utilityControls(size[0],size[1],settings);
   for(var r:controls)assertTrue(r.x()>=0&&r.y()>=0&&r.right()<=size[0]&&r.bottom()<=size[1]);
   for(int a=0;a<controls.length;a++)for(int b=a+1;b<controls.length;b++)assertFalse(controls[a].overlaps(controls[b]));
   if(!settings)assertTrue(controls[0].y()>=98,"Keep text body above navigation");
  }
 }
 @Test void belowDocumentedUtilityMinimumOnlyBackControlRemains(){
  assertTrue(AtlasLayout.utilitySupported(240,150));
  for(int[] size:new int[][]{{120,80},{240,120},{320,133},{239,150},{20,20}})for(boolean settings:new boolean[]{false,true}){
   assertFalse(AtlasLayout.utilitySupported(size[0],size[1]));
   var r=AtlasLayout.utilityControls(size[0],size[1],settings);assertEquals(1,r.length);
   assertTrue(r[0].x()>=0&&r[0].y()>=0&&r[0].right()<=size[0]&&r[0].bottom()<=size[1]);
  }
 }
}
