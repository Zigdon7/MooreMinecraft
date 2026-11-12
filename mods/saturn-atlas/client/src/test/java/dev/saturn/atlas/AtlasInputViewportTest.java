package dev.saturn.atlas;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
import net.minecraft.client.input.KeyEvent;
class AtlasInputViewportTest {
 @Test void defaultMappingIsMAndDoesNotMatchOtherKeys(){var key=SaturnAtlasClient.createOpenKey();assertEquals(com.mojang.blaze3d.platform.InputConstants.KEY_M,key.getDefaultKey().getValue());assertEquals("key.keyboard.m",key.getDefaultKey().getName());assertTrue(key.matches(new KeyEvent(com.mojang.blaze3d.platform.InputConstants.KEY_M,0,0)));assertFalse(key.matches(new KeyEvent(com.mojang.blaze3d.platform.InputConstants.KEY_N,0,0)));}
 @Test void mappingCanBeRebound(){var key=SaturnAtlasClient.createOpenKey();key.setKey(com.mojang.blaze3d.platform.InputConstants.Type.KEYBOARD.getOrCreate(com.mojang.blaze3d.platform.InputConstants.KEY_N));assertTrue(key.matches(new KeyEvent(com.mojang.blaze3d.platform.InputConstants.KEY_N,0,0)));assertFalse(key.matches(new KeyEvent(com.mojang.blaze3d.platform.InputConstants.KEY_M,0,0)));}
 @Test void optionalKeysAreUnboundWithAValidKeyCode(){var key=SaturnAtlasClient.createUnboundKey("key.saturn_atlas.test_unbound");assertTrue(key.isUnbound());assertEquals("key.keyboard.unknown",key.saveString());assertTrue(key.getDefaultKey().getValue()>=0);assertFalse(SaturnAtlasClient.repairKey(key));}
 @Test void invalidSavedKeyIsUnboundInsteadOfCrashingOnMouseGrab(){var key=SaturnAtlasClient.createUnboundKey("key.saturn_atlas.test_repair");key.setKey(com.mojang.blaze3d.platform.InputConstants.getKey("key.keyboard.-1"));assertFalse(key.isUnbound());assertTrue(SaturnAtlasClient.repairKey(key));assertTrue(key.isUnbound());assertEquals("key.keyboard.unknown",key.saveString());var open=SaturnAtlasClient.createOpenKey();assertFalse(SaturnAtlasClient.repairKey(open));assertEquals("key.keyboard.m",open.saveString());}
 @Test void largerScreenshotSizedMap(){var l=AtlasLayout.of(400,220);assertTrue(l.map().w()>200);assertTrue(l.map().h()>=150);assertTrue(l.map().w()*l.map().h()>108*108*2);}
 @Test void rectangularProjectionKeepsScaleAndCenter(){var v=AtlasViewport.of(0,0,220,156);assertEquals(v.scale()*16,v.x(24)-v.x(8),1);assertEquals(v.scale()*16,v.z(24)-v.z(8),1);assertEquals(110,v.x(8));assertEquals(78,v.z(8));}
 @Test void zoomedProjectionShowsNineTilesAcrossTheShorterSideCenteredOnThePlayersTile(){
  for(int zoom:new int[]{1,2,4,8,16}){var v=AtlasViewport.of(-0.5,1000,220,156,zoom);int tile=16*zoom;
   assertEquals(156.0/(9*tile),v.scale(),1e-9);assertEquals(110,v.x(-tile/2.0));assertEquals(78,v.z(Math.floor(1000.0/tile)*tile+tile/2.0));
   assertEquals(0,v.z((Math.floorDiv(1000,tile)-4)*tile),"first row of the 9 x 9 snapshot starts at the top edge");assertTrue(v.contains(v.x(-0.5),v.z(1000),3));}
  assertEquals(AtlasViewport.of(5,5,200,100),AtlasViewport.of(5,5,200,100,1));assertThrows(IllegalArgumentException.class,()->AtlasViewport.of(0,0,10,10,0));
 }
 @Test void draggedProjectionIsCentredExactlyOnTheViewAtTheSameScale(){
  for(int zoom:new int[]{1,4,16}){var v=AtlasViewport.centered(-37.5,912.25,220,156,zoom);assertEquals(AtlasViewport.of(0,0,220,156,zoom).scale(),v.scale(),1e-12);assertEquals(110,v.x(-37.5));assertEquals(78,v.z(912.25));}
  assertThrows(IllegalArgumentException.class,()->AtlasViewport.centered(0,0,0,10,1));
 }
 @Test void recenterButtonSitsInTheHeaderClearOfOtherControls(){
  for(int[] size:new int[][]{{380,200},{427,240},{640,360},{960,540}}){var l=AtlasLayout.of(size[0],size[1]);var r=l.recenter();assertTrue(r.w()>0&&r.x()>=120,"room for the title at "+size[0]);
   var text=new AtlasLayout.Rect(size[0]-108,7,46,18);assertFalse(r.overlaps(text));for(var other:new AtlasLayout.Rect[]{l.map(),l.sidebar(),l.refresh(),l.held(),l.quote(),l.confirm(),l.close(),l.zoomIn(),l.zoomOut()})assertFalse(r.overlaps(other),size[0]+"x"+size[1]);}
  assertEquals(0,AtlasLayout.of(300,180).recenter().w(),"compact layout has no map to drag");
 }
 @Test void negativeCoordinateProjectionStaysCentered(){var v=AtlasViewport.of(-0.5,-16.5,220,156);assertEquals(110,v.x(-8));assertEquals(78,v.z(-24));assertTrue(v.contains(v.x(-0.5),v.z(-16.5),3));assertFalse(v.contains(10,156,3));}
}
