package dev.saturn.atlas;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
class AtlasLayoutTest {
 @Test void supportedGuiSizesHaveNoOverlapsOrOffscreenControls(){
  for(int[] size:new int[][]{{380,200},{400,220},{380,240},{400,240},{480,270},{640,360},{960,540},{1280,720}}){var l=AtlasLayout.of(size[0],size[1]);assertFalse(l.compact());var controls=new AtlasLayout.Rect[]{l.refresh(),l.held(),l.quote(),l.confirm(),l.close()};for(var r:controls){assertTrue(r.x()>=0&&r.y()>=0&&r.right()<=l.width()&&r.bottom()<=l.height());assertFalse(r.overlaps(l.map()));}for(int a=0;a<controls.length;a++)for(int b=a+1;b<controls.length;b++)assertFalse(controls[a].overlaps(controls[b]),size[0]+"x"+size[1]);assertFalse(l.map().overlaps(l.sidebar()));assertTrue(l.map().w()>=80);}
 }
 @Test void smallGuiHasExplicitCompactState(){for(int[] size:new int[][]{{320,180},{400,190},{360,240}}){var l=AtlasLayout.of(size[0],size[1]);assertTrue(l.compact());assertFalse(l.refresh().overlaps(l.close()));assertTrue(l.close().right()<=size[0]);assertEquals(0,l.confirm().w());}}
}
